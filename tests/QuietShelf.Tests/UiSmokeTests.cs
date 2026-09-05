using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuietShelf.Models;

namespace QuietShelf.Tests;

public sealed class UiSmokeTests
{
    [Fact]
    [Trait("Category", "Manual")]
    public async Task CoreWindowsCanBeConstructedOnStaThread()
    {
        await using var context = await TempDatabase.CreateAsync();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new QuietShelf.App();
                application.InitializeComponent();
                var mainWindow = new MainWindow();
                Assert.IsAssignableFrom<TextBlock>(mainWindow.FindName("LibraryCountText"));
                Assert.IsAssignableFrom<ScrollViewer>(mainWindow.FindName("DashboardScroll"));
                Assert.IsAssignableFrom<FrameworkElement>(mainWindow.FindName("DashboardTimelineList"));
                Assert.IsAssignableFrom<TextBlock>(mainWindow.FindName("DashboardWorkCountText"));
                Assert.IsAssignableFrom<Button>(mainWindow.FindName("HomeButton"));
                Assert.IsAssignableFrom<FrameworkElement>(mainWindow.FindName("RegularWorksHeader"));
                Assert.IsAssignableFrom<TextBlock>(mainWindow.FindName("DetailKickerText"));
                Assert.IsType<StackPanel>(mainWindow.FindName("DashboardShowcasePanel"));
                Assert.IsType<Border>(mainWindow.FindName("DetailHeroShell"));
                AssertExperienceRatingTemplate(mainWindow);
                AssertTimelineTemplate(mainWindow);
                AssertDashboardLayout(mainWindow);
                var addWork = new AddWorkWindow();
                Assert.IsType<Border>(addWork.FindName("WorkFormSection"));
                var addExperience = new AddExperienceWindow("ui-test", "book");
                Assert.IsType<Border>(addExperience.FindName("ExperienceDateSection"));
                Assert.IsType<Border>(addExperience.FindName("RatingSection"));
                var allureBox = Assert.IsType<ComboBox>(addExperience.FindName("AllureBox"));
                Assert.Equal(4, allureBox.Items.Count);
                var completedOn = Assert.IsType<DatePicker>(addExperience.FindName("CompletedOnPicker"));
                Assert.NotNull(completedOn.SelectedDate);
                var dateError = Assert.IsType<TextBlock>(addExperience.FindName("DateError"));
                var saveButton = Assert.IsAssignableFrom<Button>(addExperience.FindName("SaveButton"));
                var deleteButton = Assert.IsAssignableFrom<Button>(addExperience.FindName("DeleteButton"));
                Assert.Equal(Visibility.Collapsed, deleteButton.Visibility);
                completedOn.SelectedDate = null;
                saveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, dateError.Visibility);
                Assert.Null(addExperience.Experience);

                var editExperience = new AddExperienceWindow("ui-test", "book", new MediaExperience
                {
                    WorkId = "ui-test",
                    CompletedOn = new DateOnly(2026, 8, 29)
                });
                Assert.Equal(Visibility.Visible, Assert.IsAssignableFrom<Button>(editExperience.FindName("DeleteButton")).Visibility);
                editExperience.Close();

                var covers = new ManageCoversWindow(context.Repository, new QuietShelf.Models.MediaWork
                {
                    Id = "ui-cover-work",
                    Title = "ui-cover-test",
                    Kind = "book"
                });
                covers.Show();
                covers.UpdateLayout();
                Assert.True(covers.ActualWidth >= 720);
                Assert.True(covers.ActualHeight >= 540);
                SaveSnapshot(covers, "cover-gallery.png");
                covers.Close();

                SnapshotWindow(addWork, "add-work.png");
                SnapshotWindow(addExperience, "add-experience.png");

                addExperience.Close();
                addWork.Close();
                mainWindow.Close();
                application.Shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void AssertDashboardLayout(MainWindow window)
    {
        for (var index = 1; index <= 3; index++)
        {
            var work = new DashboardShowcaseItem
            {
                WorkId = index.ToString(), Title = $"收藏作品 {index}", Author = "示例作者",
                AggregateRank = 3.5, RatingCount = 2, CompletionCount = 2,
                FirstCompletedOn = new DateOnly(2026, 1, index), LatestCompletedOn = new DateOnly(2026, 8, index)
            };
            window.DashboardTopBooks.Add(work);
            if (index == 1) window.DashboardTopScreens.Add(work);
            window.DashboardRecentWorks.Add(work);
            window.DashboardTopAuthors.Add(new DashboardAuthorRank
            {
                Position = index, Author = $"作者 {index}", WorkCount = 2, RatingCount = 3, WeightedRank = 3.5
            });
        }
        var panel = (StackPanel)window.FindName("DashboardShowcasePanel");
        ((Button)window.FindName("DashboardFirstBookCard")).DataContext = window.DashboardTopBooks[0];
        ((TextBlock)window.FindName("DashboardFirstBookEmpty")).Visibility = Visibility.Collapsed;
        foreach (var width in new[] { 850d, 600d })
        {
            panel.Measure(new Size(width, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
            panel.UpdateLayout();
            panel.Measure(new Size(width, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
            panel.UpdateLayout();
            foreach (var wrap in Descendants(panel).OfType<WrapPanel>())
            {
                foreach (FrameworkElement child in wrap.Children)
                {
                    var origin = child.TranslatePoint(new Point(), wrap);
                    Assert.True(origin.X + child.ActualWidth <= wrap.ActualWidth + 0.5);
                }
            }
            Assert.True(((Border)window.FindName("DashboardScreenRanking")).ActualHeight <
                        ((Border)window.FindName("DashboardBookRanking")).ActualHeight);
            var firstBookSection = (Border)window.FindName("DashboardFirstBookSection");
            Assert.Equal(width < 740 ? 1 : 0, Grid.GetRow(firstBookSection));
            foreach (var list in Descendants(panel).OfType<ItemsControl>()
                         .Where(list => ReferenceEquals(list.ItemTemplate, window.Resources["ShowcaseRankItemTemplate"])))
            {
                Assert.All(Descendants(list).OfType<Button>(), button =>
                    Assert.True(button.ActualWidth >= list.ActualWidth - 1, "Rank rows should fill the list width."));
            }
            Assert.Equal(width < 780 ? 1 : 0, Grid.GetRow((Border)window.FindName("DashboardAuthorRanking")));
            SaveSnapshot(panel, $"dashboard-showcase-{width:0}.png");
        }
    }

    private static void SnapshotWindow(Window window, string fileName)
    {
        window.Show();
        window.UpdateLayout();
        SaveSnapshot(window, fileName);
        window.Hide();
    }

    private static void AssertTimelineTemplate(MainWindow mainWindow)
    {
        var timeline = Assert.IsType<ItemsControl>(mainWindow.FindName("DashboardTimelineList"));
        var presenter = new ContentPresenter
        {
            ContentTemplate = timeline.ItemTemplate,
            Resources = mainWindow.Resources,
            Width = 740,
            Content = new DashboardTimelineDay
            {
                Date = new DateOnly(2026, 8, 28),
                Items = new[] { "completion" }.Select(type => new DashboardTimelineItem
                {
                    Id = type, WorkId = "timeline-test", Title = "一本正在读的书", Kind = "book",
                    EventType = type, Metric = "duration", Amount = 30,
                    Notes = type == "completion" ? "合上书之后，仍然记得这一段旅程。" : null
                }).ToArray()
            }
        };
        presenter.Measure(new Size(presenter.Width, double.PositiveInfinity));
        presenter.Arrange(new Rect(presenter.DesiredSize));
        presenter.UpdateLayout();
        var buttons = Descendants(presenter).OfType<Button>().ToArray();
        Assert.Single(buttons);
        Assert.All(buttons, button =>
        {
            Assert.IsType<DashboardTimelineItem>(button.Tag);
            Assert.True(button.ActualWidth > 500, "Timeline actions should fill the date card.");
            Assert.True(button.Focusable);
        });
        SaveSnapshot(presenter, "timeline-day.png");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void AssertExperienceRatingTemplate(MainWindow mainWindow)
    {
        var history = Assert.IsType<ListBox>(mainWindow.FindName("HistoryList"));
        var presenter = new ContentPresenter
        {
            ContentTemplate = history.ItemTemplate,
            Resources = mainWindow.Resources,
            Width = 880
        };

        void Display(MediaExperience experience)
        {
            presenter.Content = new ExperienceArchiveCard { ArchiveNumber = 1, Experience = experience };
            presenter.Measure(new Size(presenter.Width, double.PositiveInfinity));
            presenter.Arrange(new Rect(presenter.DesiredSize));
            presenter.UpdateLayout();
        }

        Grid Scores() => Assert.IsType<Grid>(history.ItemTemplate.FindName("DimensionScores", presenter));
        TextBlock IncompleteMessage() => Assert.IsType<TextBlock>(history.ItemTemplate.FindName("IncompleteRating", presenter));
        string[] Values() => Scores().Children.OfType<StackPanel>()
            .SelectMany(panel => panel.Children.OfType<TextBlock>())
            .Select(text => new TextRange(text.ContentStart, text.ContentEnd).Text).ToArray();

        Display(new MediaExperience
        {
            WorkId = "ui-rating",
            StartedOn = new DateOnly(2026, 8, 26),
            CompletedOn = new DateOnly(2026, 8, 28),
            ProgressEntryCount = 3,
            Allure = 3,
            Immersion = 5,
            Rationality = 4,
            Illumination = 4
        });
        Assert.Equal(Visibility.Visible, Scores().Visibility);
        Assert.Equal(Visibility.Collapsed, IncompleteMessage().Visibility);
        Assert.Equal(["Allure", "3 / 3", "Immersion", "5 / 5", "Rationality", "4 / 5", "Illumination", "4 / 5"], Values());
        SaveSnapshot(presenter, "experience-ratings.png");

        presenter.Width = 650;
        presenter.Measure(new Size(presenter.Width, double.PositiveInfinity));
        presenter.Arrange(new Rect(presenter.DesiredSize));
        presenter.UpdateLayout();
        Assert.True(presenter.DesiredSize.Width <= presenter.Width + 0.5, "Archive cards should fit the available detail width.");
        SaveSnapshot(presenter, "experience-ratings-narrow.png");

        Display(new MediaExperience { WorkId = "ui-rating", Allure = 3 });
        Assert.Equal(Visibility.Collapsed, Scores().Visibility);
        Assert.Equal(Visibility.Visible, IncompleteMessage().Visibility);

        Display(new MediaExperience { WorkId = "ui-rating", Allure = 1, Immersion = 2, Rationality = 3, Illumination = 4 });
        Assert.Equal(Visibility.Visible, Scores().Visibility);
        Assert.Equal(Visibility.Collapsed, IncompleteMessage().Visibility);
        Assert.Equal(["Allure", "1 / 3", "Immersion", "2 / 5", "Rationality", "3 / 5", "Illumination", "4 / 5"], Values());
    }

    private static void SaveSnapshot(FrameworkElement element, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("QUIETSHELF_UI_SNAPSHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(element.ActualWidth * 2),
            (int)Math.Ceiling(element.ActualHeight * 2),
            192, 192, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, fileName));
        encoder.Save(stream);
    }
}
