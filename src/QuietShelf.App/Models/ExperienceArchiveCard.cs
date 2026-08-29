namespace QuietShelf.Models;

public sealed class ExperienceArchiveCard
{
    public required MediaExperience Experience { get; init; }
    public required int ArchiveNumber { get; init; }

    public string ArchiveNumberLabel => ArchiveNumber.ToString("00");
    public string JourneyLabel => $"旅程 {ArchiveNumberLabel}";
    public string StartDateLabel => (Experience.StartedOn ?? Experience.CompletedOn ?? DateOnly.FromDateTime(Experience.CreatedAt.LocalDateTime)).ToString("yyyy.MM.dd");
    public string EndDateLabel => (Experience.CompletedOn ?? Experience.StartedOn ?? DateOnly.FromDateTime(Experience.CreatedAt.LocalDateTime)).ToString("yyyy.MM.dd");
    public int JourneyDays
    {
        get
        {
            var start = Experience.StartedOn ?? Experience.CompletedOn ?? DateOnly.FromDateTime(Experience.CreatedAt.LocalDateTime);
            var end = Experience.CompletedOn ?? Experience.StartedOn ?? start;
            return Math.Max(1, end.DayNumber - start.DayNumber + 1);
        }
    }
    public string JourneyDaysLabel => JourneyDays == 1 ? "当日抵达" : $"历时 {JourneyDays} 天";
    public bool HasCompleteRating => Experience.HasCompleteRating;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Experience.Notes);
    public string Notes => Experience.Notes?.Trim() ?? string.Empty;
    public int? Allure => Experience.Allure;
    public int? Immersion => Experience.Immersion;
    public int? Rationality => Experience.Rationality;
    public int? Illumination => Experience.Illumination;
    public string ActivityDaysLabel => Experience.ProgressDayCount > 0
        ? $"记录 {Experience.ProgressDayCount} 天"
        : "没有中途记录";
    public string ProgressAmountLabel
    {
        get
        {
            if (Experience.TotalMinutes > 0)
            {
                return $"累计 {Experience.TotalMinutes} 分钟";
            }
            return string.Empty;
        }
    }
    public bool HasProgressAmount => !string.IsNullOrWhiteSpace(ProgressAmountLabel);
    public string RatingTier => RatingScale.GetPercentage(Experience.Rank) switch
    {
        >= 0.8 => "gold",
        >= 0.6 => "silver",
        _ => "bronze"
    };
}
