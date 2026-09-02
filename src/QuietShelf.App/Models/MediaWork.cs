namespace QuietShelf.Models;

public sealed class MediaWork
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Author { get; init; }
    public required string Kind { get; init; }
    public string? Status { get; init; }
    public int? TotalEpisodes { get; init; }
    public string? PrimaryCoverPath { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;
    public int ExperienceCount { get; init; }
    public int RatedExperienceCount { get; init; }
    public bool HasActiveExperience { get; init; }
    public double? AggregateRank { get; init; }
    public DateOnly? LatestActivityOn { get; init; }

    public string KindLabel => Kind == "book" ? "书籍" : "影视";
    public string KindGlyph => Kind == "book" ? "书" : "影";
    public string ExperienceActionLabel => Kind == "book" ? "阅读" : "观看";
    public string ExperienceSummaryLabel => ExperienceCount == 0
        ? "尚无完成记录"
        : $"已记录 {ExperienceCount} 次";
    public string AggregateRankLabel => AggregateRank is null
        ? "暂无评分"
        : $"{AggregateRank:0.0} / {RatingScale.RankMaximum:0.0}";
    public bool HasAggregateRank => AggregateRank is not null;
    public string AggregateRankValueLabel => AggregateRank?.ToString("0.0") ?? string.Empty;
    public string AggregateRankMaximumLabel => $"/ {RatingScale.RankMaximum:0.0}";
    public string AggregateScoreTier => RatingScale.GetPercentage(AggregateRank) switch
    {
        >= 0.8 => "gold",
        >= 0.6 => "silver",
        _ => "bronze"
    };
    public string AggregateScoreTierLabel => AggregateScoreTier switch
    {
        "gold" => "金星",
        "silver" => "银星",
        _ => "铜星"
    };
    public string AggregateRatingMarkLabel => $"{AggregateScoreTierLabel} · {AggregateRankLabel}";
    public string ExperienceCountColorTier => ExperienceCount switch
    {
        >= 2 => "gold",
        1 => "green",
        _ => "muted"
    };
    public string ExperienceCountLabel => $"{ExperienceActionLabel} {ExperienceCount} 次";
    public string RatingCountLabel => RatedExperienceCount == 0 ? "尚无完整评分" : $"来自 {RatedExperienceCount} 次评分";
    public string LatestActivityLabel => LatestActivityOn is null ? "尚未记录" : $"最近 {LatestActivityOn:yyyy-MM-dd}";
    public bool HasPrimaryCover => !string.IsNullOrWhiteSpace(PrimaryCoverPath);
}
