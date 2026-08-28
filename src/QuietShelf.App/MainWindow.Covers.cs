using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using QuietShelf.Converters;
using QuietShelf.Models;

namespace QuietShelf;

public partial class MainWindow
{
    private void RenderCoverStack(IReadOnlyList<WorkCover> covers)
    {
        DetailCoverCanvas.Children.Clear();
        if (_selectedWork is null || covers.Count == 0)
        {
            var placeholder = new Border
            {
                Width = 136,
                Height = 204,
                CornerRadius = new CornerRadius(9),
                Background = (Brush)FindResource("AccentSoftBrush"),
                BorderBrush = (Brush)FindResource("DividerBrush"),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = _selectedWork?.KindGlyph ?? "书",
                    FontSize = 25,
                    FontWeight = FontWeights.DemiBold,
                    Foreground = (Brush)FindResource("AccentBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Canvas.SetLeft(placeholder, 8);
            Canvas.SetTop(placeholder, 5);
            DetailCoverCanvas.Children.Add(placeholder);
            AddCoverBadge("+");
            return;
        }

        var visible = covers.Take(3).Reverse().ToList();
        foreach (var cover in visible)
        {
            var position = Math.Min(cover.SortOrder, 2);
            var (left, top, angle) = position switch
            {
                0 => (8d, 5d, 0d),
                1 => (11d, 5d, 2.5d),
                _ => (5d, 5d, -2.5d)
            };
            var image = new Image
            {
                Source = new CoverImageConverter().Convert(
                    cover.FilePath,
                    typeof(ImageSource),
                    216,
                    CultureInfo.InvariantCulture) as ImageSource,
                Stretch = Stretch.UniformToFill
            };
            var card = new Border
            {
                Width = 136,
                Height = 204,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(232, 236, 233)),
                BorderBrush = (Brush)FindResource("DividerBrush"),
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(angle),
                Effect = new DropShadowEffect { BlurRadius = 9, ShadowDepth = 2, Opacity = 0.16 },
                Child = image
            };
            Canvas.SetLeft(card, left);
            Canvas.SetTop(card, top);
            DetailCoverCanvas.Children.Add(card);
        }
        if (covers.Count > 1)
        {
            AddCoverBadge(covers.Count.ToString());
        }
    }

    private void AddCoverBadge(string text)
    {
        var badge = new Border
        {
            MinWidth = 25,
            Height = 25,
            Padding = new Thickness(6, 0, 6, 0),
            CornerRadius = new CornerRadius(13),
            Background = (Brush)FindResource("AccentBrush"),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.DemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Canvas.SetRight(badge, 0);
        Canvas.SetBottom(badge, 1);
        DetailCoverCanvas.Children.Add(badge);
    }
}
