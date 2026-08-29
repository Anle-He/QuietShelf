using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuietShelf.Data;
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
        var archive = new ExperienceArchiveCard { ArchiveNumber = 1, Experience = history[0] };
        Assert.Equal("记录 1 天", archive.ActivityDaysLabel);
    }

    [Fact]
    public async Task RecentTimeline_CombinesProgressAndCompletionAcrossWorks()
    {
        await using var context = await TempDatabase.CreateAsync();
        var screen = new MediaWork { Title = "timeline-screen", Kind = "screen" };
        var book = new MediaWork { Title = "timeline-book", Kind = "book" };
        await context.Repository.AddWorkAsync(screen);
        await context.Repository.AddWorkAsync(book);

        var viewing = new MediaExperience { WorkId = screen.Id, StartedOn = new DateOnly(2026, 8, 20) };
        await context.Repository.AddExperienceAsync(viewing);
        await context.Repository.AddProgressEntryAsync(new ProgressEntry
        {
            ExperienceId = viewing.Id,
            LoggedOn = new DateOnly(2026, 8, 27),
            Metric = "episodes",
            Amount = 3,
            Notes = "a useful note"
        });

        var reading = new MediaExperience
        {
            WorkId = book.Id,
            StartedOn = new DateOnly(2026, 8, 21),
            CompletedOn = new DateOnly(2026, 8, 26)
        };
        await context.Repository.AddExperienceAsync(reading);

        var timeline = await context.Repository.GetRecentTimelineAsync();

        Assert.Equal(2, timeline.Count);
        Assert.True(timeline[0].IsLatest);
        Assert.Equal(screen.Id, timeline[0].WorkId);
        Assert.Equal("看了 3 集", timeline[0].ActionLabel);
        Assert.Equal("a useful note", timeline[0].NotesExcerpt);
        Assert.False(timeline[1].IsLatest);
        Assert.Equal(book.Id, timeline[1].WorkId);
        Assert.Equal("完成一次阅读", timeline[1].ActionLabel);
    }

    [Fact]
    public async Task ActivityHeatmap_GroupsProgressAndCompletionByDay()
    {
        await using var context = await TempDatabase.CreateAsync();
        var work = new MediaWork { Title = "Heatmap", Kind = "screen" };
        await context.Repository.AddWorkAsync(work);
        var experience = new MediaExperience
        {
            WorkId = work.Id,
            StartedOn = new DateOnly(2026, 8, 20)
        };
        await context.Repository.AddExperienceAsync(experience);
        await context.Repository.AddProgressEntryAsync(new ProgressEntry
        {
            ExperienceId = experience.Id,
            LoggedOn = new DateOnly(2026, 8, 26),
            Metric = "episodes",
            Amount = 2
        });
        await context.Repository.AddProgressEntryAsync(new ProgressEntry
        {
            ExperienceId = experience.Id,
            LoggedOn = new DateOnly(2026, 8, 26),
            Metric = "episodes",
            Amount = 1
        });
        await context.Repository.UpdateExperienceAsync(new MediaExperience
        {
            Id = experience.Id,
            WorkId = work.Id,
            StartedOn = experience.StartedOn,
            CompletedOn = new DateOnly(2026, 8, 26)
        });

        var days = await context.Repository.GetActivityHeatmapAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        var day = Assert.Single(days);
        Assert.Equal(new DateOnly(2026, 8, 26), day.Date);
        Assert.Equal(3, day.ActivityCount);
        Assert.Equal(1, day.CompletionCount);
        Assert.Contains("Heatmap", day.TitleSummary);
    }

    [Fact]
    public async Task DashboardActivity_TracksHistoricalEditsAndDeletesWithoutInflatingWorkTotals()
    {
        await using var context = await TempDatabase.CreateAsync();
        var work = new MediaWork { Title = "historical-dashboard", Kind = "book" };
        await context.Repository.AddWorkAsync(work);
        var startedOn = new DateOnly(2026, 8, 1);
        var completedOn = startedOn.AddDays(9);
        var experience = new MediaExperience { WorkId = work.Id, StartedOn = startedOn };
        await context.Repository.AddExperienceAsync(experience);
        var progress = new ProgressEntry
        {
            ExperienceId = experience.Id, LoggedOn = startedOn, Metric = "duration", Amount = 20
        };
        await context.Repository.AddProgressEntryAsync(progress);
        await context.Repository.AddProgressEntryAsync(new ProgressEntry
        {
            ExperienceId = experience.Id, LoggedOn = startedOn, Metric = "duration", Amount = 30
        });
        await context.Repository.UpdateExperienceAsync(new MediaExperience
        {
            Id = experience.Id, WorkId = work.Id, StartedOn = startedOn, CompletedOn = completedOn,
            Allure = 3, Immersion = 5, Rationality = 5, Illumination = 5
        });

        var aggregate = Assert.Single(await context.Repository.GetWorksAsync());
        Assert.Equal(1, aggregate.ExperienceCount);
        Assert.Equal(1, aggregate.RatedExperienceCount);
        Assert.Equal(RatingScale.RankMaximum, aggregate.AggregateRank);
        Assert.Equal(completedOn, aggregate.LatestActivityOn);
        var latest = Assert.Single(await context.Repository.GetRecentTimelineAsync(1));
        Assert.Equal("completion", latest.EventType);
        Assert.Equal(completedOn, latest.LoggedOn);
        var completionDay = Assert.Single(await context.Repository.GetActivityHeatmapAsync(completedOn, completedOn));
        Assert.Equal(1, completionDay.ActivityCount);
        Assert.Equal(1, completionDay.CompletionCount);

        var editedOn = startedOn.AddDays(1);
        await context.Repository.UpdateProgressEntryAsync(new ProgressEntry
        {
            Id = progress.Id, ExperienceId = experience.Id, LoggedOn = editedOn,
            Metric = "duration", Amount = 25, CreatedAt = progress.CreatedAt
        });
        var timeline = await context.Repository.GetRecentTimelineAsync();
        Assert.Equal(new[] { completedOn, editedOn, startedOn }, timeline.Select(item => item.LoggedOn));
        Assert.Equal(25, timeline[1].Amount);
        var days = await context.Repository.GetActivityHeatmapAsync(startedOn, completedOn);
        Assert.Equal(3, days.Count);
        Assert.All(days, day => Assert.Equal(1, day.ActivityCount));

        await context.Repository.DeleteProgressEntryAsync(progress.Id);
        Assert.Empty(await context.Repository.GetActivityHeatmapAsync(editedOn, editedOn));
        Assert.DoesNotContain(await context.Repository.GetRecentTimelineAsync(), item => item.Id == progress.Id);
        await context.Repository.DeleteExperienceAsync(experience.Id, work.Id);
        Assert.Empty(await context.Repository.GetRecentTimelineAsync());
        Assert.Empty(await context.Repository.GetActivityHeatmapAsync(startedOn, completedOn));
        aggregate = Assert.Single(await context.Repository.GetWorksAsync());
        Assert.Equal(0, aggregate.ExperienceCount);
        Assert.Null(aggregate.LatestActivityOn);
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

    [Theory]
    [InlineData(1, 10, 2, 10)]
    [InlineData(1, null, 2, 2)]
    [InlineData(1, null, null, 1)]
    [InlineData(null, 10, null, 10)]
    [InlineData(null, null, null, 28)]
    public async Task WorkLatestActivity_UsesCreatedDateOnlyWhenActivityDatesAreMissing(
        int? startDay, int? completionDay, int? progressDay, int expectedDay)
    {
        await using var context = await TempDatabase.CreateAsync();
        var work = new MediaWork { Title = "backdated-activity-test", Kind = "book" };
        await context.Repository.AddWorkAsync(work);
        var experience = new MediaExperience
        {
            WorkId = work.Id,
            StartedOn = startDay is { } start ? new DateOnly(2026, 8, start) : null,
            CompletedOn = completionDay is { } completion ? new DateOnly(2026, 8, completion) : null,
            CreatedAt = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero)
        };
        await context.Repository.AddExperienceAsync(experience);
        if (progressDay is { } progress)
        {
            await context.Repository.AddProgressEntryAsync(new ProgressEntry
            {
                ExperienceId = experience.Id,
                LoggedOn = new DateOnly(2026, 8, progress),
                Metric = "duration",
                Amount = 30
            });
        }

        Assert.Equal(new DateOnly(2026, 8, expectedDay),
            (await context.Repository.GetWorkAsync(work.Id))?.LatestActivityOn);
    }

    [Fact]
    public async Task CoverRead_DuringImport_PreservesFilesAcrossRepositoryInstances()
    {
        await using var context = await TempDatabase.CreateAsync();
        var work = new MediaWork { Title = "concurrent-cover-test", Kind = "book" };
        await context.Repository.AddWorkAsync(work);
        var sourcePath = Path.Combine(context.Root, "source.png");
        await File.WriteAllBytesAsync(sourcePath, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        using var sources = new PausingCoverSources(sourcePath);
        var importing = Task.Run(() => context.Repository.AddCoversAsync(work.Id, sources));
        Task<IReadOnlyList<WorkCover>> reading;
        try
        {
            // Pause with the first JPEG on disk but not yet committed to SQLite.
            await sources.FirstCoverStaged.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var otherRepository = new LibraryRepository(new Database(context.Database.DatabasePath));
            reading = otherRepository.GetCoversAsync(work.Id);
        }
        finally
        {
            sources.Resume();
        }
        await Task.WhenAll(importing, reading).WaitAsync(TimeSpan.FromSeconds(10));

        var covers = await context.Repository.GetCoversAsync(work.Id);
        Assert.Equal(2, covers.Count);
        Assert.All(covers, cover => Assert.True(File.Exists(cover.FilePath), cover.FilePath));
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
        var abandonedImportPath = Path.Combine(context.Database.GetCoverDirectory(work.Id), "orphan.jpg.adding");
        var uncommittedCoverPath = Path.Combine(context.Database.GetCoverDirectory(work.Id), "orphan.jpg");
        await File.WriteAllTextAsync(abandonedImportPath, "abandoned import");
        await File.WriteAllTextAsync(uncommittedCoverPath, "uncommitted cover");
        await context.Repository.GetCoversAsync(work.Id);
        Assert.False(File.Exists(orphanPath));
        Assert.False(File.Exists(abandonedImportPath));
        Assert.False(File.Exists(uncommittedCoverPath));
    }

    [Theory]
    [InlineData(2400, 1200, 1600, 800)]
    [InlineData(1200, 2400, 800, 1600)]
    public async Task CoverImport_NormalizesDimensionsFormatAndTransparency(
        int sourceWidth,
        int sourceHeight,
        int expectedWidth,
        int expectedHeight)
    {
        await using var context = await TempDatabase.CreateAsync();
        var work = new MediaWork { Title = "optimized-cover-test", Kind = "book" };
        await context.Repository.AddWorkAsync(work);
        var sourcePath = Path.Combine(context.Root, "transparent-source.png");
        var pixels = new byte[sourceWidth * sourceHeight * 4];
        var source = BitmapSource.Create(
            sourceWidth,
            sourceHeight,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            sourceWidth * 4);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(source));
        await using (var output = File.Create(sourcePath))
        {
            png.Save(output);
        }

        await context.Repository.AddCoversAsync(work.Id, [sourcePath]);

        var cover = Assert.Single(await context.Repository.GetCoversAsync(work.Id));
        Assert.EndsWith(".jpg", cover.FileName, StringComparison.Ordinal);
        var signature = await File.ReadAllBytesAsync(cover.FilePath);
        Assert.True(signature.Length > 3);
        Assert.Equal(0xFF, signature[0]);
        Assert.Equal(0xD8, signature[1]);
        using var input = File.OpenRead(cover.FilePath);
        var frame = BitmapFrame.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert.Equal(expectedWidth, frame.PixelWidth);
        Assert.Equal(expectedHeight, frame.PixelHeight);
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgr24, null, 0);
        var firstPixel = new byte[3];
        converted.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), firstPixel, 3, 0);
        Assert.All(firstPixel, channel => Assert.InRange(channel, (byte)245, byte.MaxValue));
    }

    [Fact]
    public async Task CoverImport_RejectsInvalidImageContentWithoutLeavingFiles()
    {
        await using var context = await TempDatabase.CreateAsync();
        var work = new MediaWork { Title = "invalid-cover-test", Kind = "book" };
        await context.Repository.AddWorkAsync(work);
        var sourcePath = Path.Combine(context.Root, "invalid.png");
        await File.WriteAllTextAsync(sourcePath, "not an image");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Repository.AddCoversAsync(work.Id, [sourcePath]));

        Assert.Empty(await context.Repository.GetCoversAsync(work.Id));
        var coverDirectory = context.Database.GetCoverDirectory(work.Id);
        Assert.False(Directory.Exists(coverDirectory)
                     && Directory.EnumerateFiles(coverDirectory, "*", SearchOption.TopDirectoryOnly).Any());
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

    private sealed class PausingCoverSources(string sourcePath) : IReadOnlyList<string>, IDisposable
    {
        private readonly ManualResetEventSlim _resume = new(false);
        public TaskCompletionSource FirstCoverStaged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count => 2;
        public string this[int index] => index is 0 or 1 ? sourcePath : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<string> GetEnumerator()
        {
            yield return sourcePath;
            FirstCoverStaged.TrySetResult();
            if (!_resume.Wait(TimeSpan.FromSeconds(15)))
            {
                throw new TimeoutException("Timed out waiting to resume the cover import.");
            }
            yield return sourcePath;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public void Resume() => _resume.Set();
        public void Dispose() => _resume.Dispose();
    }
}
