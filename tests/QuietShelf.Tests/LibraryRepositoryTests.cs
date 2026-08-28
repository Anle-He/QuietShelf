using QuietShelf.Models;

namespace QuietShelf.Tests;

public sealed class LibraryRepositoryTests
{
    [Fact]
    public async Task ScreenWork_DoesNotPersistBookAuthor()
    {
        await using var context = await TempDatabase.CreateAsync();
        var work = new MediaWork
        {
            Title = "screen-author-test",
            Author = "not-applicable",
            Kind = "screen"
        };

        await context.Repository.AddWorkAsync(work);

        Assert.Null((await context.Repository.GetWorkAsync(work.Id))?.Author);
    }

    [Fact]
    public async Task WorkMetadataAndCoverLifecycle_PersistsLocalAssets()
    {
        await using var context = await TempDatabase.CreateAsync();
        var work = new MediaWork
        {
            Title = "cover-test",
            Subtitle = "original-subtitle",
            Author = "original-author",
            Kind = "book"
        };
        await context.Repository.AddWorkAsync(work);

        await context.Repository.UpdateWorkMetadataAsync(new MediaWork
        {
            Id = work.Id,
            Title = work.Title,
            Subtitle = "updated-subtitle",
            Author = "updated-author",
            Kind = work.Kind,
            Status = work.Status,
            CreatedAt = work.CreatedAt
        });

        var pixelPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        var firstSource = Path.Combine(context.Root, "first.png");
        var secondSource = Path.Combine(context.Root, "second.png");
        await File.WriteAllBytesAsync(firstSource, pixelPng);
        await File.WriteAllBytesAsync(secondSource, pixelPng);

        await context.Repository.AddCoversAsync(work.Id, [firstSource, secondSource]);
        var covers = await context.Repository.GetCoversAsync(work.Id);
        var storedWork = await context.Repository.GetWorkAsync(work.Id);

        Assert.Equal("updated-subtitle", storedWork?.Subtitle);
        Assert.Equal("updated-author", storedWork?.Author);
        Assert.Equal(2, covers.Count);
        Assert.True(covers[0].IsPrimary);
        Assert.All(covers, cover => Assert.True(File.Exists(cover.FilePath)));
        Assert.False(string.IsNullOrWhiteSpace(storedWork?.PrimaryCoverPath));

        await context.Repository.SetPrimaryCoverAsync(work.Id, covers[1].Id);
        var reordered = await context.Repository.GetCoversAsync(work.Id);
        Assert.Equal(covers[1].Id, reordered[0].Id);

        var deletedPath = reordered[1].FilePath;
        await context.Repository.DeleteCoverAsync(work.Id, reordered[1].Id);
        var remaining = await context.Repository.GetCoversAsync(work.Id);
        Assert.Single(remaining);
        Assert.True(remaining[0].IsPrimary);
        Assert.False(File.Exists(deletedPath));

        await context.Repository.DeleteWorkAsync(work.Id);
        Assert.Null(await context.Repository.GetWorkAsync(work.Id));
        Assert.False(Directory.Exists(context.Database.GetCoverDirectory(work.Id)));
    }

    [Fact]
    public async Task ProgressAndRatings_AggregateAcrossCompletedExperiences()
    {
        await using var context = await TempDatabase.CreateAsync();
        var work = new MediaWork { Title = "aggregate-test", Kind = "book" };
        await context.Repository.AddWorkAsync(work);

        var first = new MediaExperience
        {
            WorkId = work.Id,
            StartedOn = new DateOnly(2026, 8, 1),
            CreatedAt = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero)
        };
        await context.Repository.AddExperienceAsync(first);

        var firstProgress = new ProgressEntry
        {
            ExperienceId = first.Id,
            LoggedOn = new DateOnly(2026, 8, 1),
            Metric = "duration",
            Amount = 30,
            Notes = "first"
        };
        var secondProgress = new ProgressEntry
        {
            ExperienceId = first.Id,
            LoggedOn = new DateOnly(2026, 8, 2),
            Metric = "duration",
            Amount = 45
        };
        await context.Repository.AddProgressEntryAsync(firstProgress);
        await context.Repository.AddProgressEntryAsync(secondProgress);
        await context.Repository.UpdateProgressEntryAsync(new ProgressEntry
        {
            Id = firstProgress.Id,
            ExperienceId = first.Id,
            LoggedOn = firstProgress.LoggedOn,
            Metric = firstProgress.Metric,
            Amount = 35,
            Notes = firstProgress.Notes,
            CreatedAt = firstProgress.CreatedAt
        });
        await context.Repository.DeleteProgressEntryAsync(secondProgress.Id);

        await context.Repository.UpdateExperienceAsync(new MediaExperience
        {
            Id = first.Id,
            WorkId = work.Id,
            StartedOn = first.StartedOn,
            CompletedOn = new DateOnly(2026, 8, 2),
            Allure = 3,
            Immersion = 4,
            Rationality = 4,
            Illumination = 3,
            CreatedAt = first.CreatedAt
        });
        await context.Repository.AddExperienceAsync(new MediaExperience
        {
            WorkId = work.Id,
            StartedOn = new DateOnly(2026, 8, 5),
            CompletedOn = new DateOnly(2026, 8, 10),
            CreatedAt = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero),
            Allure = 3,
            Immersion = 5,
            Rationality = 5,
            Illumination = 5
        });

        var aggregate = await context.Repository.GetWorkAsync(work.Id);
        var history = await context.Repository.GetExperiencesAsync(work.Id);
        Assert.NotNull(aggregate);
        Assert.Equal(2, aggregate.ExperienceCount);
        Assert.Equal(2, aggregate.RatedExperienceCount);
        Assert.Equal(3.5, aggregate.AggregateRank);
        Assert.Equal(new DateOnly(2026, 8, 10), aggregate.LatestActivityOn);
        Assert.False(aggregate.HasActiveExperience);
        Assert.Equal("completed", aggregate.Status);
        Assert.Equal(2, history.Count);
        Assert.Equal(new DateOnly(2026, 8, 5), history[0].StartedOn);
        Assert.Equal(1, history[1].ProgressDayCount);
        Assert.Equal(1, history[1].ProgressEntryCount);
        Assert.Equal(35, history[1].TotalMinutes);
    }

    [Fact]
    public async Task ExperienceProgressSummary_AggregatesMetricsInOneHistoryDay()
    {
        await using var context = await TempDatabase.CreateAsync();
        var work = new MediaWork { Title = "progress-summary-test", Kind = "screen" };
        await context.Repository.AddWorkAsync(work);
        var experience = new MediaExperience
        {
            WorkId = work.Id,
            StartedOn = new DateOnly(2026, 8, 24)
        };
        await context.Repository.AddExperienceAsync(experience);
        await context.Repository.AddProgressEntryAsync(new ProgressEntry
        {
            ExperienceId = experience.Id,
            LoggedOn = new DateOnly(2026, 8, 24),
            Metric = "duration",
            Amount = 40
        });
        await context.Repository.AddProgressEntryAsync(new ProgressEntry
        {
            ExperienceId = experience.Id,
            LoggedOn = new DateOnly(2026, 8, 24),
            Metric = "episodes",
            Amount = 2
        }, totalEpisodes: 12);

        var history = await context.Repository.GetExperiencesAsync(work.Id);

        Assert.Single(history);
        Assert.Equal(1, history[0].ProgressDayCount);
        Assert.Equal(2, history[0].ProgressEntryCount);
        Assert.Equal(40, history[0].TotalMinutes);
        Assert.Equal(2, history[0].TotalEpisodes);
        Assert.Equal(12, history[0].AvailableEpisodes);
    }

    [Fact]
    public async Task ProgressMetadata_UpdateUsesExperienceOwnership()
    {
        await using var context = await TempDatabase.CreateAsync();
        var firstWork = new MediaWork { Title = "first-work", Kind = "screen" };
        var secondWork = new MediaWork { Title = "second-work", Kind = "screen" };
        await context.Repository.AddWorkAsync(firstWork);
        await context.Repository.AddWorkAsync(secondWork);
        var experience = new MediaExperience
        {
            WorkId = firstWork.Id,
            StartedOn = new DateOnly(2026, 8, 22)
        };
        await context.Repository.AddExperienceAsync(experience);

        await context.Repository.AddProgressEntryAsync(new ProgressEntry
        {
            ExperienceId = experience.Id,
            LoggedOn = new DateOnly(2026, 8, 22),
            Metric = "episodes",
            Amount = 1
        }, totalEpisodes: 10);

        Assert.Equal(10, (await context.Repository.GetWorkAsync(firstWork.Id))?.TotalEpisodes);
        Assert.Null((await context.Repository.GetWorkAsync(secondWork.Id))?.TotalEpisodes);
    }

    [Fact]
    public async Task WorkLatestActivity_UsesCompletionAfterEarlierProgress()
    {
        await using var context = await TempDatabase.CreateAsync();
        var work = new MediaWork { Title = "latest-activity-test", Kind = "book" };
        await context.Repository.AddWorkAsync(work);
        var experience = new MediaExperience
        {
            WorkId = work.Id,
            StartedOn = new DateOnly(2026, 8, 1),
            CreatedAt = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero)
        };
        await context.Repository.AddExperienceAsync(experience);
        await context.Repository.AddProgressEntryAsync(new ProgressEntry
        {
            ExperienceId = experience.Id,
            LoggedOn = new DateOnly(2026, 8, 2),
            Metric = "duration",
            Amount = 30
        });
        await context.Repository.UpdateExperienceAsync(new MediaExperience
        {
            Id = experience.Id,
            WorkId = work.Id,
            StartedOn = experience.StartedOn,
            CompletedOn = new DateOnly(2026, 8, 10),
            CreatedAt = experience.CreatedAt
        });

        var storedWork = await context.Repository.GetWorkAsync(work.Id);

        Assert.Equal(new DateOnly(2026, 8, 10), storedWork?.LatestActivityOn);
    }

    [Fact]
    public async Task EpisodeProgress_TracksAvailableAndWatchedTotals()
    {
        await using var context = await TempDatabase.CreateAsync();
        var work = new MediaWork { Title = "episode-test", Kind = "screen" };
        await context.Repository.AddWorkAsync(work);
        var experience = new MediaExperience
        {
            WorkId = work.Id,
            StartedOn = new DateOnly(2026, 8, 21)
        };
        await context.Repository.AddExperienceAsync(experience);
        await context.Repository.AddProgressEntryAsync(new ProgressEntry
        {
            ExperienceId = experience.Id,
            LoggedOn = new DateOnly(2026, 8, 21),
            Metric = "episodes",
            Amount = 2
        }, totalEpisodes: 12);

        var storedWork = await context.Repository.GetWorkAsync(work.Id);
        var history = await context.Repository.GetExperiencesAsync(work.Id);
        Assert.Equal(12, storedWork?.TotalEpisodes);
        Assert.Single(history);
        Assert.Equal(2, history[0].TotalEpisodes);
        Assert.Equal(12, history[0].AvailableEpisodes);
        Assert.Contains("已看 2 / 12 集", history[0].ProgressSummaryLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CoverCleanup_ReconcilesInterruptedDeletionFiles()
    {
        await using var context = await TempDatabase.CreateAsync();
        var work = new MediaWork { Title = "cover-reconcile-test", Kind = "book" };
        await context.Repository.AddWorkAsync(work);
        var sourcePath = Path.Combine(context.Root, "source.png");
        await File.WriteAllBytesAsync(sourcePath, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        await context.Repository.AddCoversAsync(work.Id, [sourcePath]);
        var cover = Assert.Single(await context.Repository.GetCoversAsync(work.Id));
        var stagedPath = cover.FilePath + ".deleting";
        File.Move(cover.FilePath, stagedPath);

        await context.Repository.GetCoversAsync(work.Id);

        Assert.True(File.Exists(cover.FilePath));
        Assert.False(File.Exists(stagedPath));

        var orphanPath = Path.Combine(context.Database.GetCoverDirectory(work.Id), "orphan.png.deleting");
        await File.WriteAllTextAsync(orphanPath, "orphan");
        await context.Repository.GetCoversAsync(work.Id);
        Assert.False(File.Exists(orphanPath));
    }

    [Fact]
    public async Task ExperienceChronology_RejectsCompletionBeforeStart()
    {
        await using var context = await TempDatabase.CreateAsync();
        var work = new MediaWork { Title = "chronology-test", Kind = "book" };
        await context.Repository.AddWorkAsync(work);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Repository.AddExperienceAsync(new MediaExperience
            {
                WorkId = work.Id,
                StartedOn = new DateOnly(2026, 8, 10),
                CompletedOn = new DateOnly(2026, 8, 9)
            }));

        Assert.Contains("Completion date", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await context.Repository.GetExperiencesAsync(work.Id));
    }

    [Fact]
    public async Task ProgressValidation_RejectsEarlyAndBookEpisodeEntries()
    {
        await using var context = await TempDatabase.CreateAsync();
        var work = new MediaWork { Title = "progress-validation-test", Kind = "book" };
        await context.Repository.AddWorkAsync(work);
        var experience = new MediaExperience
        {
            WorkId = work.Id,
            StartedOn = new DateOnly(2026, 8, 10)
        };
        await context.Repository.AddExperienceAsync(experience);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Repository.AddProgressEntryAsync(new ProgressEntry
            {
                ExperienceId = experience.Id,
                LoggedOn = new DateOnly(2026, 8, 9),
                Metric = "duration",
                Amount = 10
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Repository.AddProgressEntryAsync(new ProgressEntry
            {
                ExperienceId = experience.Id,
                LoggedOn = new DateOnly(2026, 8, 10),
                Metric = "episodes",
                Amount = 1
            }));

        Assert.Empty(await context.Repository.GetProgressEntriesAsync(experience.Id));
    }

    [Fact]
    public async Task ActiveExperiences_LoadForAllActiveWorksInOneResult()
    {
        await using var context = await TempDatabase.CreateAsync();
        var firstWork = new MediaWork { Title = "active-one", Kind = "book" };
        var secondWork = new MediaWork { Title = "active-two", Kind = "screen" };
        var completedWork = new MediaWork { Title = "completed", Kind = "book" };
        await context.Repository.AddWorkAsync(firstWork);
        await context.Repository.AddWorkAsync(secondWork);
        await context.Repository.AddWorkAsync(completedWork);
        await context.Repository.AddExperienceAsync(new MediaExperience
        {
            WorkId = firstWork.Id,
            StartedOn = new DateOnly(2026, 8, 1)
        });
        await context.Repository.AddExperienceAsync(new MediaExperience
        {
            WorkId = secondWork.Id,
            StartedOn = new DateOnly(2026, 8, 2)
        });
        await context.Repository.AddExperienceAsync(new MediaExperience
        {
            WorkId = completedWork.Id,
            StartedOn = new DateOnly(2026, 8, 1),
            CompletedOn = new DateOnly(2026, 8, 3)
        });

        var active = await context.Repository.GetActiveExperiencesAsync();

        Assert.Equal(2, active.Count);
        Assert.Contains(firstWork.Id, active.Keys);
        Assert.Contains(secondWork.Id, active.Keys);
        Assert.DoesNotContain(completedWork.Id, active.Keys);
    }
}
