using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiteSnap.Core;
using LiteSnap.Core.Models;

namespace LiteSnap.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _selectedFolderPath = string.Empty;

    [ObservableProperty]
    private VersionData? _selectedVersion;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isFolderLoaded;

    [ObservableProperty]
    private bool _hasSelection;

    public ObservableCollection<VersionData> Versions { get; } = [];
    public ObservableCollection<FileVersionObjects> NodeFiles { get; } = [];

    private SnapLiteManager? _manager;

    public MainWindowViewModel()
    {
        OnPropertyChanged(nameof(IsFolderLoaded));
    }

    [RelayCommand]
    private async Task OpenFolder()
    {
        var topLevel = GetTopLevel();
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择包含 .litesnap 目录的文件夹",
            AllowMultiple = false,
        });

        if (folders.Count == 0) return;

        var path = folders[0].Path.LocalPath;
        if (!SnapLiteManager.IsSnapLiteFolder(path))
        {
            StatusMessage = "所选文件夹不包含有效的 .litesnap 数据";
            return;
        }

        await LoadFolder(path);
    }

    private async Task LoadFolder(string path)
    {
        IsLoading = true;
        Versions.Clear();
        NodeFiles.Clear();
        SelectedVersion = null;
        HasSelection = false;
        IsFolderLoaded = false;

        try
        {
            _manager?.Dispose();
            _manager = new SnapLiteManager(path);
            SelectedFolderPath = path;

            var versions = await Task.Run(() => _manager.GetVersions());
            foreach (var v in versions)
                Versions.Add(v);

            IsFolderLoaded = true;
            StatusMessage = $"已加载 {Versions.Count} 个快照版本";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedVersionChanged(VersionData? value)
    {
        NodeFiles.Clear();
        HasSelection = value is not null;
        if (value is null || _manager is null) return;

        try
        {
            var files = _manager.GetNodeObjects(value.NodeId);
            if (files is not null)
                foreach (var f in files.OrderBy(x => x.Path))
                    NodeFiles.Add(f);
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载文件列表失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportZip()
    {
        if (_manager is null || SelectedVersion is null) return;
        var topLevel = GetTopLevel();
        if (topLevel is null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出为 ZIP",
            SuggestedFileName = $"{SelectedVersion.Name}.zip",
            DefaultExtension = "zip",
            FileTypeChoices =
            [
                new FilePickerFileType("ZIP 文件") { Patterns = ["*.zip"] },
            ],
        });
        if (file is null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "正在导出 ZIP...";
            await Task.Run(() => _manager.ExportToZip(SelectedVersion.NodeId, file.Path.LocalPath));
            StatusMessage = $"已导出到: {file.Path.LocalPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportFolder()
    {
        if (_manager is null || SelectedVersion is null) return;
        var topLevel = GetTopLevel();
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择导出目标文件夹",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;

        try
        {
            IsLoading = true;
            StatusMessage = "正在导出...";
            await Task.Run(() => _manager.ExportToFolder(SelectedVersion.NodeId, folders[0].Path.LocalPath));
            StatusMessage = $"已导出到: {folders[0].Path.LocalPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ApplyVersion()
    {
        if (_manager is null || SelectedVersion is null) return;
        var topLevel = GetTopLevel();
        if (topLevel is null) return;

        // 确认对话框
        var dialog = new Window
        {
            Title = "确认操作",
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        try
        {
            IsLoading = true;
            StatusMessage = "正在应用快照...";
            await Task.Run(() => _manager.ApplyVersion(SelectedVersion.NodeId));
            StatusMessage = "快照已应用";
        }
        catch (Exception ex)
        {
            StatusMessage = $"应用快照失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CreateVersion()
    {
        if (_manager is null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "正在创建快照...";
            await Task.Run(() => _manager.CreateVersion());
            StatusMessage = "快照创建成功";

            // 刷新列表
            Versions.Clear();
            var versions = await Task.Run(() => _manager.GetVersions());
            foreach (var v in versions)
                Versions.Add(v);
        }
        catch (Exception ex)
        {
            StatusMessage = $"创建快照失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteVersion()
    {
        if (_manager is null || SelectedVersion is null) return;

        try
        {
            IsLoading = true;
            StatusMessage = "正在删除快照...";
            await Task.Run(() => _manager.DeleteVersion(SelectedVersion.NodeId));
            StatusMessage = "快照已删除";

            Versions.Remove(SelectedVersion);
            SelectedVersion = null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除快照失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static TopLevel? GetTopLevel()
    {
        return Avalonia.Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime lifetime
            ? lifetime.MainWindow
            : null;
    }
}
