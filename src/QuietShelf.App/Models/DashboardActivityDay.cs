namespace QuietShelf.Models;

public sealed class DashboardActivityDay
{
    public required DateOnly Date { get; init; }
    public required int ActivityCount { get; init; }
    public required int CompletionCount { get; init; }
    public string TitleSummary { get; init; } = string.Empty;
}
