using System.Windows;
using System.Windows.Controls;
using QuietShelf.Models;

namespace QuietShelf;

public partial class AddExperienceWindow : Window
{
    private readonly string _workId;
    private readonly MediaExperience? _existing;
    private readonly bool _completing;

    public AddExperienceWindow(string workId, string kind, MediaExperience? existing = null, bool completing = false)
    {
        _workId = workId;
        _existing = existing;
        _completing = completing;
        InitializeComponent();
        var editingCompleted = existing?.CompletedOn is not null;
        HeadingText.Text = editingCompleted
            ? (kind == "book" ? "编辑本次阅读" : "编辑本次观看")
            : completing
                ? (kind == "book" ? "完成本次阅读" : "完成本次观看")
                : (kind == "book" ? "开始一次阅读" : "开始一次观看");
        IntroText.Text = editingCompleted
            ? "修改日期、最终评分或总结。"
            : completing ? "完成后可以留下本次评分与总结。" : "先记下开始日期，中途可以随时补记进度。";
        StartDateLabel.Text = kind == "book" ? "开始阅读" : "开始观看";
        EndDateLabel.Text = kind == "book" ? "完成阅读" : "结束观看";
        PopulateRatingBox(AllureBox, RatingScale.AllureMaximum);
        foreach (var box in new[] { ImmersionBox, RationalityBox, IlluminationBox })
        {
            PopulateRatingBox(box, RatingScale.DimensionMaximum);
        }
        StartedOnPicker.SelectedDate = existing?.StartedOn?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
        CompletedOnPicker.SelectedDate = completing
            ? existing?.CompletedOn?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today
            : null;
        NotesBox.Text = existing?.Notes ?? string.Empty;
        SetRating(AllureBox, existing?.Allure);
        SetRating(ImmersionBox, existing?.Immersion);
        SetRating(RationalityBox, existing?.Rationality);
        SetRating(IlluminationBox, existing?.Illumination);
        CompletionPanel.Visibility = completing ? Visibility.Visible : Visibility.Collapsed;
        StartedOnPicker.IsEnabled = !completing || editingCompleted;
        SaveButton.Content = editingCompleted ? "保存修改" : completing ? "完成本次" : "开始记录";
        if (!completing)
        {
            Height = 350;
        }
    }

    public MediaExperience? Experience { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DateOnly? startedOn = StartedOnPicker.SelectedDate is null ? null : DateOnly.FromDateTime(StartedOnPicker.SelectedDate.Value);
        DateOnly? completedOn = !_completing || CompletedOnPicker.SelectedDate is null
            ? null
            : DateOnly.FromDateTime(CompletedOnPicker.SelectedDate.Value);
        if (startedOn is null)
        {
            DateError.Text = "请选择开始日期";
            DateError.Visibility = Visibility.Visible;
            StartedOnPicker.Focus();
            return;
        }
        if (_completing && completedOn is null)
        {
            DateError.Text = "请选择结束日期";
            DateError.Visibility = Visibility.Visible;
            CompletedOnPicker.Focus();
            return;
        }
        if (startedOn is not null && completedOn is not null && completedOn < startedOn)
        {
            DateError.Text = "结束日期不能早于开始日期";
            DateError.Visibility = Visibility.Visible;
            CompletedOnPicker.Focus();
            return;
        }

        var now = DateTimeOffset.Now;
        Experience = new MediaExperience
        {
            Id = _existing?.Id ?? Guid.NewGuid().ToString("N"),
            WorkId = _workId,
            StartedOn = startedOn,
            CompletedOn = completedOn,
            Allure = _completing ? GetRating(AllureBox) : null,
            Immersion = _completing ? GetRating(ImmersionBox) : null,
            Rationality = _completing ? GetRating(RationalityBox) : null,
            Illumination = _completing ? GetRating(IlluminationBox) : null,
            Notes = _completing && !string.IsNullOrWhiteSpace(NotesBox.Text) ? NotesBox.Text.Trim() : null,
            CreatedAt = _existing?.CreatedAt ?? now,
            UpdatedAt = now
        };
        DialogResult = true;
    }

    private void Rating_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RankPreview is null)
        {
            return;
        }
        var values = new[] { GetRating(AllureBox), GetRating(ImmersionBox), GetRating(RationalityBox), GetRating(IlluminationBox) };
        if (values.Any(value => value is null))
        {
            RankPreview.Text = values.All(value => value is null) ? "未评分" : "评分未完成";
            return;
        }
        var rank = RatingScale.Calculate(values[0], values[1], values[2], values[3]);
        RankPreview.Text = $"{rank:0.0} / {RatingScale.RankMaximum:0.0}";
    }

    private static void PopulateRatingBox(ComboBox box, int maximum)
    {
        box.Items.Add(new ComboBoxItem { Content = "不设置", Tag = string.Empty });
        for (var score = RatingScale.Minimum; score <= maximum; score++)
        {
            box.Items.Add(new ComboBoxItem { Content = score.ToString(), Tag = score.ToString() });
        }
        box.SelectedIndex = 0;
    }

    private static int? GetRating(ComboBox box) =>
        box.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var score) ? score : null;

    private static void SetRating(ComboBox box, int? score) => box.SelectedIndex = score ?? 0;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
