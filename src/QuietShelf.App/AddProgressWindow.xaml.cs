using System.Windows;
using System.Windows.Controls;
using QuietShelf.Models;

namespace QuietShelf;

public partial class AddProgressWindow : Window
{
    private readonly string _experienceId;
    private readonly DateOnly _startedOn;
    private readonly ProgressEntry? _existing;
    private readonly int? _workTotalEpisodes;
    private readonly int _watchedEpisodes;

    public AddProgressWindow(
        string experienceId,
        string kind,
        DateOnly startedOn,
        ProgressEntry? existing = null,
        int? totalEpisodes = null,
        int watchedEpisodes = 0)
    {
        _experienceId = experienceId;
        _startedOn = startedOn;
        _existing = existing;
        _workTotalEpisodes = totalEpisodes;
        _watchedEpisodes = watchedEpisodes;
        InitializeComponent();
        HeadingText.Text = existing is not null
            ? "编辑中途记录"
            : kind == "book" ? "记录阅读进度" : "记录观看进度";
        if (kind == "book")
        {
            EpisodesOption.Visibility = Visibility.Collapsed;
        }
        LoggedOnPicker.SelectedDate = existing?.LoggedOn.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
        MetricBox.SelectedIndex = existing?.Metric == "episodes" ? 1 : 0;
        AmountBox.Text = existing?.Amount.ToString() ?? string.Empty;
        TotalEpisodesBox.Text = totalEpisodes?.ToString() ?? string.Empty;
        NotesBox.Text = existing?.Notes ?? string.Empty;
        SaveButton.Content = existing is null ? "保存" : "保存修改";
        Loaded += (_, _) => AmountBox.Focus();
    }

    public ProgressEntry? Entry { get; private set; }
    public int? TotalEpisodes { get; private set; }

    private void Metric_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AmountLabel is null || MetricBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        var isEpisodes = item.Tag?.ToString() == "episodes";
        AmountLabel.Text = isEpisodes ? "本次观看（集）" : "本次记录（分钟）";
        AmountBox.PlaceholderText = isEpisodes ? "例如 2" : "例如 30";
        EpisodeTotalPanel.Visibility = isEpisodes ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        InputError.Visibility = Visibility.Collapsed;
        if (LoggedOnPicker.SelectedDate is null)
        {
            ShowError("请选择记录日期");
            LoggedOnPicker.Focus();
            return;
        }
        var loggedOn = DateOnly.FromDateTime(LoggedOnPicker.SelectedDate.Value);
        if (loggedOn < _startedOn)
        {
            ShowError("记录日期不能早于本次开始日期");
            LoggedOnPicker.Focus();
            return;
        }
        if (!int.TryParse(AmountBox.Text.Trim(), out var amount) || amount <= 0)
        {
            ShowError("请输入大于 0 的整数");
            AmountBox.Focus();
            return;
        }

        var metric = ((ComboBoxItem)MetricBox.SelectedItem).Tag?.ToString() ?? "duration";
        if (metric == "episodes")
        {
            if (!string.IsNullOrWhiteSpace(TotalEpisodesBox.Text))
            {
                if (!int.TryParse(TotalEpisodesBox.Text.Trim(), out var totalEpisodes) || totalEpisodes <= 0)
                {
                    ShowError("总集数应为大于 0 的整数，或者留空");
                    TotalEpisodesBox.Focus();
                    return;
                }

                var existingAmount = _existing?.Metric == "episodes" ? _existing.Amount : 0;
                var resultingWatchedEpisodes = _watchedEpisodes - existingAmount + amount;
                if (totalEpisodes < resultingWatchedEpisodes)
                {
                    ShowError($"总集数不能小于已记录的 {resultingWatchedEpisodes} 集");
                    TotalEpisodesBox.Focus();
                    return;
                }
                TotalEpisodes = totalEpisodes;
            }
        }
        else
        {
            TotalEpisodes = _workTotalEpisodes;
        }

        var now = DateTimeOffset.Now;
        Entry = new ProgressEntry
        {
            Id = _existing?.Id ?? Guid.NewGuid().ToString("N"),
            ExperienceId = _experienceId,
            LoggedOn = loggedOn,
            Metric = metric,
            Amount = amount,
            Notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
            CreatedAt = _existing?.CreatedAt ?? now,
            UpdatedAt = now
        };
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        InputError.Text = message;
        InputError.Visibility = Visibility.Visible;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
