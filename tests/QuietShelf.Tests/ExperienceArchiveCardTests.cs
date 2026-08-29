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
    public void ArchiveCopy_OmitsAnEmptyNoteAndSeparatesProgressDetails()
    {
        var card = new ExperienceArchiveCard
        {
            ArchiveNumber = 12,
            Experience = new MediaExperience
            {
                WorkId = "work",
                ProgressDayCount = 2,
                ProgressEntryCount = 3,
                TotalEpisodes = 10,
                AvailableEpisodes = 10
            }
        };

        Assert.Equal("12", card.ArchiveNumberLabel);
        Assert.Equal("旅程 12", card.JourneyLabel);
        Assert.False(card.HasNotes);
        Assert.Equal("记录 2 天", card.ActivityDaysLabel);
        Assert.False(card.HasProgressAmount);
        Assert.Equal(string.Empty, card.ProgressAmountLabel);
    }

    [Fact]
    public void JourneyCopy_UsesInclusiveDaysAndDistinguishesSingleDayTrips()
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
        var singleDay = new ExperienceArchiveCard
        {
            ArchiveNumber = 1,
            Experience = new MediaExperience
            {
                WorkId = "work",
                StartedOn = new DateOnly(2026, 8, 28),
                CompletedOn = new DateOnly(2026, 8, 28)
            }
        };

        Assert.Equal("2026.08.26", multiDay.StartDateLabel);
        Assert.Equal("2026.08.28", multiDay.EndDateLabel);
        Assert.Equal(3, multiDay.JourneyDays);
        Assert.Equal("历时 3 天", multiDay.JourneyDaysLabel);
        Assert.Equal("当日抵达", singleDay.JourneyDaysLabel);
    }
}
