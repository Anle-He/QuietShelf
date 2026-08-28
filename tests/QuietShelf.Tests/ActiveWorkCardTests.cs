using QuietShelf.Models;

namespace QuietShelf.Tests;

public sealed class ActiveWorkCardTests
{
    [Fact]
    public void ActiveCard_UsesExistingWorkAndExperienceSummaries()
    {
        var card = new ActiveWorkCard
        {
            Work = new MediaWork
            {
                Title = "测试作品",
                Kind = "screen",
                LatestActivityOn = new DateOnly(2026, 8, 27)
            },
            Experience = new MediaExperience
            {
                WorkId = "work",
                StartedOn = new DateOnly(2026, 8, 26),
                ProgressDayCount = 2,
                TotalEpisodes = 6,
                AvailableEpisodes = 10
            }
        };

        Assert.Equal("测试作品", card.Title);
        Assert.Equal("记录 2 天 · 已看 6 / 10 集", card.ProgressLabel);
        Assert.Equal("最近 2026-08-27", card.ActivityLabel);
        Assert.True(card.HasEpisodeProgress);
        Assert.Equal(60, card.ProgressPercent);
        Assert.Equal(0.6, card.ProgressFraction);
        Assert.Equal("60%", card.ProgressPercentLabel);
        Assert.Equal("6 / 10 集", card.EpisodeProgressLabel);
    }
}
