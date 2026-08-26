using System.Windows;
using System.Windows.Controls;
using QuietShelf.Models;

namespace QuietShelf;

public partial class AddWorkWindow : Window
{
    public AddWorkWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => TitleBox.Focus();
    }

    public MediaWork? Work { get; private set; }

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        AddButton.IsEnabled = !string.IsNullOrWhiteSpace(TitleBox.Text);
        if (AddButton.IsEnabled)
        {
            TitleError.Visibility = Visibility.Collapsed;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            TitleError.Visibility = Visibility.Visible;
            TitleBox.Focus();
            return;
        }
        var now = DateTimeOffset.Now;
        Work = new MediaWork
        {
            Title = title,
            Kind = ((ComboBoxItem)KindBox.SelectedItem).Tag?.ToString() ?? "book",
            Status = "planned",
            CreatedAt = now,
            UpdatedAt = now
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
