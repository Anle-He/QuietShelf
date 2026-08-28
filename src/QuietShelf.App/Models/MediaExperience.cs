namespace QuietShelf.Models;

public sealed class MediaExperience
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string WorkId { get; init; }
    public DateOnly? StartedOn { get; init; }
    public DateOnly? CompletedOn { get; init; }
    public int? Allure { get; init; }
    public int? Immersion { get; init; }
    public int? Rationality { get; init; }
    public int? Illumination { get; init; }
    public string? Notes { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;
    public int ProgressDayCount { get; init; }
    public int ProgressEntryCount { get; init; }
    public int TotalMinutes { get; init; }
    public int TotalEpisodes { get; init; }
    public int? AvailableEpisodes { get; init; }

    public bool HasCompleteRating => Allure is not null && Immersion is not null && Rationality is not null && Illumination is not null;
    public double? Rank => RatingScale.Calculate(Allure, Immersion, Rationality, Illumination);
    public string RankLabel => Rank is null ? "评分未完成" : $"{Rank:0.0} / {RatingScale.RankMaximum:0.0}";
    public string DateRangeLabel => (StartedOn, CompletedOn) switch
    {
        ({ } started, { } completed) when started == completed => started.ToString("yyyy-MM-dd"),
        ({ } started, { } completed) => $"{started:yyyy-MM-dd} — {completed:yyyy-MM-dd}",
        ({ } started, null) => $"始于 {started:yyyy-MM-dd}",
        (null, { } completed) => $"结束于 {completed:yyyy-MM-dd}",
        _ => $"记录于 {CreatedAt.LocalDateTime:yyyy-MM-dd}"
    };
    public string ScoresLabel => HasCompleteRating
        ? $"{Allure} / {Immersion} / {Rationality} / {Illumination}"
        : "四维评分未完成";
    public string NotesLabel => string.IsNullOrWhiteSpace(Notes) ? "没有留下想法" : Notes;
    public string ProgressSummaryLabel
    {
        get
        {
            var parts = new List<string>();
            if (ProgressDayCount > 0) parts.Add($"记录 {ProgressDayCount} 天");
            if (TotalMinutes > 0) parts.Add($"{TotalMinutes} 分钟");
            if (TotalEpisodes > 0)
            {
                parts.Add(AvailableEpisodes is > 0 ? $"已看 {TotalEpisodes} / {AvailableEpisodes} 集" : $"已看 {TotalEpisodes} 集");
            }
            return parts.Count == 0 ? "还没有中途记录" : string.Join(" · ", parts);
        }
    }
}
