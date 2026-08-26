using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
using QuietShelf.Data;
using QuietShelf.Models;

namespace QuietShelf;

public partial class ManageCoversWindow : Window
{
    private readonly LibraryRepository _repository;
    private readonly MediaWork _work;

    public ManageCoversWindow(LibraryRepository repository, MediaWork work)
    {
        _repository = repository;
        _work = work;
        InitializeComponent();
        DataContext = this;
        Loaded += async (_, _) => await ReloadAsync();
    }

    public ObservableCollection<WorkCover> Covers { get; } = [];

    private async Task ReloadAsync()
    {
        Covers.Clear();
        foreach (var cover in await _repository.GetCoversAsync(_work.Id))
        {
            Covers.Add(cover);
        }
        CoverCountText.Text = Covers.Count == 0 ? "可以用多张封面表示不同版本" : $"共 {Covers.Count} 张 · 第一张作为主封面";
        EmptyState.Visibility = Covers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CoverScroll.Visibility = Covers.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void AddCovers_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择封面图片",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp|JPEG 图片|*.jpg;*.jpeg|PNG 图片|*.png|BMP 图片|*.bmp",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await _repository.AddCoversAsync(_work.Id, dialog.FileNames);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "无法添加封面", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SetPrimary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WorkCover cover })
        {
            return;
        }
        await ExecuteAndReloadAsync(() => _repository.SetPrimaryCoverAsync(_work.Id, cover.Id), "无法设置主封面");
    }

    private async void MoveEarlier_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WorkCover cover })
        {
            return;
        }
        await ExecuteAndReloadAsync(() => _repository.MoveCoverAsync(_work.Id, cover.Id, -1), "无法调整封面顺序");
    }

    private async void MoveLater_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WorkCover cover })
        {
            return;
        }
        await ExecuteAndReloadAsync(() => _repository.MoveCoverAsync(_work.Id, cover.Id, 1), "无法调整封面顺序");
    }

    private async void DeleteCover_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WorkCover cover })
        {
            return;
        }
        var choice = MessageBox.Show(
            $"删除{(cover.IsPrimary ? "当前主封面" : cover.PositionLabel)}？\n\n图片会从 QuietShelf 的本地数据目录移除。",
            "删除封面", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }
        await ExecuteAndReloadAsync(() => _repository.DeleteCoverAsync(_work.Id, cover.Id), "无法删除封面");
    }

    private async Task ExecuteAndReloadAsync(Func<Task> action, string errorTitle)
    {
        try
        {
            await action();
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, errorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
