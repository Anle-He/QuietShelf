using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using QuietShelf.Data;
using QuietShelf.Models;

namespace QuietShelf;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly Database _database = new();
    private LibraryRepository? _repository;
    private IReadOnlyList<MediaWork> _allWorks = [];
    private IReadOnlyList<DashboardTimelineItem> _dashboardTimelineItems = [];
    private DashboardShowcase _dashboardShowcase = new();
    private string _kindFilter = "all";
    private string? _selectedWorkId;
    private MediaWork? _selectedWork;
    private int _selectionLoadVersion;
    private CancellationTokenSource? _searchDebounceCancellation;
    private bool _isApplyingFilters;
    private bool _showingDashboard = true;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += MainWindow_Loaded;
    }

    public ObservableCollection<MediaWork> VisibleWorks { get; } = [];
    public ObservableCollection<DashboardTimelineDay> DashboardTimelineDays { get; } = [];
    public ObservableCollection<DashboardShowcaseItem> DashboardTopBooks { get; } = [];
    public ObservableCollection<DashboardShowcaseItem> DashboardTopScreens { get; } = [];
    public ObservableCollection<DashboardShowcaseItem> DashboardRecentWorks { get; } = [];
    public ObservableCollection<DashboardAuthorRank> DashboardTopAuthors { get; } = [];
    public ObservableCollection<ExperienceArchiveCard> CompletedExperiences { get; } = [];

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
                await ExecuteRepositoryActionAsync(() => ReloadLibraryAsync(existing.Id), "无法打开作品");
                return;
            }
            if (choice != MessageBoxResult.No)
            {
                return;
            }
        }

        await ExecuteRepositoryActionAsync(async () =>
        {
            await _repository.AddWorkAsync(dialog.Work);
            SearchBox.Clear();
            await ReloadLibraryAsync(dialog.Work.Id);
        }, "无法添加作品");
    }

    private async void WorkList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingFilters)
        {
            return;
        }

        if (WorkList.SelectedItem is not MediaWork work)
        {
            if (VisibleWorks.Count == 0)
            {
                ShowNoSelection();
            }
            return;
        }

        _showingDashboard = false;
        _selectedWorkId = work.Id;
        if (_selectedWork?.Id != work.Id)
        {
            await ExecuteRepositoryActionAsync(() => LoadSelectedWorkAsync(work.Id), "无法打开作品");
        }
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_repository is null)
        {
            return;
        }

        _searchDebounceCancellation?.Cancel();
        _searchDebounceCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _searchDebounceCancellation = cancellation;
        try
        {
            await Task.Delay(200, cancellation.Token);
            if (_searchDebounceCancellation == cancellation)
            {
                await ExecuteRepositoryActionAsync(() => ApplyFiltersAsync(), "无法筛选作品");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not string filter)
        {
            return;
        }
        _kindFilter = filter;
        UpdateFilterButtons();
        CancelPendingSearch();
        await ExecuteRepositoryActionAsync(() => ApplyFiltersAsync(), "无法筛选作品");
    }

    private async Task ReloadLibraryAsync(string? selectWorkId = null)
    {
        if (_repository is null)
        {
            return;
        }
        _allWorks = await _repository.GetWorksAsync();
        await ReloadDashboardAsync();
        if (selectWorkId is not null)
        {
            _selectedWorkId = selectWorkId;
            _showingDashboard = false;
        }
        CancelPendingSearch();
        await ApplyFiltersAsync(reloadSelected: true);
    }

    private async Task ReloadDashboardAsync()
    {
        if (_repository is null)
        {
            return;
        }

        var timelineTask = _repository.GetRecentTimelineAsync();
        var showcaseTask = _repository.GetDashboardShowcaseAsync();
        _dashboardTimelineItems = await timelineTask;
        _dashboardShowcase = await showcaseTask;
        RefreshDashboard();
    }

    private void RefreshDashboard()
    {
        DashboardTimelineDays.Clear();
        foreach (var day in _dashboardTimelineItems.GroupBy(item => item.LoggedOn))
        {
            DashboardTimelineDays.Add(new DashboardTimelineDay { Date = day.Key, Items = day.ToList() });
        }

        DashboardWorkCountText.Text = _allWorks.Count.ToString();
        DashboardActiveCountText.Text = _allWorks.Sum(work => work.RatedExperienceCount).ToString();
        DashboardExperienceCountText.Text = _allWorks.Sum(work => work.ExperienceCount).ToString();
        DashboardEmptyState.Visibility = _allWorks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DashboardTimelineList.Visibility = _dashboardTimelineItems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        DashboardTimelineEmptyState.Visibility = _allWorks.Count > 0 && _dashboardTimelineItems.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        RefreshDashboardShowcase();
    }

    private void RefreshDashboardShowcase()
    {
        static IEnumerable<DashboardShowcaseItem> Ranked(IEnumerable<DashboardShowcaseItem> works) =>
            works.Where(work => work.AggregateRank is not null)
                .OrderByDescending(work => work.AggregateRank)
                .ThenByDescending(work => work.RatingCount)
                .ThenByDescending(work => work.LatestCompletedOn)
                .Take(3);

        DashboardTopBooks.Clear();
        foreach (var work in Ranked(_dashboardShowcase.CompletedWorks.Where(work => work.Kind == "book")))
        {
            DashboardTopBooks.Add(work);
        }
        DashboardTopScreens.Clear();
        foreach (var work in Ranked(_dashboardShowcase.CompletedWorks.Where(work => work.Kind == "screen")))
        {
            DashboardTopScreens.Add(work);
        }
        DashboardRecentWorks.Clear();
        foreach (var work in _dashboardShowcase.CompletedWorks.OrderByDescending(work => work.LatestCompletedOn).Take(3))
        {
            DashboardRecentWorks.Add(work);
        }
        DashboardTopAuthors.Clear();
        foreach (var author in _dashboardShowcase.TopAuthors)
        {
            DashboardTopAuthors.Add(author);
        }

        var year = DateTime.Today.Year;
        var firstBook = _dashboardShowcase.CompletedWorks
            .Where(work => work.Kind == "book" && work.FirstCompletedOn.Year == year)
            .OrderBy(work => work.FirstCompletedOn)
            .ThenBy(work => work.Title)
            .FirstOrDefault();
        DashboardFirstBookCard.DataContext = firstBook;
        DashboardFirstBookCard.Visibility = firstBook is null ? Visibility.Collapsed : Visibility.Visible;
        DashboardFirstBookEmpty.Visibility = firstBook is null ? Visibility.Visible : Visibility.Collapsed;
        DashboardShowcasePanel.Visibility = _allWorks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task ApplyFiltersAsync(bool reloadSelected = false)
    {
        var query = SearchBox?.Text.Trim() ?? string.Empty;
        var matches = _allWorks.Where(work =>
            (_kindFilter == "all" || work.Kind == _kindFilter) &&
             (string.IsNullOrWhiteSpace(query)
              || work.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
              || (work.Subtitle?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
              || (work.Author?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false))).ToList();

        var regularMatches = matches;
        var selected = _showingDashboard ? null : matches.FirstOrDefault(work => work.Id == _selectedWorkId);
        if (!_showingDashboard && selected is null && matches.Count > 0 && string.IsNullOrWhiteSpace(query))
        {
            selected = matches[0];
        }

        _isApplyingFilters = true;
        try
        {
            VisibleWorks.Clear();
            foreach (var work in regularMatches)
            {
                VisibleWorks.Add(work);
            }

            WorkList.SelectedItem = selected;
        }
        finally
        {
            _isApplyingFilters = false;
        }

        if (EmptyState is null || WorkList is null)
        {
            return;
        }
        LibraryCountText.Text = $"{_allWorks.Count} 部作品";
        RegularWorksHeader.Visibility = regularMatches.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        WorkList.Visibility = regularMatches.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyTitle.Text = _allWorks.Count == 0 ? "这里还没有作品" : "没有找到相符的作品";
        EmptyDescription.Text = _allWorks.Count == 0
            ? "先加入一本书，或一部想留下来的影视。"
            : "换一个标题关键词或类别试试。";

        if (selected is null)
        {
            if (_showingDashboard)
            {
                ShowDashboard();
            }
            else
            {
                ShowNoSelection();
            }
            return;
        }

        _selectedWorkId = selected.Id;
        if (reloadSelected || _selectedWork?.Id != selected.Id)
        {
            await LoadSelectedWorkAsync(selected.Id);
        }
    }

    private void CancelPendingSearch()
    {
        _searchDebounceCancellation?.Cancel();
        _searchDebounceCancellation?.Dispose();
        _searchDebounceCancellation = null;
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

        var loadVersion = ++_selectionLoadVersion;
        _selectedWork = null;
        var selectedWorkTask = _repository.GetWorkAsync(workId);
        var coversTask = _repository.GetCoversAsync(workId);
        var experiencesTask = _repository.GetExperiencesAsync(workId);
        await Task.WhenAll(selectedWorkTask, coversTask, experiencesTask);
        if (loadVersion != _selectionLoadVersion || _selectedWorkId != workId)
        {
            return;
        }

        var selectedWork = await selectedWorkTask;
        if (selectedWork is null)
        {
            if (loadVersion == _selectionLoadVersion && _selectedWorkId == workId)
            {
                ShowNoSelection();
            }
            return;
        }

        var covers = await coversTask;
        var allExperiences = await experiencesTask;
        if (loadVersion != _selectionLoadVersion || _selectedWorkId != workId)
        {
            return;
        }

        _selectedWork = selectedWork;
        _showingDashboard = false;
        DashboardScroll.Visibility = Visibility.Collapsed;
        HomeButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;

        RenderCoverStack(covers);
        DetailKickerText.Text = _selectedWork.Kind == "book" ? "书籍档案" : "影视档案";
        DetailTitleText.Text = _selectedWork.Title;
        DetailSubtitleText.Text = _selectedWork.Subtitle ?? string.Empty;
        DetailSubtitleText.Visibility = string.IsNullOrWhiteSpace(_selectedWork.Subtitle)
            ? Visibility.Collapsed
            : Visibility.Visible;
        DetailAuthorText.Text = _selectedWork.Author ?? string.Empty;
        DetailAuthorText.Visibility = _selectedWork.Kind == "book" && !string.IsNullOrWhiteSpace(_selectedWork.Author)
            ? Visibility.Visible
            : Visibility.Collapsed;
        DetailMetaText.Text = _selectedWork.ExperienceCount == 0
            ? $"{_selectedWork.KindLabel} · 尚无完成记录"
            : $"{_selectedWork.KindLabel} · {_selectedWork.ExperienceSummaryLabel} · {_selectedWork.LatestActivityLabel}";
        DetailRankText.Text = _selectedWork.AggregateRankLabel;
        DetailCountText.Text = _selectedWork.ExperienceCountLabel;
        DetailRatingCountText.Text = _selectedWork.RatingCountLabel;
        PrimaryExperienceButton.Content = "记录一次完成";

        CompletedExperiences.Clear();
        var completed = allExperiences.Where(experience => experience.CompletedOn is not null).ToList();
        for (var index = 0; index < completed.Count; index++)
        {
            CompletedExperiences.Add(new ExperienceArchiveCard
            {
                Experience = completed[index],
                ArchiveNumber = completed.Count - index
            });
        }
        HistoryCaptionText.Text = CompletedExperiences.Count == 0 ? string.Empty : $"共 {CompletedExperiences.Count} 次";
        HistoryEmpty.Visibility = CompletedExperiences.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Visibility = CompletedExperiences.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        DetailEmpty.Visibility = Visibility.Collapsed;
        DetailScroll.Visibility = Visibility.Visible;
    }

    private void ShowNoSelection()
    {
        _selectionLoadVersion++;
        _selectedWork = null;
        DetailScroll.Visibility = Visibility.Collapsed;
        DetailEmpty.Visibility = Visibility.Visible;
        DashboardScroll.Visibility = Visibility.Collapsed;
        HomeButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
    }

    private void ShowDashboard()
    {
        _selectionLoadVersion++;
        _showingDashboard = true;
        _selectedWorkId = null;
        _selectedWork = null;
        _isApplyingFilters = true;
        try
        {
            WorkList.SelectedItem = null;
        }
        finally
        {
            _isApplyingFilters = false;
        }
        DetailScroll.Visibility = Visibility.Collapsed;
        DetailEmpty.Visibility = Visibility.Collapsed;
        DashboardScroll.Visibility = Visibility.Visible;
        HomeButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        ShowDashboard();
    }

    private async void PrimaryExperience_Click(object sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedWork is null)
        {
            return;
        }
        var dialog = new AddExperienceWindow(_selectedWork.Id, _selectedWork.Kind) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Experience is null)
        {
            return;
        }
        var selectedWorkId = _selectedWork.Id;
        await ExecuteRepositoryActionAsync(async () =>
        {
            await _repository.AddExperienceAsync(dialog.Experience);
            await ReloadLibraryAsync(selectedWorkId);
        }, "无法保存本次记录");
    }

    private async void DashboardTimeline_Open(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DashboardTimelineItem item })
        {
            return;
        }

        _showingDashboard = false;
        _kindFilter = "all";
        UpdateFilterButtons();
        SearchBox.Clear();
        await ExecuteRepositoryActionAsync(() => ReloadLibraryAsync(item.WorkId), "无法打开作品");
    }

    private async void DashboardShowcase_Open(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DashboardShowcaseItem item })
        {
            return;
        }

        _showingDashboard = false;
        _kindFilter = "all";
        UpdateFilterButtons();
        SearchBox.Clear();
        await ExecuteRepositoryActionAsync(() => ReloadLibraryAsync(item.WorkId), "无法打开作品");
    }


    private async void EditExperience_Click(object sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedWork is null || sender is not FrameworkElement { Tag: MediaExperience experience })
        {
            return;
        }
        var dialog = new AddExperienceWindow(_selectedWork.Id, _selectedWork.Kind, experience) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Experience is null)
        {
            return;
        }
        var selectedWorkId = _selectedWork.Id;
        await ExecuteRepositoryActionAsync(async () =>
        {
            await _repository.UpdateExperienceAsync(dialog.Experience);
            await ReloadLibraryAsync(selectedWorkId);
        }, "无法更新本次记录");
    }

    private async void DeleteExperience_Click(object sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedWork is null || sender is not FrameworkElement { Tag: MediaExperience experience })
        {
            return;
        }
        var progressCount = experience.ProgressEntryCount;
        var detail = progressCount == 0 ? "这次体验没有中途记录。" : $"其中的 {progressCount} 条中途记录也会一起删除。";
        var choice = MessageBox.Show(
            $"删除 {experience.DateRangeLabel} 的这次{(_selectedWork.Kind == "book" ? "阅读" : "观看")}？\n\n{detail}\n完成次数和综合评分会自动重新计算。此操作无法撤销。",
            "删除本次记录", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }
        var selectedWorkId = _selectedWork.Id;
        await ExecuteRepositoryActionAsync(async () =>
        {
            await _repository.DeleteExperienceAsync(experience.Id, selectedWorkId);
            await ReloadLibraryAsync(selectedWorkId);
        }, "无法删除本次记录");
    }

    private void ArchiveMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private async void DeleteWork_Click(object sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedWork is null)
        {
            return;
        }
        var choice = MessageBox.Show(
            $"删除《{_selectedWork.Title}》？\n\n作品资料、完成记录和兼容保留的历史进度都会一起删除。此操作无法撤销。",
            "删除作品", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }
        var selectedWorkId = _selectedWork.Id;
        await ExecuteRepositoryActionAsync(async () =>
        {
            await _repository.DeleteWorkAsync(selectedWorkId);
            ShowDashboard();
            await ReloadLibraryAsync();
        }, "无法删除作品");
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

        await ExecuteRepositoryActionAsync(async () =>
        {
            await _repository.UpdateWorkMetadataAsync(dialog.Work);
            SearchBox.Clear();
            await ReloadLibraryAsync(dialog.Work.Id);
        }, "无法更新作品");
    }

    private async void ManageCovers_Click(object sender, RoutedEventArgs e)
    {
        if (_repository is null || _selectedWork is null)
        {
            return;
        }

        var workId = _selectedWork.Id;
        new ManageCoversWindow(_repository, _selectedWork) { Owner = this }.ShowDialog();
        await ExecuteRepositoryActionAsync(() => ReloadLibraryAsync(workId), "无法刷新作品");
    }

    private async Task ExecuteRepositoryActionAsync(Func<Task> action, string errorTitle)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, errorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
