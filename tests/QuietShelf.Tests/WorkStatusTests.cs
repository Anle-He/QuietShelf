using QuietShelf.Models;

namespace QuietShelf.Tests;

public sealed class WorkStatusTests
{
    [Fact]
    public async Task EditingHistory_DoesNotHideAnActiveExperience()
    {
        await using var context = await TempDatabase.CreateAsync();
        var work = new MediaWork { Title = "status-test", Kind = "book" };
        await context.Repository.AddWorkAsync(work);

        var historical = new MediaExperience
        {
            WorkId = work.Id,
            StartedOn = new DateOnly(2026, 8, 1),
            CompletedOn = new DateOnly(2026, 8, 2),
            Allure = 3,
            Immersion = 4,
            Rationality = 4,
            Illumination = 3
        };
        await context.Repository.AddExperienceAsync(historical);

        var active = new MediaExperience
        {
            WorkId = work.Id,
            StartedOn = new DateOnly(2026, 8, 3)
        };
        await context.Repository.AddExperienceAsync(active);

        await context.Repository.UpdateExperienceAsync(new MediaExperience
        {
            Id = historical.Id,
            WorkId = historical.WorkId,
            StartedOn = historical.StartedOn,
            CompletedOn = historical.CompletedOn,
            Allure = historical.Allure,
            Immersion = historical.Immersion,
            Rationality = historical.Rationality,
            Illumination = historical.Illumination,
            Notes = "edited",
            CreatedAt = historical.CreatedAt
        });

        Assert.Equal("in_progress", (await context.Repository.GetWorkAsync(work.Id))?.Status);

        await context.Repository.DeleteExperienceAsync(active.Id, work.Id);
        Assert.Equal("completed", (await context.Repository.GetWorkAsync(work.Id))?.Status);
    }
}
