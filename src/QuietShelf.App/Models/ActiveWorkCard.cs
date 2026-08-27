namespace QuietShelf.Models;

public sealed class ActiveWorkCard
{
    public required MediaWork Work { get; init; }
    public required MediaExperience Experience { get; init; }

    public string Title => Work.Title;
    public string? Subtitle => Work.Subtitle;
    public string KindGlyph => Work.KindGlyph;
    public string? PrimaryCoverPath => Work.PrimaryCoverPath;
    public bool HasPrimaryCover => Work.HasPrimaryCover;
    public string ProgressLabel => Experience.ProgressSummaryLabel;
    public string ActivityLabel => Work.LatestActivityLabel;
    public int EpisodeTarget => Experience.AvailableEpisodes ?? Work.TotalEpisodes ?? 0;
    public bool HasEpisodeProgress => Work.Kind == "screen" && EpisodeTarget > 0;
    public double ProgressPercent => !HasEpisodeProgress
        ? 0
        : Math.Clamp(Experience.TotalEpisodes * 100d / EpisodeTarget, 0, 100);
    public double ProgressFraction => ProgressPercent / 100d;
    public string ProgressPercentLabel => $"{ProgressPercent:0}%";
    public string EpisodeProgressLabel => $"{Experience.TotalEpisodes} / {EpisodeTarget} 集";
}
