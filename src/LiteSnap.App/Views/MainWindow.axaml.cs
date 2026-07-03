using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using LiteSnap.App.ViewModels;

namespace LiteSnap.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.HasRecentFolders)
                || args.PropertyName == nameof(MainWindowViewModel.RecentFolders))
                RebuildRecentMenu(vm);
        };
        RebuildRecentMenu(vm);
    }

    private void RebuildRecentMenu(MainWindowViewModel vm)
    {
        if (OpenMenuButton.Flyout is not MenuFlyout flyout) return;

        // Clear all but first item (📂 打开文件夹)
        while (flyout.Items.Count > 1)
            flyout.Items.RemoveAt(flyout.Items.Count - 1);

        if (vm.RecentFolders.Count == 0) return;

        flyout.Items.Add(new Separator());

        foreach (var path in vm.RecentFolders)
        {
            var name = System.IO.Path.GetDirectoryName(path) ?? path;
            var item = new MenuItem
            {
                Command = vm.OpenRecentFolderCommand,
                CommandParameter = path,
            };
            var icon = new Path
            {
                Data = (Geometry?)Application.Current?.FindResource("IconFolder"),
                Fill = Brushes.White,
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
            };
            item.Icon = icon;
            item.Header = new TextBlock { Text = name };
            ToolTip.SetTip(item, path);
            flyout.Items.Add(item);
        }
    }
}
