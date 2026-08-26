namespace QuietShelf.Models;

public sealed class WorkCover
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string WorkId { get; init; }
    public required string FileName { get; init; }
    public required string FilePath { get; init; }
    public int SortOrder { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public bool IsPrimary => SortOrder == 0;
    public string PositionLabel => IsPrimary ? "主封面" : $"第 {SortOrder + 1} 张";
}
