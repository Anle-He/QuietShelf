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
    public async Task Ratings_AggregateAcrossCompletedExperiencesAndReadHistoricalEntryCount()
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

        await context.SeedHistoricalProgressAsync(first.Id, new DateOnly(2026, 8, 1));

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
        Assert.Equal(1, history[1].ProgressEntryCount);
        Assert.Equal(1L, await context.CountHistoricalProgressAsync(first.Id));

        await context.Repository.DeleteExperienceAsync(first.Id, work.Id);
        Assert.Equal(0L, await context.CountHistoricalProgressAsync(first.Id));
    }

    [Fact]
    public async Task RecentTimeline_ListsOnlyCompletedExperiences()
    {
        await using var context = await TempDatabase.CreateAsync();
        var screen = new MediaWork { Title = "timeline-screen", Kind = "screen" };
        var book = new MediaWork { Title = "timeline-book", Kind = "book" };
        await context.Repository.AddWorkAsync(screen);
        await context.Repository.AddWorkAsync(book);

        var viewing = new MediaExperience { WorkId = screen.Id, StartedOn = new DateOnly(2026, 8, 20) };
        await context.Repository.AddExperienceAsync(viewing);

        var reading = new MediaExperience
        {
            WorkId = book.Id,
            StartedOn = new DateOnly(2026, 8, 21),
            CompletedOn = new DateOnly(2026, 8, 26)
        };
        await context.Repository.AddExperienceAsync(reading);

        var timeline = await context.Repository.GetRecentTimelineAsync();

        var item = Assert.Single(timeline);
        Assert.True(item.IsLatest);
        Assert.Equal(book.Id, item.WorkId);
        Assert.Equal("完成一次阅读", item.ActionLabel);
    }

    [Fact]
    public async Task DashboardShowcase_RanksCompletedWorksAndAuthorsFromCompleteRatings()
    {
        await using var context = await TempDatabase.CreateAsync();
        var alpha = new MediaWork { Title = "Alpha Book", Author = "Alpha", Kind = "book" };
        var beta = new MediaWork { Title = "Beta Book", Author = "Beta", Kind = "book" };
        var screen = new MediaWork { Title = "Screen", Kind = "screen" };
        await context.Repository.AddWorkAsync(alpha);
        await context.Repository.AddWorkAsync(beta);
        await context.Repository.AddWorkAsync(screen);

        await context.Repository.AddExperienceAsync(new MediaExperience
        {
            WorkId = alpha.Id, CompletedOn = new DateOnly(2026, 1, 2),
            Allure = 3, Immersion = 5, Rationality = 5, Illumination = 5
        });
        for (var day = 3; day <= 5; day++)
        {
            await context.Repository.AddExperienceAsync(new MediaExperience
            {
                WorkId = beta.Id, CompletedOn = new DateOnly(2026, 1, day),
                Allure = 3, Immersion = 4, Rationality = 4, Illumination = 4
            });
        }
        await context.Repository.AddExperienceAsync(new MediaExperience
        {
            WorkId = screen.Id, CompletedOn = new DateOnly(2026, 1, 6)
        });

        var showcase = await context.Repository.GetDashboardShowcaseAsync();

        Assert.Equal(3, showcase.CompletedWorks.Count);
        Assert.Equal(screen.Id, showcase.CompletedWorks[0].WorkId);
        Assert.Equal(new DateOnly(2026, 1, 2), showcase.CompletedWorks.Single(work => work.WorkId == alpha.Id).FirstCompletedOn);
        Assert.Equal(2, showcase.TopAuthors.Count);
        Assert.Equal([1, 2], showcase.TopAuthors.Select(author => author.Position));
        Assert.All(showcase.TopAuthors, author => Assert.InRange(author.WeightedRank, 0, RatingScale.RankMaximum));
        Assert.Equal(3, showcase.TopAuthors.Single(author => author.Author == "Beta").RatingCount);
    }

    [Fact]
    public async Task DashboardTimeline_TracksCompletionsWithoutInflatingWorkTotals()
    {
        await using var context = await TempDatabase.CreateAsync();
        var work = new MediaWork { Title = "historical-dashboard", Kind = "book" };
        await context.Repository.AddWorkAsync(work);
        var startedOn = new DateOnly(2026, 8, 1);
        var completedOn = startedOn.AddDays(9);
        var experience = new MediaExperience { WorkId = work.Id, StartedOn = startedOn };
        await context.Repository.AddExperienceAsync(experience);
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
        await context.Repository.DeleteExperienceAsync(experience.Id, work.Id);
        Assert.Empty(await context.Repository.GetRecentTimelineAsync());
        aggregate = Assert.Single(await context.Repository.GetWorksAsync());
        Assert.Equal(0, aggregate.ExperienceCount);
        Assert.Null(aggregate.LatestActivityOn);
    }

    [Theory]
    [InlineData(1, 10, 10)]
    [InlineData(1, null, null)]
    [InlineData(null, 10, 10)]
    [InlineData(null, null, null)]
    public async Task WorkLatestActivity_UsesOnlyCompletionDates(
        int? startDay, int? completionDay, int? expectedDay)
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
        var latest = (await context.Repository.GetWorkAsync(work.Id))?.LatestActivityOn;
        if (expectedDay is { } day)
        {
            Assert.Equal(new DateOnly(2026, 8, day), latest);
        }
        else
        {
            Assert.Null(latest);
        }
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
