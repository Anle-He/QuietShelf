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
}
