using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
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

    [ObservableProperty]
    private int _fileCount;

    [ObservableProperty]
    private int _folderCount;

    [ObservableProperty]
    private string _totalSizeFormatted = string.Empty;

    public ObservableCollection<VersionData> Versions { get; } = [];
    public ObservableCollection<FileVersionObjects> NodeFiles { get; } = [];
    public ObservableCollection<FileTreeNode> FileTreeRoot { get; } = [];
    public ObservableCollection<string> RecentFolders { get; } = [];

    private SnapLiteManager? _manager;

    public MainWindowViewModel()
    {
        OnPropertyChanged(nameof(IsFolderLoaded));
        LoadHistory();
    }

    public bool HasRecentFolders => RecentFolders.Count > 0;

    private static string HistoryPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LiteSnap", "history.json");

    private void LoadHistory()
    {
        try
        {
            var path = HistoryPath;
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<string>>(json);
            if (list is null) return;
            RecentFolders.Clear();
            foreach (var p in list
                .Select(static x => Path.GetFullPath(x))
                .Where(static x => Directory.Exists(x)))
                RecentFolders.Add(p);
        }
        catch { }
        OnPropertyChanged(nameof(HasRecentFolders));
    }

    private void SaveHistory()
    {
        try
        {
            var dir = Path.GetDirectoryName(HistoryPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(RecentFolders.Take(8).ToList());
            File.WriteAllText(HistoryPath, json);
        }
        catch { }
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
        FileTreeRoot.Clear();
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

            RecentFolders.Remove(path);
            RecentFolders.Insert(0, path);
            if (RecentFolders.Count > 8)
                RecentFolders.RemoveAt(RecentFolders.Count - 1);
            OnPropertyChanged(nameof(HasRecentFolders));
            SaveHistory();
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

    [RelayCommand]
    private async Task OpenRecentFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!SnapLiteManager.IsSnapLiteFolder(path))
        {
            StatusMessage = "所选文件夹不包含有效的 .litesnap 数据";
            RecentFolders.Remove(path);
            OnPropertyChanged(nameof(HasRecentFolders));
            SaveHistory();
            return;
        }
        await LoadFolder(path);
    }

    partial void OnSelectedVersionChanged(VersionData? value)
    {
        NodeFiles.Clear();
        FileTreeRoot.Clear();
        HasSelection = value is not null;
        FileCount = 0;
        FolderCount = 0;
        TotalSizeFormatted = string.Empty;

        if (value is null || _manager is null) return;

        try
        {
            var files = _manager.GetNodeObjects(value.NodeId);
            if (files is null) return;

            foreach (var f in files.OrderBy(x => x.Path))
                NodeFiles.Add(f);

            FileCount = files.Count(x => x.ObjectType == ObjectType.File);
            FolderCount = files.Count(x => x.ObjectType == ObjectType.Directory);
            var totalBytes = files.Where(x => x.ObjectType == ObjectType.File).Sum(x => x.Length);
            TotalSizeFormatted = FormatSize(totalBytes);

            // Build tree
            var dirs = new Dictionary<string, FileTreeNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files.OrderBy(x => x.Path))
            {
                var segments = f.Path.Split('/', '\\');
                var currentPath = "";

                for (int i = 0; i < segments.Length - 1; i++)
                {
                    var parentPath = currentPath;
                    currentPath = currentPath.Length == 0 ? segments[i] : currentPath + "/" + segments[i];
                    if (!dirs.ContainsKey(currentPath))
                    {
                        var node = new FileTreeNode
                        {
                            Name = segments[i],
                            FullPath = currentPath,
                            IsDirectory = true,
                        };
                        dirs[currentPath] = node;
                        if (parentPath.Length == 0)
                            FileTreeRoot.Add(node);
                        else if (dirs.TryGetValue(parentPath, out var parent))
                            parent.Children.Add(node);
                    }
                }

                var node2 = new FileTreeNode
                {
                    Name = segments[^1],
                    FullPath = f.Path,
                    IsDirectory = f.ObjectType == ObjectType.Directory,
                    Length = f.Length,
                };
                if (segments.Length == 1)
                    FileTreeRoot.Add(node2);
                else
                {
                    var parentDir = string.Join("/", segments[..^1]);
                    if (dirs.TryGetValue(parentDir, out var parent2))
                        parent2.Children.Add(node2);
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载文件列表失败: {ex.Message}";
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
    };

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

        var now = DateTime.Now;
        var nameBox = new TextBox
        {
            PlaceholderText = now.ToString("yyyy/MM/dd-HH:mm:ss"),
        };
        var descBox = new TextBox
        {
            PlaceholderText = "由 LiteSnap 创建的备份",
            AcceptsReturn = true,
            MaxHeight = 80,
        };

        var dialog = new FAContentDialog
        {
            Title = "创建快照",
            PrimaryButtonText = "创建",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "名称", FontSize = 12 },
                    nameBox,
                    new TextBlock { Text = "描述", FontSize = 12 },
                    descBox,
                },
            },
        };

        if (await dialog.ShowAsync() != FAContentDialogResult.Primary)
            return;

        try
        {
            IsLoading = true;
            StatusMessage = "正在创建快照...";
            var name = string.IsNullOrWhiteSpace(nameBox.Text)
                ? now.ToString("yyyy/MM/dd-HH:mm:ss")
                : nameBox.Text.Trim();
            var desc = string.IsNullOrWhiteSpace(descBox.Text)
                ? "由 LiteSnap 创建的备份"
                : descBox.Text.Trim();
            await Task.Run(() => _manager.CreateVersion(name, desc));
            StatusMessage = "快照创建成功";

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

        var dialog = new FAContentDialog
        {
            Title = "确认删除",
            Content = $"确定要删除快照 \"{SelectedVersion.Name}\" 吗？此操作不可撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Close,
            IsPrimaryButtonEnabled = true,
        };

        if (await dialog.ShowAsync() != FAContentDialogResult.Primary)
            return;

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

    private static readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("https://api.github.com/"),
        DefaultRequestHeaders = { { "User-Agent", "LiteSnap" } },
    };

    private static string CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is not null ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
        }
    }

    [RelayCommand]
    private async Task SoftwareInfo()
    {
        var ver = CurrentVersion;
        await new FAContentDialog
        {
            Title = "软件信息",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "LiteSnap", FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    new TextBlock { Text = $"版本 v{ver}", FontSize = 13 },
                    new TextBlock { Text = "快照备份管理器 —— 为 Minecraft 存档及任意文件夹提供文件级快照备份。", FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 380 },
                    new TextBlock { Text = "基于 SHA-512 内容寻址存储 + LiteDB 索引。", FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 380 },
                },
            },
            PrimaryButtonText = "确定",
            DefaultButton = FAContentDialogButton.Primary,
        }.ShowAsync();
    }

    [RelayCommand]
    private async Task CheckUpdates()
    {
        var spinner = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 4,
            Margin = new Avalonia.Thickness(0, 4),
        };
        var statusText = new TextBlock { Text = "正在检查最新版本...", FontSize = 12 };
        var resultPanel = new StackPanel { Spacing = 10, Children = { spinner, statusText } };

        var dialog = new FAContentDialog
        {
            Title = "检查更新",
            Content = resultPanel,
            PrimaryButtonText = "确定",
            DefaultButton = FAContentDialogButton.Primary,
        };

        var showTask = dialog.ShowAsync();

        try
        {
            var latest = await FetchLatestVersionAsync();
            var cur = CurrentVersion;

            if (latest is null)
            {
                resultPanel.Children[1] = new TextBlock { Text = "检查失败，无法连接到 GitHub。", FontSize = 12 };
            }
            else if (string.Compare(latest, cur, StringComparison.OrdinalIgnoreCase) > 0)
            {
                resultPanel.Children.Clear();
                resultPanel.Children.Add(new TextBlock { Text = $"发现新版本：v{latest}", FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold });
                resultPanel.Children.Add(new TextBlock { Text = $"当前版本：v{cur}", FontSize = 12, Foreground = Avalonia.Media.Brushes.Gray });
                resultPanel.Spacing = 6;
                dialog.PrimaryButtonText = "跳转下载";
                dialog.CloseButtonText = "取消";
            }
            else
            {
                resultPanel.Children.Clear();
                resultPanel.Children.Add(new TextBlock { Text = "已是最新版本。", FontSize = 14 });
                resultPanel.Children.Add(new TextBlock { Text = $"v{cur}", FontSize = 12, Foreground = Avalonia.Media.Brushes.Gray });
            }
        }
        catch
        {
            resultPanel.Children[1] = new TextBlock { Text = "检查失败，请检查网络连接。", FontSize = 12 };
        }

        if (await showTask == FAContentDialogResult.Primary && dialog.PrimaryButtonText == "跳转下载")
            await OpenUrl("https://github.com/PCL-Community/LiteSnap/releases");
    }

    [RelayCommand]
    private async Task OpenLicense()
    {
        await OpenUrl("https://github.com/PCL-Community/LiteSnap/blob/main/LICENSE");
    }

    private static async Task<string?> FetchLatestVersionAsync()
    {
        var response = await _httpClient.GetAsync("repos/PCL-Community/LiteSnap/releases/latest");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v');
    }

    private static async Task OpenUrl(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", url);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", url);
        }
        catch { }
        await Task.CompletedTask;
    }

    private static TopLevel? GetTopLevel()
    {
        return Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime
            ? lifetime.MainWindow : null;
    }
}

public class FileTreeNode
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Length { get; set; }
    public ObservableCollection<FileTreeNode> Children { get; set; } = [];
    public bool IsExpanded { get; set; } = true;
}
