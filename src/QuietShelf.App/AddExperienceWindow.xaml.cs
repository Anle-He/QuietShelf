using System.Windows;
using System.Windows.Controls;
using QuietShelf.Models;

namespace QuietShelf;

public partial class AddExperienceWindow : Window
{
    private readonly string _workId;
    private readonly string _kind;
    private readonly MediaExperience? _existing;

    public AddExperienceWindow(string workId, string kind, MediaExperience? existing = null)
    {
        _workId = workId;
        _kind = kind;
        _existing = existing;
        InitializeComponent();
        var editingCompleted = existing?.CompletedOn is not null;
        HeadingText.Text = editingCompleted
            ? (kind == "book" ? "编辑本次阅读" : "编辑本次观看")
            : (kind == "book" ? "记录读完的一本书" : "记录看完的一部影视");
        IntroText.Text = editingCompleted
            ? "修改日期、最终评分或总结。"
            : "只收录已经完成的这一次，评分与总结可以稍后补充。";
        EndDateLabel.Text = kind == "book" ? "读完日期" : "看完日期";
        PopulateRatingBox(AllureBox, RatingScale.AllureMaximum);
        foreach (var box in new[] { ImmersionBox, RationalityBox, IlluminationBox })
        {
            PopulateRatingBox(box, RatingScale.DimensionMaximum);
        }
        CompletedOnPicker.SelectedDate = existing?.CompletedOn?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
        NotesBox.Text = existing?.Notes ?? string.Empty;
        SetRating(AllureBox, existing?.Allure);
        SetRating(ImmersionBox, existing?.Immersion);
        SetRating(RationalityBox, existing?.Rationality);
        SetRating(IlluminationBox, existing?.Illumination);
        CompletionPanel.Visibility = Visibility.Visible;
        SaveButton.Content = editingCompleted ? "保存修改" : "保存完成记录";
        DeleteButton.Visibility = editingCompleted ? Visibility.Visible : Visibility.Collapsed;
    }

    public MediaExperience? Experience { get; private set; }
    public bool DeleteRequested { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DateOnly? startedOn = _existing?.StartedOn;
        DateOnly? completedOn = CompletedOnPicker.SelectedDate is null
            ? null
            : DateOnly.FromDateTime(CompletedOnPicker.SelectedDate.Value);
        if (completedOn is null)
        {
            DateError.Text = "请选择完成日期";
            DateError.Visibility = Visibility.Visible;
            CompletedOnPicker.Focus();
            return;
        }
        if (startedOn is not null && completedOn is not null && completedOn < startedOn)
        {
            DateError.Text = "完成日期不能早于原有开始日期";
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
            Allure = GetRating(AllureBox),
            Immersion = GetRating(ImmersionBox),
            Rationality = GetRating(RationalityBox),
            Illumination = GetRating(IlluminationBox),
            Notes = !string.IsNullOrWhiteSpace(NotesBox.Text) ? NotesBox.Text.Trim() : null,
            CreatedAt = _existing?.CreatedAt ?? now,
            UpdatedAt = now
        };
        DialogResult = true;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_existing is null)
        {
            return;
        }

        var progressCount = _existing.ProgressEntryCount;
        var detail = progressCount == 0 ? string.Empty : $"\n其中的 {progressCount} 条历史记录也会一起删除。";
        var choice = MessageBox.Show(
            this,
            $"删除这次{(_kind == "book" ? "阅读" : "观看")}记录？{detail}\n\n完成次数和综合评分会自动重新计算。此操作无法撤销。",
            "删除完成记录",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        DeleteRequested = true;
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
