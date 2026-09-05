using System.Windows;
using System.Windows.Controls;
using QuietShelf.Models;

namespace QuietShelf;

public partial class MainWindow
{
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

        DashboardHero.Visibility = _allWorks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DashboardCollectionHeader.Visibility = _allWorks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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

    private void DashboardRankings_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 780;
        DashboardRankings.ColumnDefinitions[3].Width = new GridLength(narrow ? 0 : 12);
        DashboardRankings.ColumnDefinitions[4].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRowSpan(DashboardBookRanking, narrow ? 2 : 1);
        Grid.SetColumn(DashboardAuthorRanking, narrow ? 2 : 4);
        Grid.SetRow(DashboardAuthorRanking, narrow ? 1 : 0);
        DashboardAuthorRanking.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(0);
    }

    private void DashboardHighlights_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 740;
        Grid.SetColumnSpan(DashboardRecentSection, stacked ? 3 : 1);
        Grid.SetColumn(DashboardFirstBookSection, stacked ? 0 : 2);
        Grid.SetRow(DashboardFirstBookSection, stacked ? 1 : 0);
        Grid.SetColumnSpan(DashboardFirstBookSection, stacked ? 3 : 1);
        DashboardFirstBookSection.Margin = stacked ? new Thickness(0, 12, 0, 0) : new Thickness(0);
    }

    private async void DashboardWork_Open(object sender, RoutedEventArgs e)
    {
        var workId = (sender as FrameworkElement)?.Tag switch
        {
            DashboardTimelineItem item => item.WorkId,
            DashboardShowcaseItem item => item.WorkId,
            _ => null
        };
        if (workId is null || _repository is null)
        {
            return;
        }

        _showingDashboard = false;
        _selectedWorkId = workId;
        _kindFilter = "all";
        UpdateFilterButtons();
        SearchBox.Clear();
        CancelPendingSearch();
        await ExecuteRepositoryActionAsync(() => ApplyFiltersAsync(reloadSelected: true), "无法打开作品");
    }
}
