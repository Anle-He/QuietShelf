namespace QuietShelf.Models;

public sealed class DashboardShowcaseItem
{
    public string WorkId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Kind { get; init; } = "book";
    public string? Author { get; init; }
    public string? PrimaryCoverPath { get; init; }
    public int CompletionCount { get; init; }
    public int RatingCount { get; init; }
    public double? AggregateRank { get; init; }
    public DateOnly FirstCompletedOn { get; init; }
    public DateOnly LatestCompletedOn { get; init; }

    public bool HasPrimaryCover => !string.IsNullOrWhiteSpace(PrimaryCoverPath);
    public string KindGlyph => Kind == "book" ? "书" : "影";
    public string RankLabel => AggregateRank is null ? "未评分" : AggregateRank.Value.ToString("0.0");
    public string RankMaximumLabel => AggregateRank is null ? string.Empty : $"/ {RatingScale.RankMaximum:0.0}";
    public string CompletionLabel => CompletionCount == 1 ? "完成 1 次" : $"完成 {CompletionCount} 次";
    public string LatestDateLabel => LatestCompletedOn.ToString("yyyy.MM.dd");
    public string FirstDateLabel => FirstCompletedOn.ToString("M月d日");
}
