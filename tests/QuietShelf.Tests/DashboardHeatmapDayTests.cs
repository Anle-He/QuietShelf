using QuietShelf.Models;

namespace QuietShelf.Tests;

public sealed class DashboardHeatmapDayTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(8, 3)]
    public void IntensityLevel_UsesFourHonestBuckets(int activityCount, int expected)
    {
        var day = new DashboardHeatmapDay
        {
            Date = new DateOnly(2026, 8, 26),
            ActivityCount = activityCount,
            CompletionCount = 0
        };

        Assert.Equal(expected, day.IntensityLevel);
    }

    [Fact]
    public void Tooltip_ExplainsActivityAndCompletion()
    {
        var day = new DashboardHeatmapDay
        {
            Date = new DateOnly(2026, 8, 26),
            ActivityCount = 3,
            CompletionCount = 1,
            TitleSummary = "致命女人"
        };

        Assert.Equal("8月26日 · 3 条记录 · 完成 1 次\n致命女人", day.TooltipText);
        Assert.True(day.HasCompletion);
    }
}
