using System.Globalization;

namespace QuietShelf.Models;

public sealed class ExperienceArchiveCard
{
    public required MediaExperience Experience { get; init; }
    public required int ArchiveNumber { get; init; }

    public string ArchiveNumberLabel => ArchiveNumber.ToString("00");
    public string JourneyLabel => $"记录 {ArchiveNumberLabel}";
    private DateOnly CompletionDate => Experience.CompletedOn ?? DateOnly.FromDateTime(Experience.CreatedAt.LocalDateTime);
    public string EndDateLabel => CompletionDate.ToString("yyyy.MM.dd");
    public string CompletionMonthDayLabel => CompletionDate.ToString("MM.dd");
    public string CompletionYearLabel => CompletionDate.ToString("yyyy");
    public string CompletionWeekdayLabel => CompletionDate.ToString("ddd", CultureInfo.GetCultureInfo("zh-CN"));
    public bool HasCompleteRating => Experience.HasCompleteRating;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Experience.Notes);
    public string Notes => Experience.Notes?.Trim() ?? string.Empty;
    public int? Allure => Experience.Allure;
    public int? Immersion => Experience.Immersion;
    public int? Rationality => Experience.Rationality;
    public int? Illumination => Experience.Illumination;
    public string RatingTier => RatingScale.GetPercentage(Experience.Rank) switch
    {
        >= 0.8 => "gold",
        >= 0.6 => "silver",
        _ => "bronze"
    };
}
