using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;

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
                Assert.IsAssignableFrom<FrameworkElement>(mainWindow.FindName("ActiveShelfPanel"));
                Assert.IsAssignableFrom<ItemsControl>(mainWindow.FindName("ActiveShelfList"));
                Assert.IsAssignableFrom<TextBlock>(mainWindow.FindName("LibraryCountText"));
                Assert.IsAssignableFrom<ScrollViewer>(mainWindow.FindName("DashboardScroll"));
                Assert.IsAssignableFrom<ItemsControl>(mainWindow.FindName("DashboardRecentList"));
                Assert.IsAssignableFrom<TextBlock>(mainWindow.FindName("DashboardWorkCountText"));
                Assert.IsAssignableFrom<Button>(mainWindow.FindName("HomeButton"));
                Assert.IsAssignableFrom<FrameworkElement>(mainWindow.FindName("RegularWorksHeader"));
                Assert.IsAssignableFrom<TextBlock>(mainWindow.FindName("DetailKickerText"));
                var addWork = new AddWorkWindow();
                var addExperience = new AddExperienceWindow("ui-test", "book", completing: true);
                var allureBox = Assert.IsType<ComboBox>(addExperience.FindName("AllureBox"));
                Assert.Equal(4, allureBox.Items.Count);
                var completedOn = Assert.IsType<DatePicker>(addExperience.FindName("CompletedOnPicker"));
                var dateError = Assert.IsType<TextBlock>(addExperience.FindName("DateError"));
                var saveButton = Assert.IsAssignableFrom<Button>(addExperience.FindName("SaveButton"));
                completedOn.SelectedDate = null;
                saveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, dateError.Visibility);
                Assert.Null(addExperience.Experience);

                var progress = new AddProgressWindow(
                    "ui-experience",
                    "screen",
                    new DateOnly(2026, 8, 1),
                    totalEpisodes: 12,
                    watchedEpisodes: 2);
                var metricBox = Assert.IsType<ComboBox>(progress.FindName("MetricBox"));
                metricBox.SelectedIndex = 1;
                var episodePanel = Assert.IsAssignableFrom<FrameworkElement>(progress.FindName("EpisodeTotalPanel"));
                Assert.Equal(Visibility.Visible, episodePanel.Visibility);

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
                covers.Close();

                progress.Close();
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
}
