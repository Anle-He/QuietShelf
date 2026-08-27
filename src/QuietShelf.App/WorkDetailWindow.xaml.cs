using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using QuietShelf.Data;
using QuietShelf.Models;

namespace QuietShelf;

public partial class WorkDetailWindow : Window
{
    private readonly LibraryRepository _repository;
    private readonly string _workId;
    private MediaWork? _work;
    private MediaExperience? _activeExperience;

    public WorkDetailWindow(LibraryRepository repository, string workId)
    {
        _repository = repository;
        _workId = workId;
        InitializeComponent();
        DataContext = this;
        Loaded += WorkDetailWindow_Loaded;
    }

    public ObservableCollection<MediaExperience> Experiences { get; } = [];
    public ObservableCollection<ProgressEntry> ProgressEntries { get; } = [];

    private async void WorkDetailWindow_Loaded(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void AddExperience_Click(object sender, RoutedEventArgs e)
    {
        if (_work is null)
        {
            return;
        }
        var dialog = _activeExperience is null
            ? new AddExperienceWindow(_work.Id, _work.Kind) { Owner = this }
            : new AddExperienceWindow(_work.Id, _work.Kind, _activeExperience, completing: true) { Owner = this };
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
        await ReloadAsync();
    }

    private async void AddProgress_Click(object sender, RoutedEventArgs e)
    {
        if (_work is null || _activeExperience?.StartedOn is null)
        {
            return;
        }
        var dialog = new AddProgressWindow(
            _activeExperience.Id,
            _work.Kind,
            _activeExperience.StartedOn.Value,
            totalEpisodes: _work.TotalEpisodes,
            watchedEpisodes: _activeExperience.TotalEpisodes) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Entry is null)
        {
            return;
        }
        await _repository.AddProgressEntryAsync(dialog.Entry, dialog.TotalEpisodes);
        await ReloadAsync();
    }

    private async void EditProgress_Click(object sender, RoutedEventArgs e)
    {
        if (_work is null || _activeExperience?.StartedOn is null || sender is not Button { Tag: ProgressEntry entry })
        {
            return;
        }
        var dialog = new AddProgressWindow(
            _activeExperience.Id,
            _work.Kind,
            _activeExperience.StartedOn.Value,
            entry,
            _work.TotalEpisodes,
            _activeExperience.TotalEpisodes) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Entry is null)
        {
            return;
        }
        await _repository.UpdateProgressEntryAsync(dialog.Entry, dialog.TotalEpisodes);
        await ReloadAsync();
    }

    private async void DeleteProgress_Click(object sender, RoutedEventArgs e)
    {
        if (_work is null || sender is not Button { Tag: ProgressEntry entry })
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
        await _repository.DeleteProgressEntryAsync(entry.Id);
        await ReloadAsync();
    }

    private async void AbandonExperience_Click(object sender, RoutedEventArgs e)
    {
        if (_work is null || _activeExperience is null)
        {
            return;
        }
        var detail = ProgressEntries.Count == 0 ? "没有中途记录。" : $"其中的 {ProgressEntries.Count} 条中途记录也会一起删除。";
        var choice = MessageBox.Show(
            $"放弃当前这一次{(_work.Kind == "book" ? "阅读" : "观看")}？\n\n{detail}\n作品和历次完成记录会保留。此操作无法撤销。",
            "放弃本次", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }
        await _repository.DeleteExperienceAsync(_activeExperience.Id, _work.Id);
        await ReloadAsync();
    }

    private async void EditExperience_Click(object sender, RoutedEventArgs e)
    {
        if (_work is null || sender is not Button { Tag: MediaExperience experience })
        {
            return;
        }
        var dialog = new AddExperienceWindow(_work.Id, _work.Kind, experience, completing: true) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Experience is null)
        {
            return;
        }
        await _repository.UpdateExperienceAsync(dialog.Experience);
        await ReloadAsync();
    }

    private async void DeleteExperience_Click(object sender, RoutedEventArgs e)
    {
        if (_work is null || sender is not Button { Tag: MediaExperience experience })
        {
            return;
        }
        var progressCount = (await _repository.GetProgressEntriesAsync(experience.Id)).Count;
        var detail = progressCount == 0 ? "这次体验没有中途记录。" : $"其中的 {progressCount} 条中途记录也会一起删除。";
        var choice = MessageBox.Show(
            $"删除 {experience.DateRangeLabel} 的这次{(_work.Kind == "book" ? "阅读" : "观看")}？\n\n{detail}\n完成次数和综合评分会自动重新计算。此操作无法撤销。",
            "删除本次记录", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }
        await _repository.DeleteExperienceAsync(experience.Id, _work.Id);
        await ReloadAsync();
    }

    private async void DeleteWork_Click(object sender, RoutedEventArgs e)
    {
        if (_work is null)
        {
            return;
        }
        var choice = MessageBox.Show(
            $"删除《{_work.Title}》？\n\n该作品的当前体验、历次完成和全部中途记录都会一起删除。此操作无法撤销。",
            "删除作品", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }
        await _repository.DeleteWorkAsync(_work.Id);
        Close();
    }

    private async Task ReloadAsync()
    {
        _work = await _repository.GetWorkAsync(_workId);
        if (_work is null)
        {
            Close();
            return;
        }

        Title = _work.Title;
        TitleText.Text = _work.Title;
        MetaText.Text = $"{_work.KindLabel} · {_work.StatusLabel} · {_work.LatestActivityLabel}";
        AggregateRankText.Text = _work.AggregateRankLabel;
        RatingCountText.Text = _work.RatingCountLabel;
        ExperienceCountText.Text = _work.ExperienceCountLabel;
        var allExperiences = await _repository.GetExperiencesAsync(_workId);
        _activeExperience = allExperiences.FirstOrDefault(experience => experience.StartedOn is not null && experience.CompletedOn is null);
        AddExperienceButton.Content = _activeExperience is not null
            ? "完成本次"
            : _work.Kind == "book"
                ? (_work.ExperienceCount == 0 ? "＋ 开始阅读" : "＋ 再读一次")
                : (_work.ExperienceCount == 0 ? "＋ 开始观看" : "＋ 再看一次");

        CurrentExperiencePanel.Visibility = _activeExperience is null ? Visibility.Collapsed : Visibility.Visible;
        ProgressEntries.Clear();
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
        ActiveProgressScroll.Visibility = ProgressEntries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        Experiences.Clear();
        foreach (var experience in allExperiences.Where(experience => experience.CompletedOn is not null))
        {
            Experiences.Add(experience);
        }
        HistoryEmpty.Visibility = Experiences.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryScroll.Visibility = Experiences.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
