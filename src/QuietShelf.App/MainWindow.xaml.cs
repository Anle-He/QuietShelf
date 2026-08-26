using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using QuietShelf.Converters;
using QuietShelf.Data;
using QuietShelf.Models;

namespace QuietShelf;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly Database _database = new();
    private LibraryRepository? _repository;
    private IReadOnlyList<MediaWork> _allWorks = [];
    private string _kindFilter = "all";
    private string? _selectedWorkId;
    private MediaWork? _selectedWork;
    private MediaExperience? _activeExperience;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += MainWindow_Loaded;
    }

    public ObservableCollection<MediaWork> VisibleWorks { get; } = [];
    public ObservableCollection<MediaExperience> CompletedExperiences { get; } = [];
    public ObservableCollection<ProgressEntry> ProgressEntries { get; } = [];

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _database.InitializeAsync();
            _repository = new LibraryRepository(_database);
            await ReloadLibraryAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show($"无法打开本地作品库。\n\n{exception.Message}", "QuietShelf", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AddWork_Click(object sender, RoutedEventArgs e)
    {
        if (_repository is null)
        {
            return;
        }

        var dialog = new AddWorkWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Work is null)
        {
            return;
        }

        var existing = _allWorks.FirstOrDefault(work =>
            work.Kind == dialog.Work.Kind && string.Equals(work.Title, dialog.Work.Title, StringComparison.CurrentCultureIgnoreCase));
        if (existing is not null)
        {
            var choice = MessageBox.Show(
                $"《{existing.Title}》已经存在。\n\n选择“是”打开已有作品；选择“否”仍创建一个同名作品。",
                "已有同名作品", MessageBoxButton.YesNoCancel, MessageBoxImage.Information);
            if (choice == MessageBoxResult.Yes)
            {
                SearchBox.Clear();
                await ReloadLibraryAsync(existing.Id);
                return;
            }
            if (choice != MessageBoxResult.No)
            {
                return;
            }
        }

        await _repository.AddWorkAsync(dialog.Work);
        SearchBox.Clear();
        await ReloadLibraryAsync(dialog.Work.Id);
    }

    private async void WorkList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorkList.SelectedItem is not MediaWork work)
        {
            if (VisibleWorks.Count == 0)
            {
                ShowNoSelection();
            }
            return;
        }

        _selectedWorkId = work.Id;
        await LoadSelectedWorkAsync(work.Id);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not string filter)
        {
            return;
        }
        _kindFilter = filter;
        UpdateFilterButtons();
        ApplyFilters();
    }

    private async Task ReloadLibraryAsync(string? selectWorkId = null)
    {
        if (_repository is null)
        {
            return;
        }
        _allWorks = await _repository.GetWorksAsync();
        if (selectWorkId is not null)
        {
            _selectedWorkId = selectWorkId;
        }
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var query = SearchBox?.Text.Trim() ?? string.Empty;
        var matches = _allWorks.Where(work =>
            (_kindFilter == "all" || work.Kind == _kindFilter) &&
            (string.IsNullOrWhiteSpace(query)
             || work.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
             || (work.Subtitle?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false))).ToList();

        VisibleWorks.Clear();
        foreach (var work in matches)
        {
            VisibleWorks.Add(work);
        }

        if (EmptyState is null || WorkList is null)
        {
            return;
        }
        EmptyState.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        WorkList.Visibility = matches.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyTitle.Text = _allWorks.Count == 0 ? "这里还没有作品" : "没有找到相符的作品";
        EmptyDescription.Text = _allWorks.Count == 0
            ? "先加入一本书，或一部想留下来的影视。"
            : "换一个标题关键词或类别试试。";

        var selected = matches.FirstOrDefault(work => work.Id == _selectedWorkId);
        if (selected is null && matches.Count > 0 && string.IsNullOrWhiteSpace(query))
        {
            selected = matches[0];
        }
        WorkList.SelectedItem = selected;
        if (selected is null)
        {
            ShowNoSelection();
        }
    }

    private void UpdateFilterButtons()
    {
        foreach (var button in new[] { AllFilterButton, BookFilterButton, ScreenFilterButton })
        {
            button.Appearance = string.Equals(button.Tag?.ToString(), _kindFilter, StringComparison.Ordinal)
                ? Wpf.Ui.Controls.ControlAppearance.Primary
                : Wpf.Ui.Controls.ControlAppearance.Secondary;
        }
    }

    private async Task LoadSelectedWorkAsync(string workId)
    {
        if (_repository is null)
        {
            return;
        }

        _selectedWork = await _repository.GetWorkAsync(workId);
        if (_selectedWork is null)
        {
            ShowNoSelection();
            return;
        }

        var covers = await _repository.GetCoversAsync(workId);
        var allExperiences = await _repository.GetExperiencesAsync(workId);
        _activeExperience = allExperiences.FirstOrDefault(experience => experience.StartedOn is not null && experience.CompletedOn is null);

        RenderCoverStack(covers);
        DetailTitleText.Text = _selectedWork.Title;
        DetailSubtitleText.Text = _selectedWork.Subtitle ?? string.Empty;
        DetailSubtitleText.Visibility = string.IsNullOrWhiteSpace(_selectedWork.Subtitle)
            ? Visibility.Collapsed
            : Visibility.Visible;
        DetailMetaText.Text = $"{_selectedWork.KindLabel} · {_selectedWork.StatusLabel} · {_selectedWork.LatestActivityLabel}";
        DetailRankText.Text = _selectedWork.AggregateRankLabel;
        DetailCountText.Text = _selectedWork.ExperienceCountLabel;
        DetailRatingCountText.Text = _selectedWork.RatingCountLabel;
        PrimaryExperienceButton.Content = _activeExperience is not null
            ? "完成本次"
            : _selectedWork.Kind == "book"
                ? (_selectedWork.ExperienceCount == 0 ? "开始阅读" : "再读一次")
                : (_selectedWork.ExperienceCount == 0 ? "开始观看" : "再看一次");

        ProgressEntries.Clear();
        ActiveExperiencePanel.Visibility = _activeExperience is null ? Visibility.Collapsed : Visibility.Visible;
        if (_activeExperience is not null)
        {
            ActiveDateText.Text = _activeExperience.DateRangeLabel;
            ActiveSummaryText.Text = _activeExperience.ProgressSummaryLabel;
            foreach (var entry in await _repository.GetProgressEntriesAsync(_activeExperience.Id))
            {
                ProgressEntries.Add(entry);
            }
        }
        ProgressEmpty.Visibility = ProgressEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ProgressList.Visibility = ProgressEntries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        CompletedExperiences.Clear();
        foreach (var experience in allExperiences.Where(experience => experience.CompletedOn is not null))
        {
            CompletedExperiences.Add(experience);
        }
        HistoryCaptionText.Text = CompletedExperiences.Count == 0 ? string.Empty : $"共 {CompletedExperiences.Count} 次";
        HistoryEmpty.Visibility = CompletedExperiences.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Visibility = CompletedExperiences.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        DetailEmpty.Visibility = Visibility.Collapsed;
        DetailScroll.Visibility = Visibility.Visible;
    }

    private void RenderCoverStack(IReadOnlyList<WorkCover> covers)
    {
        DetailCoverCanvas.Children.Clear();
        if (_selectedWork is null || covers.Count == 0)
        {
            var placeholder = new Border
            {
                Width = 68,
                Height = 96,
                CornerRadius = new CornerRadius(8),
                Background = (Brush)FindResource("AccentSoftBrush"),
                BorderBrush = (Brush)FindResource("DividerBrush"),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = _selectedWork?.KindGlyph ?? "书",
                    FontSize = 21,
                    FontWeight = FontWeights.DemiBold,
                    Foreground = (Brush)FindResource("AccentBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Canvas.SetLeft(placeholder, 11);
            Canvas.SetTop(placeholder, 10);
            DetailCoverCanvas.Children.Add(placeholder);
            AddCoverBadge("+");
            return;
        }

        var visible = covers.Take(3).Reverse().ToList();
        foreach (var cover in visible)
        {
            var position = Math.Min(cover.SortOrder, 2);
            var (left, top, angle) = position switch
            {
                0 => (8d, 12d, 0d),
                1 => (14d, 8d, 4d),
                _ => (3d, 8d, -4d)
            };
            var image = new Image
            {
                Source = new CoverImageConverter().Convert(cover.FilePath, typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture) as ImageSource,
                Stretch = Stretch.UniformToFill
            };
            var card = new Border
            {
                Width = 68,
                Height = 96,
                CornerRadius = new CornerRadius(7),
                Background = Brushes.White,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(3),
                ClipToBounds = true,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(angle),
                Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.2 },
                Child = image
            };
            Canvas.SetLeft(card, left);
            Canvas.SetTop(card, top);
            DetailCoverCanvas.Children.Add(card);
        }
        if (covers.Count > 1)
        {
            AddCoverBadge(covers.Count.ToString());
        }
    }

    private void AddCoverBadge(string text)
    {
        var badge = new Border
        {
            MinWidth = 25,
            Height = 25,
            Padding = new Thickness(6, 0, 6, 0),
            CornerRadius = new CornerRadius(13),
            Background = (Brush)FindResource("AccentBrush"),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.DemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Canvas.SetRight(badge, 1);
        Canvas.SetBottom(badge, 3);
        DetailCoverCanvas.Children.Add(badge);
    }

    private void ShowNoSelection()
    {
        _selectedWork = null;
        _activeExperience = null;
        DetailScroll.Visibility = Visibility.Collapsed;
        DetailEmpty.Visibility = Visibility.Visible;
    }

    private async void PrimaryExperience_Click(object sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedWork is null)
        {
            return;
        }
        var dialog = _activeExperience is null
            ? new AddExperienceWindow(_selectedWork.Id, _selectedWork.Kind) { Owner = this }
            : new AddExperienceWindow(_selectedWork.Id, _selectedWork.Kind, _activeExperience, completing: true) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Experience is null)
        {
            return;
        }
        if (_activeExperience is null)
        {
            await _repository.AddExperienceAsync(dialog.Experience);
        }
        else
        {
            await _repository.UpdateExperienceAsync(dialog.Experience);
        }
        await ReloadLibraryAsync(_selectedWork.Id);
    }

    private async void AddProgress_Click(object sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedWork is null || _activeExperience?.StartedOn is null)
        {
            return;
        }
        var dialog = new AddProgressWindow(
            _activeExperience.Id,
            _selectedWork.Kind,
            _activeExperience.StartedOn.Value,
            totalEpisodes: _selectedWork.TotalEpisodes,
            watchedEpisodes: _activeExperience.TotalEpisodes) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Entry is null)
        {
            return;
        }
        await _repository.AddProgressEntryAsync(dialog.Entry, _selectedWork.Id, dialog.TotalEpisodes);
        await ReloadLibraryAsync(_selectedWork.Id);
    }

    private async void EditProgress_Click(object sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedWork is null || _activeExperience?.StartedOn is null || sender is not System.Windows.Controls.Button { Tag: ProgressEntry entry })
        {
            return;
        }
        var dialog = new AddProgressWindow(
            _activeExperience.Id,
            _selectedWork.Kind,
            _activeExperience.StartedOn.Value,
            entry,
            _selectedWork.TotalEpisodes,
            _activeExperience.TotalEpisodes) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Entry is null)
        {
            return;
        }
        await _repository.UpdateProgressEntryAsync(dialog.Entry, _selectedWork.Id, dialog.TotalEpisodes);
        await ReloadLibraryAsync(_selectedWork.Id);
    }

    private async void DeleteProgress_Click(object sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedWork is null || sender is not System.Windows.Controls.Button { Tag: ProgressEntry entry })
        {
            return;
        }
        var choice = MessageBox.Show(
            $"删除 {entry.LoggedOn:yyyy-MM-dd} 的“{entry.AmountLabel}”记录？\n\n累计进度会自动重新计算。此操作无法撤销。",
            "删除中途记录", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }
        await _repository.DeleteProgressEntryAsync(entry.Id, _selectedWork.Id);
        await ReloadLibraryAsync(_selectedWork.Id);
    }

    private async void AbandonExperience_Click(object sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedWork is null || _activeExperience is null)
        {
            return;
        }
        var detail = ProgressEntries.Count == 0 ? "没有中途记录。" : $"其中的 {ProgressEntries.Count} 条中途记录也会一起删除。";
        var choice = MessageBox.Show(
            $"放弃当前这一次{(_selectedWork.Kind == "book" ? "阅读" : "观看")}？\n\n{detail}\n作品和历次完成记录会保留。此操作无法撤销。",
            "放弃本次", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }
        await _repository.DeleteExperienceAsync(_activeExperience.Id, _selectedWork.Id);
        await ReloadLibraryAsync(_selectedWork.Id);
    }

    private async void EditExperience_Click(object sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedWork is null || sender is not System.Windows.Controls.Button { Tag: MediaExperience experience })
        {
            return;
        }
        var dialog = new AddExperienceWindow(_selectedWork.Id, _selectedWork.Kind, experience, completing: true) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Experience is null)
        {
            return;
        }
        await _repository.UpdateExperienceAsync(dialog.Experience);
        await ReloadLibraryAsync(_selectedWork.Id);
    }

    private async void DeleteExperience_Click(object sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedWork is null || sender is not System.Windows.Controls.Button { Tag: MediaExperience experience })
        {
            return;
        }
        var progressCount = (await _repository.GetProgressEntriesAsync(experience.Id)).Count;
        var detail = progressCount == 0 ? "这次体验没有中途记录。" : $"其中的 {progressCount} 条中途记录也会一起删除。";
        var choice = MessageBox.Show(
            $"删除 {experience.DateRangeLabel} 的这次{(_selectedWork.Kind == "book" ? "阅读" : "观看")}？\n\n{detail}\n完成次数和综合评分会自动重新计算。此操作无法撤销。",
            "删除本次记录", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }
        await _repository.DeleteExperienceAsync(experience.Id, _selectedWork.Id);
        await ReloadLibraryAsync(_selectedWork.Id);
    }

    private async void DeleteWork_Click(object sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedWork is null)
        {
            return;
        }
        var choice = MessageBox.Show(
            $"删除《{_selectedWork.Title}》？\n\n该作品的当前体验、历次完成和全部中途记录都会一起删除。此操作无法撤销。",
            "删除作品", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }
        await _repository.DeleteWorkAsync(_selectedWork.Id);
        _selectedWorkId = null;
        ShowNoSelection();
        await ReloadLibraryAsync();
    }

    private async void EditWork_Click(object sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedWork is null)
        {
            return;
        }

        var dialog = new AddWorkWindow(_selectedWork) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Work is null)
        {
            return;
        }

        await _repository.UpdateWorkMetadataAsync(dialog.Work);
        SearchBox.Clear();
        await ReloadLibraryAsync(dialog.Work.Id);
    }

    private async void ManageCovers_Click(object sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedWork is null)
        {
            return;
        }

        var workId = _selectedWork.Id;
        new ManageCoversWindow(_repository, _selectedWork) { Owner = this }.ShowDialog();
        await ReloadLibraryAsync(workId);
    }
}
