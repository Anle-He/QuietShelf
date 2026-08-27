namespace QuietShelf.Models;

public static class RatingScale
{
    public const int Minimum = 1;
    public const int AllureMaximum = 3;
    public const int DimensionMaximum = 5;
    public const double RankMaximum = 3.9;

    public static bool IsValidAllure(int? value) => IsInRange(value, AllureMaximum);

    public static bool IsValidDimension(int? value) => IsInRange(value, DimensionMaximum);

    public static double? Calculate(int? allure, int? immersion, int? rationality, int? illumination)
    {
        if (allure is null || immersion is null || rationality is null || illumination is null)
        {
            return null;
        }

        if (!IsValidAllure(allure) || !IsValidDimension(immersion)
            || !IsValidDimension(rationality) || !IsValidDimension(illumination))
        {
            throw new ArgumentOutOfRangeException(nameof(allure), "Rating values are outside the supported scale.");
        }

        return Math.Round(
            (allure.Value * 1.5 + immersion.Value + rationality.Value + illumination.Value) / 5,
            1,
            MidpointRounding.AwayFromZero);
    }

    public static void Validate(MediaExperience experience)
    {
        if (!IsValidAllure(experience.Allure)
            || !IsValidDimension(experience.Immersion)
            || !IsValidDimension(experience.Rationality)
            || !IsValidDimension(experience.Illumination))
        {
            throw new ArgumentOutOfRangeException(nameof(experience), "Rating values are outside the supported scale.");
        }
    }

    private static bool IsInRange(int? value, int maximum) =>
        !value.HasValue || value.Value >= Minimum && value.Value <= maximum;
}
