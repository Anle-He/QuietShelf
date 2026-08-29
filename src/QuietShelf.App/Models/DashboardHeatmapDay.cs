namespace QuietShelf.Models;

public sealed class DashboardHeatmapDay
{
    public required DateOnly Date { get; init; }
    public required int ActivityCount { get; init; }
    public required int CompletionCount { get; init; }
    public string TitleSummary { get; init; } = string.Empty;
    public bool IsFuture { get; init; }

    public int IntensityLevel => ActivityCount switch
    {
        <= 0 => 0,
        1 => 1,
        2 => 2,
        _ => 3
    };

    public bool HasCompletion => CompletionCount > 0;

    public string TooltipText
    {
        get
        {
            var date = Date.ToString("M月d日");
            if (IsFuture)
            {
                return date;
            }
            if (ActivityCount == 0)
            {
                return $"{date} · 没有记录";
            }

            var completion = CompletionCount > 0 ? $" · 完成 {CompletionCount} 次" : string.Empty;
            var titles = string.IsNullOrWhiteSpace(TitleSummary) ? string.Empty : $"\n{TitleSummary}";
            return $"{date} · {ActivityCount} 条记录{completion}{titles}";
        }
    }
}
