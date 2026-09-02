using QuietShelf.Models;

namespace QuietShelf.Tests;

public sealed class ExperienceArchiveCardTests
{
    [Fact]
    public void RatingValues_PreserveEachDimensionOnItsOriginalScale()
    {
        var card = new ExperienceArchiveCard
        {
            ArchiveNumber = 1,
            Experience = new MediaExperience
            {
                WorkId = "work",
                Allure = 3,
                Immersion = 5,
                Rationality = 4,
                Illumination = 2
            }
        };

        Assert.Equal("01", card.ArchiveNumberLabel);
        Assert.True(card.HasCompleteRating);
        Assert.Equal(3, card.Allure);
        Assert.Equal(5, card.Immersion);
        Assert.Equal(4, card.Rationality);
        Assert.Equal(2, card.Illumination);
    }

    [Fact]
    public void ArchiveCopy_OmitsAnEmptyNoteAndUsesRecordNumber()
    {
        var card = new ExperienceArchiveCard
        {
            ArchiveNumber = 12,
            Experience = new MediaExperience
            {
                WorkId = "work",
                ProgressEntryCount = 3
            }
        };

        Assert.Equal("12", card.ArchiveNumberLabel);
        Assert.Equal("记录 12", card.JourneyLabel);
        Assert.False(card.HasNotes);
    }

    [Fact]
    public void ArchiveCopy_UsesOnlyTheCompletionDate()
    {
        var multiDay = new ExperienceArchiveCard
        {
            ArchiveNumber = 2,
            Experience = new MediaExperience
            {
                WorkId = "work",
                StartedOn = new DateOnly(2026, 8, 26),
                CompletedOn = new DateOnly(2026, 8, 28)
            }
        };
        Assert.Equal("2026.08.28", multiDay.EndDateLabel);
        Assert.Equal("08.28", multiDay.CompletionMonthDayLabel);
        Assert.Equal("2026", multiDay.CompletionYearLabel);
        Assert.Equal("周五", multiDay.CompletionWeekdayLabel);
    }
}
