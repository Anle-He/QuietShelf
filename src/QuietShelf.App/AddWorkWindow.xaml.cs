using System.Windows;
using System.Windows.Controls;
using QuietShelf.Models;

namespace QuietShelf;

public partial class AddWorkWindow : Window
{
    private readonly MediaWork? _existingWork;

    public AddWorkWindow(MediaWork? existingWork = null)
    {
        _existingWork = existingWork;
        InitializeComponent();
        if (existingWork is not null)
        {
            Title = "编辑作品信息";
            HeadingText.Text = "编辑作品信息";
            DescriptionText.Text = "修改作品的标题、副标题和作者。";
            AddButton.Content = "保存";
            TitleBox.Text = existingWork.Title;
            SubtitleBox.Text = existingWork.Subtitle ?? string.Empty;
            AuthorBox.Text = existingWork.Author ?? string.Empty;
            KindBox.SelectedIndex = existingWork.Kind == "screen" ? 1 : 0;
            KindBox.IsEnabled = false;
        }
        Loaded += (_, _) => TitleBox.Focus();
    }

    public MediaWork? Work { get; private set; }

    private void KindBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AuthorPanel is null)
        {
            return;
        }

        var kind = (KindBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        AuthorPanel.Visibility = kind == "book" ? Visibility.Visible : Visibility.Collapsed;
    }

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
        var kind = ((ComboBoxItem)KindBox.SelectedItem).Tag?.ToString() ?? "book";
        Work = new MediaWork
        {
            Id = _existingWork?.Id ?? Guid.NewGuid().ToString("N"),
            Title = title,
            Subtitle = string.IsNullOrWhiteSpace(SubtitleBox.Text) ? null : SubtitleBox.Text.Trim(),
            Author = kind == "book" && !string.IsNullOrWhiteSpace(AuthorBox.Text) ? AuthorBox.Text.Trim() : null,
            Kind = kind,
            Status = _existingWork?.Status,
            TotalEpisodes = _existingWork?.TotalEpisodes,
            CreatedAt = _existingWork?.CreatedAt ?? now,
            UpdatedAt = now
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
