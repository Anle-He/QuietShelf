namespace QuietShelf.Models;

public sealed class ProgressEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string ExperienceId { get; init; }
    public DateOnly LoggedOn { get; init; } = DateOnly.FromDateTime(DateTime.Today);
    public required string Metric { get; init; }
    public int Amount { get; init; }
    public string? Notes { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;

    public string AmountLabel => Metric == "episodes" ? $"看了 {Amount} 集" : $"记录 {Amount} 分钟";
    public string NotesLabel => string.IsNullOrWhiteSpace(Notes) ? "没有留下想法" : Notes;
}
