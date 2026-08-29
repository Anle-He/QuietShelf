namespace QuietShelf.Models;

public sealed class DashboardTimelineDay
{
    public required DateOnly Date { get; init; }
    public required IReadOnlyList<DashboardTimelineItem> Items { get; init; }
    public string DateLabel => Date.ToString("MM.dd");
    public string YearLabel => Date.ToString("yyyy");
    public string WeekdayLabel => Date.DayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日"
    };
}
