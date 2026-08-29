namespace QuietShelf.Models;

public sealed class DashboardTimelineItem
{
    public required string Id { get; init; }
    public required string WorkId { get; init; }
    public required string Title { get; init; }
    public required string Kind { get; init; }
    public required string EventType { get; init; }
    public required string Metric { get; init; }
    public DateOnly LoggedOn { get; init; }
    public int Amount { get; init; }
    public string? Notes { get; init; }
    public string? PrimaryCoverPath { get; init; }
    public bool IsLatest { get; init; }

    public bool HasPrimaryCover => !string.IsNullOrWhiteSpace(PrimaryCoverPath);
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public string KindGlyph => Kind == "book" ? "书" : "影";
    public string DateLabel => LoggedOn.ToString("M月d日");
    public string YearLabel => LoggedOn.ToString("yyyy");
    public string EventLabel => EventType == "completion" ? "完成留档" : "途中记录";
    public string ActionLabel => EventType == "completion"
        ? Kind == "book" ? "完成一次阅读" : "完成一次观看"
        : Metric == "episodes"
            ? $"看了 {Amount} 集"
            : Kind == "book" ? $"阅读 {Amount} 分钟" : $"观看 {Amount} 分钟";
    public string NotesExcerpt
    {
        get
        {
            var text = Notes?.Trim() ?? string.Empty;
            return text.Length <= 100 ? text : text[..100] + "…";
        }
    }
}
