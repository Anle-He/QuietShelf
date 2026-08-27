using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;

namespace QuietShelf.Tests;

public sealed class UiSmokeTests
{
    [Fact]
    [Trait("Category", "Manual")]
    public void CoreWindowsCanBeConstructedOnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = new Application();
                var addWork = new AddWorkWindow();
                var addExperience = new AddExperienceWindow("ui-test", "book", completing: true);
                var allureBox = Assert.IsType<ComboBox>(addExperience.FindName("AllureBox"));
                Assert.Equal(4, allureBox.Items.Count);
                addExperience.Close();
                addWork.Close();
                Application.Current.Shutdown();
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
