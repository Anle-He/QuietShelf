namespace QuietShelf.Models;

public sealed class DashboardAuthorRank
{
    public int Position { get; init; }
    public string Author { get; init; } = string.Empty;
    public int WorkCount { get; init; }
    public int RatingCount { get; init; }
    public double WeightedRank { get; init; }

    public string PositionLabel => Position.ToString("00");
    public string WeightedRankLabel => WeightedRank.ToString("0.0");
    public string EvidenceLabel => $"{WorkCount} 本书 · {RatingCount} 次完整评分";
}
