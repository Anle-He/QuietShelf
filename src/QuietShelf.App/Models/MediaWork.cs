namespace QuietShelf.Models;

public sealed class MediaWork
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
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
    public string StatusLabel => HasActiveExperience ? "进行中" : Status switch
    {
        "planned" => Kind == "book" ? "想读" : "想看",
        "in_progress" => "进行中",
        "completed" => Kind == "book" ? "已读" : "已看",
        _ => "未设置状态"
    };
    public string AggregateRankLabel => AggregateRank is null ? "暂无评分" : $"{AggregateRank:0.0} / 3.9";
    public string ExperienceCountLabel => Kind == "book" ? $"阅读 {ExperienceCount} 次" : $"观看 {ExperienceCount} 次";
    public string RatingCountLabel => RatedExperienceCount == 0 ? "尚无完整评分" : $"来自 {RatedExperienceCount} 次评分";
    public string LatestActivityLabel => LatestActivityOn is null ? "尚未记录" : $"最近 {LatestActivityOn:yyyy-MM-dd}";
    public bool HasPrimaryCover => !string.IsNullOrWhiteSpace(PrimaryCoverPath);
}
