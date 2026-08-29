using QuietShelf.Models;

namespace QuietShelf.Tests;

public sealed class RatingScaleTests
{
    [Fact]
    public void Calculate_UsesTheDocumentedMaximum()
    {
        Assert.Equal(RatingScale.RankMaximum, RatingScale.Calculate(3, 5, 5, 5));
    }

    [Fact]
    public void Calculate_RejectsAllureAboveThree()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RatingScale.Calculate(4, 5, 5, 5));
    }

    [Fact]
    public void Calculate_ReturnsNullForAnIncompleteRating()
    {
        Assert.Null(RatingScale.Calculate(3, 5, null, 5));
    }

    [Fact]
    public void GetPercentage_ReturnsNullWithoutARank()
    {
        Assert.Null(RatingScale.GetPercentage(null));
    }

    [Theory]
    [InlineData(0.78, 0.2)]
    [InlineData(2.9, 0.7435897435897436)]
    [InlineData(3.9, 1.0)]
    public void GetPercentage_MapsRankToTheDocumentedMaximum(double rank, double expected)
    {
        Assert.Equal(expected, RatingScale.GetPercentage(rank)!.Value, 10);
    }

    [Theory]
    [InlineData(2.3, "bronze")]
    [InlineData(2.4, "silver")]
    [InlineData(3.1, "silver")]
    [InlineData(3.2, "gold")]
    public void MediaWork_AssignsAThreeTierRatingMark(double rank, string expectedTier)
    {
        var work = new MediaWork { Title = "测试作品", Kind = "book", AggregateRank = rank };

        Assert.Equal(expectedTier, work.AggregateScoreTier);
    }

    [Theory]
    [InlineData(0, "muted")]
    [InlineData(1, "green")]
    [InlineData(2, "gold")]
    [InlineData(8, "gold")]
    public void MediaWork_AssignsAColorTierToItsExperienceCount(int count, string expectedTier)
    {
        var work = new MediaWork { Title = "测试作品", Kind = "book", ExperienceCount = count };

        Assert.Equal(expectedTier, work.ExperienceCountColorTier);
    }
}
