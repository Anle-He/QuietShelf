using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using QuietShelf.Data;
using QuietShelf.Models;
using Wpf.Ui.Appearance;

namespace QuietShelf;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplicationAccentColorManager.Apply(
            Color.FromRgb(0x35, 0x66, 0x4F),
            ApplicationTheme.Light,
            systemGlassColor: false,
            systemAccentColor: false);

        if (e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var sample = new MediaExperience
                {
                    WorkId = "smoke-test",
                    Allure = 3,
                    Immersion = 4,
                    Rationality = 4,
                    Illumination = 3
                };
                if (sample.Rank != 3.1)
                {
                    throw new InvalidOperationException("Rank calculation failed.");
                }

                var smokeDirectory = Path.Combine(Path.GetTempPath(), "QuietShelf-Smoke-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(smokeDirectory);
                try
                {
                    var smokeDatabase = new Database(Path.Combine(smokeDirectory, "smoke.db"));
                    await smokeDatabase.InitializeAsync();
                    var repository = new LibraryRepository(smokeDatabase);
                    var work = new MediaWork { Title = "smoke-test", Subtitle = "subtitle-smoke-test", Kind = "book" };
                    await repository.AddWorkAsync(work);
                    var active = new MediaExperience
                    {
                        WorkId = work.Id,
                        StartedOn = new DateOnly(2026, 8, 1),
                    };
                    await repository.AddExperienceAsync(active);
                    var during = await repository.GetWorkAsync(work.Id);
                    if (during is null || during.Subtitle != "subtitle-smoke-test"
                        || !during.HasActiveExperience || during.ExperienceCount != 0)
                    {
                        throw new InvalidOperationException("Active experience lifecycle failed.");
                    }
                    await repository.UpdateWorkMetadataAsync(new MediaWork
                    {
                        Id = work.Id,
                        Title = work.Title,
                        Subtitle = "updated-subtitle-smoke-test",
                        Kind = work.Kind,
                        Status = during.Status,
                        CreatedAt = work.CreatedAt
                    });
                    if ((await repository.GetWorkAsync(work.Id))?.Subtitle != "updated-subtitle-smoke-test")
                    {
                        throw new InvalidOperationException("Work subtitle update failed.");
                    }
                    var firstCoverSource = Path.Combine(smokeDirectory, "first.png");
                    var secondCoverSource = Path.Combine(smokeDirectory, "second.png");
                    var pixelPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
                    await File.WriteAllBytesAsync(firstCoverSource, pixelPng);
                    await File.WriteAllBytesAsync(secondCoverSource, pixelPng);
                    await repository.AddCoversAsync(work.Id, [firstCoverSource, secondCoverSource]);
                    var covers = await repository.GetCoversAsync(work.Id);
                    var workWithCover = await repository.GetWorkAsync(work.Id);
                    if (covers.Count != 2 || !covers[0].IsPrimary || !File.Exists(covers[0].FilePath)
                        || string.IsNullOrWhiteSpace(workWithCover?.PrimaryCoverPath))
                    {
                        throw new InvalidOperationException("Cover import failed.");
                    }
                    await repository.SetPrimaryCoverAsync(work.Id, covers[1].Id);
                    var reorderedCovers = await repository.GetCoversAsync(work.Id);
                    if (reorderedCovers[0].Id != covers[1].Id)
                    {
                        throw new InvalidOperationException("Primary cover ordering failed.");
                    }
                    var deletedCoverPath = reorderedCovers[1].FilePath;
                    await repository.DeleteCoverAsync(work.Id, reorderedCovers[1].Id);
                    var remainingCovers = await repository.GetCoversAsync(work.Id);
                    if (remainingCovers.Count != 1 || !remainingCovers[0].IsPrimary || File.Exists(deletedCoverPath))
                    {
                        throw new InvalidOperationException("Cover deletion failed.");
                    }
                    var firstProgress = new ProgressEntry
                    {
                        ExperienceId = active.Id,
                        LoggedOn = new DateOnly(2026, 8, 1),
                        Metric = "duration",
                        Amount = 30,
                        Notes = "smoke progress"
                    };
                    await repository.AddProgressEntryAsync(firstProgress, work.Id);
                    var secondProgress = new ProgressEntry
                    {
                        ExperienceId = active.Id,
                        LoggedOn = new DateOnly(2026, 8, 2),
                        Metric = "duration",
                        Amount = 45
                    };
                    await repository.AddProgressEntryAsync(secondProgress, work.Id);
                    await repository.UpdateProgressEntryAsync(new ProgressEntry
                    {
                        Id = firstProgress.Id,
                        ExperienceId = active.Id,
                        LoggedOn = firstProgress.LoggedOn,
                        Metric = firstProgress.Metric,
                        Amount = 35,
                        Notes = firstProgress.Notes,
                        CreatedAt = firstProgress.CreatedAt
                    }, work.Id);
                    await repository.DeleteProgressEntryAsync(secondProgress.Id, work.Id);
                    await repository.UpdateExperienceAsync(new MediaExperience
                    {
                        Id = active.Id,
                        WorkId = work.Id,
                        StartedOn = active.StartedOn,
                        CompletedOn = new DateOnly(2026, 8, 2),
                        Allure = 3, Immersion = 4, Rationality = 4, Illumination = 3,
                        CreatedAt = active.CreatedAt
                    });
                    await repository.AddExperienceAsync(new MediaExperience
                    {
                        WorkId = work.Id,
                        StartedOn = new DateOnly(2026, 8, 5),
                        CompletedOn = new DateOnly(2026, 8, 10),
                        Allure = 4, Immersion = 5, Rationality = 4, Illumination = 4
                    });
                    var aggregate = await repository.GetWorkAsync(work.Id);
                    var history = await repository.GetExperiencesAsync(work.Id);
                    if (aggregate is null || aggregate.ExperienceCount != 2 || aggregate.RatedExperienceCount != 2
                        || aggregate.AggregateRank != 3.5 || aggregate.LatestActivityOn != new DateOnly(2026, 8, 10)
                        || aggregate.HasActiveExperience || history.Count != 2 || history[0].StartedOn != new DateOnly(2026, 8, 5)
                        || history[1].ProgressEntryCount != 1 || history[1].TotalMinutes != 35)
                    {
                        throw new InvalidOperationException("Aggregate rating calculation failed.");
                    }

                    var disposable = new MediaExperience { WorkId = work.Id, StartedOn = new DateOnly(2026, 8, 20) };
                    await repository.AddExperienceAsync(disposable);
                    await repository.AddProgressEntryAsync(new ProgressEntry
                    {
                        ExperienceId = disposable.Id,
                        LoggedOn = new DateOnly(2026, 8, 20),
                        Metric = "duration",
                        Amount = 10
                    }, work.Id);
                    await repository.DeleteExperienceAsync(disposable.Id, work.Id);
                    var afterDelete = await repository.GetWorkAsync(work.Id);
                    if (afterDelete is null || afterDelete.HasActiveExperience || afterDelete.ExperienceCount != 2 || afterDelete.Status != "completed")
                    {
                        throw new InvalidOperationException("Experience deletion failed.");
                    }

                    var disposableWork = new MediaWork { Title = "delete-me", Kind = "screen" };
                    await repository.AddWorkAsync(disposableWork);
                    var screenExperience = new MediaExperience
                    {
                        WorkId = disposableWork.Id,
                        StartedOn = new DateOnly(2026, 8, 21)
                    };
                    await repository.AddExperienceAsync(screenExperience);
                    await repository.AddProgressEntryAsync(new ProgressEntry
                    {
                        ExperienceId = screenExperience.Id,
                        LoggedOn = new DateOnly(2026, 8, 21),
                        Metric = "episodes",
                        Amount = 2
                    }, disposableWork.Id, totalEpisodes: 12);
                    var screenWork = await repository.GetWorkAsync(disposableWork.Id);
                    var screenHistory = await repository.GetExperiencesAsync(disposableWork.Id);
                    if (screenWork?.TotalEpisodes != 12 || screenHistory.Count != 1
                        || screenHistory[0].TotalEpisodes != 2 || screenHistory[0].AvailableEpisodes != 12
                        || !screenHistory[0].ProgressSummaryLabel.Contains("已看 2 / 12 集", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Episode total progress failed.");
                    }
                    await repository.DeleteWorkAsync(disposableWork.Id);
                    if (await repository.GetWorkAsync(disposableWork.Id) is not null)
                    {
                        throw new InvalidOperationException("Work deletion failed.");
                    }
                }
                finally
                {
                    SqliteConnection.ClearAllPools();
                    var resolvedSmokeDirectory = Path.GetFullPath(smokeDirectory);
                    var resolvedTempDirectory = Path.GetFullPath(Path.GetTempPath());
                    if (resolvedSmokeDirectory.StartsWith(resolvedTempDirectory, StringComparison.OrdinalIgnoreCase)
                        && Path.GetFileName(resolvedSmokeDirectory).StartsWith("QuietShelf-Smoke-", StringComparison.Ordinal))
                    {
                        Directory.Delete(resolvedSmokeDirectory, true);
                    }
                }
                Environment.Exit(0);
            }
            catch (Exception exception)
            {
                var diagnosticDirectory = Environment.GetEnvironmentVariable("QUIETSHELF_DATA_DIR");
                if (!string.IsNullOrWhiteSpace(diagnosticDirectory))
                {
                    Directory.CreateDirectory(diagnosticDirectory);
                    File.WriteAllText(Path.Combine(diagnosticDirectory, "smoke-error.txt"), exception.ToString());
                }
                Environment.Exit(1);
            }

            return;
        }

        if (e.Args.Contains("--ui-smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var database = new Database();
                await database.InitializeAsync();
                var repository = new LibraryRepository(database);
                var mainWindow = new MainWindow { ShowActivated = false };
                mainWindow.VisibleWorks.Add(new MediaWork
                {
                    Title = "render-smoke-test",
                    Kind = "book",
                    Status = "planned"
                });
                mainWindow.Show();
                mainWindow.UpdateLayout();
                if (mainWindow.ActualWidth < 900 || mainWindow.ActualHeight < 600)
                {
                    throw new InvalidOperationException("The main two-pane layout did not render at its minimum usable size.");
                }
                mainWindow.Close();

                var addWork = new AddWorkWindow();
                addWork.ShowActivated = false;
                addWork.Show();
                addWork.UpdateLayout();
                var titleBottom = addWork.TitleBox.TranslatePoint(new Point(0, addWork.TitleBox.ActualHeight), addWork).Y;
                var addButtonTop = addWork.AddButton.TranslatePoint(new Point(0, 0), addWork).Y;
                if (addWork.TitleBox.ActualHeight < 39 || titleBottom >= addButtonTop)
                {
                    throw new InvalidOperationException($"Add-work title input is clipped by the action row: height={addWork.TitleBox.ActualHeight}, bottom={titleBottom}, buttonTop={addButtonTop}.");
                }
                addWork.Close();
                var addFlow = new AddWorkWindow { ShowActivated = false };
                addFlow.Loaded += (_, _) => addFlow.Dispatcher.BeginInvoke(() =>
                {
                    addFlow.TitleBox.Text = "button-smoke-test";
                    addFlow.SubtitleBox.Text = "button-subtitle-smoke-test";
                    if (!addFlow.AddButton.IsEnabled)
                    {
                        throw new InvalidOperationException("Add-work button did not become enabled after entering a title.");
                    }
                    addFlow.AddButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                });
                if (addFlow.ShowDialog() != true || addFlow.Work?.Title != "button-smoke-test"
                    || addFlow.Work.Subtitle != "button-subtitle-smoke-test")
                {
                    throw new InvalidOperationException("Add-work button flow failed.");
                }
                var editWork = new AddWorkWindow(new MediaWork
                {
                    Title = "edit-smoke-test",
                    Subtitle = "existing-subtitle",
                    Kind = "book"
                });
                if (editWork.TitleBox.Text != "edit-smoke-test" || editWork.SubtitleBox.Text != "existing-subtitle"
                    || editWork.KindBox.IsEnabled || editWork.AddButton.Content?.ToString() != "保存")
                {
                    throw new InvalidOperationException("Edit-work metadata form failed.");
                }
                _ = new AddExperienceWindow("smoke-test", "book");
                var progressDialog = new AddProgressWindow("smoke-experience", "screen", new DateOnly(2026, 8, 1));
                progressDialog.MetricBox.SelectedIndex = 1;
                if (progressDialog.AmountBox.PlaceholderText != "例如 2"
                    || progressDialog.EpisodeTotalPanel.Visibility != Visibility.Visible)
                {
                    throw new InvalidOperationException("Episode progress form did not switch its fields and example text.");
                }
                var coversWindow = new ManageCoversWindow(repository, new MediaWork
                {
                    Id = "cover-ui-smoke-test",
                    Title = "cover-ui-smoke-test",
                    Kind = "book"
                }) { ShowActivated = false };
                coversWindow.Show();
                coversWindow.UpdateLayout();
                if (coversWindow.ActualWidth < 680 || coversWindow.ActualHeight < 500)
                {
                    throw new InvalidOperationException("Cover management layout did not render at a usable size.");
                }
                coversWindow.Close();
                _ = new WorkDetailWindow(repository, "smoke-test");
                Environment.Exit(0);
            }
            catch (Exception exception)
            {
                var diagnosticDirectory = Environment.GetEnvironmentVariable("QUIETSHELF_DATA_DIR");
                if (!string.IsNullOrWhiteSpace(diagnosticDirectory))
                {
                    Directory.CreateDirectory(diagnosticDirectory);
                    File.WriteAllText(Path.Combine(diagnosticDirectory, "ui-smoke-error.txt"), exception.ToString());
                }
                Environment.Exit(1);
            }
            return;
        }

        new MainWindow().Show();
    }
}
