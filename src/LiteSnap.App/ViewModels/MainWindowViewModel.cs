using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using LiteSnap.App.Services;
using LiteSnap.Core;
using LiteSnap.Core.Models;

namespace LiteSnap.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private static LanguageManager Lang => LanguageManager.Instance;

    [ObservableProperty]
    private string _selectedFolderPath = string.Empty;

    [ObservableProperty]
    private VersionData? _selectedVersion;

    [ObservableProperty]
    private string _statusMessage = "";

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
    public ObservableCollection<FileTreeNode> FileTreeRoot { get; } = [];
    public ObservableCollection<string> RecentFolders { get; } = [];

    private SnapLiteManager? _manager;

    public MainWindowViewModel()
    {
        _statusMessage = Lang["Status.Ready"];
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
            Title = Lang["FolderPicker.Title"],
            AllowMultiple = false,
        });

        if (folders.Count == 0) return;

        var path = folders[0].Path.LocalPath;
        if (!SnapLiteManager.IsSnapLiteFolder(path))
        {
            StatusMessage = Lang["FolderPicker.Invalid"];
            return;
        }

        await LoadFolder(path);
    }

    private async Task LoadFolder(string path)
    {
        IsLoading = true;
        Versions.Clear();
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
            StatusMessage = Lang.Format("FolderPicker.Loaded", Versions.Count);

            RecentFolders.Remove(path);
            RecentFolders.Insert(0, path);
            if (RecentFolders.Count > 8)
                RecentFolders.RemoveAt(RecentFolders.Count - 1);
            OnPropertyChanged(nameof(HasRecentFolders));
            SaveHistory();
        }
        catch (Exception ex)
        {
            StatusMessage = Lang.Format("FolderPicker.LoadFailed", ex.Message);
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
            StatusMessage = Lang["FolderPicker.Invalid"];
            RecentFolders.Remove(path);
            OnPropertyChanged(nameof(HasRecentFolders));
            SaveHistory();
            return;
        }
        await LoadFolder(path);
    }

    partial void OnSelectedVersionChanged(VersionData? value)
    {
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

            FileCount = files.Count(x => x.ObjectType == ObjectType.File);
            FolderCount = files.Count(x => x.ObjectType == ObjectType.Directory);
            TotalSizeFormatted = FormatSize(files.Where(x => x.ObjectType == ObjectType.File).Sum(x => x.Length));

            var tree = _manager.GetFileTree(value.NodeId);
            foreach (var node in tree)
                FileTreeRoot.Add(node);
        }
        catch (Exception ex)
        {
            StatusMessage = Lang.Format("FolderPicker.FileListFailed", ex.Message);
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
            Title = Lang["Export.SaveDialogTitle"],
            SuggestedFileName = $"{SelectedVersion.Name}.zip",
            DefaultExtension = "zip",
            FileTypeChoices =
            [
                new FilePickerFileType(Lang["Export.ZipFileType"]) { Patterns = ["*.zip"] },
            ],
        });
        if (file is null) return;

        try
        {
            IsLoading = true;
            StatusMessage = Lang["Export.ExportingZip"];
            await Task.Run(() => _manager.ExportToZip(SelectedVersion.NodeId, file.Path.LocalPath));
            StatusMessage = Lang.Format("Export.Exported", file.Path.LocalPath);
        }
        catch (Exception ex)
        {
            StatusMessage = Lang.Format("Export.Failed", ex.Message);
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
            Title = Lang["Export.FolderPickerTitle"],
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;

        try
        {
            IsLoading = true;
            StatusMessage = Lang["Export.Exporting"];
            await Task.Run(() => _manager.ExportToFolder(SelectedVersion.NodeId, folders[0].Path.LocalPath));
            StatusMessage = Lang.Format("Export.Exported", folders[0].Path.LocalPath);
        }
        catch (Exception ex)
        {
            StatusMessage = Lang.Format("Export.Failed", ex.Message);
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
            StatusMessage = Lang["Snapshot.Applying"];
            await Task.Run(() => _manager.ApplyVersion(SelectedVersion.NodeId));
            StatusMessage = Lang["Snapshot.Applied"];
        }
        catch (Exception ex)
        {
            StatusMessage = Lang.Format("Snapshot.ApplyFailed", ex.Message);
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
            PlaceholderText = Lang["App.DefaultDesc"],
            AcceptsReturn = true,
            MaxHeight = 80,
        };

        var dialog = new FAContentDialog
        {
            Title = Lang["Dialog.CreateSnapshot"],
            PrimaryButtonText = Lang["Dialog.Create"],
            CloseButtonText = Lang["Dialog.Cancel"],
            DefaultButton = FAContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = Lang["Dialog.Name"], FontSize = 12 },
                    nameBox,
                    new TextBlock { Text = Lang["Dialog.Description"], FontSize = 12 },
                    descBox,
                },
            },
        };

        if (await dialog.ShowAsync() != FAContentDialogResult.Primary)
            return;

        try
        {
            IsLoading = true;
            StatusMessage = Lang["Snapshot.Creating"];
            var name = string.IsNullOrWhiteSpace(nameBox.Text)
                ? now.ToString("yyyy/MM/dd-HH:mm:ss")
                : nameBox.Text.Trim();
            var desc = string.IsNullOrWhiteSpace(descBox.Text)
                ? Lang["App.DefaultDesc"]
                : descBox.Text.Trim();
            await Task.Run(() => _manager.CreateVersion(name, desc));
            StatusMessage = Lang["Snapshot.Created"];

            Versions.Clear();
            var versions = await Task.Run(() => _manager.GetVersions());
            foreach (var v in versions)
                Versions.Add(v);
        }
        catch (Exception ex)
        {
            StatusMessage = Lang.Format("Snapshot.CreateFailed", ex.Message);
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
            Title = Lang["Dialog.ConfirmDelete"],
            Content = Lang.Format("Dialog.DeleteConfirmBody", SelectedVersion.Name),
            PrimaryButtonText = Lang["Dialog.Delete"],
            CloseButtonText = Lang["Dialog.Cancel"],
            DefaultButton = FAContentDialogButton.Close,
            IsPrimaryButtonEnabled = true,
        };

        if (await dialog.ShowAsync() != FAContentDialogResult.Primary)
            return;

        try
        {
            IsLoading = true;
            StatusMessage = Lang["Snapshot.Deleting"];
            await Task.Run(() => _manager.DeleteVersion(SelectedVersion.NodeId));
            StatusMessage = Lang["Snapshot.Deleted"];

            Versions.Remove(SelectedVersion);
            SelectedVersion = null;
        }
        catch (Exception ex)
        {
            StatusMessage = Lang.Format("Snapshot.DeleteFailed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SetLanguageAuto() => LanguageManager.Instance.CurrentSetting = "auto";

    [RelayCommand]
    private void SetLanguageZh() => LanguageManager.Instance.CurrentSetting = "zh";

    [RelayCommand]
    private void SetLanguageEn() => LanguageManager.Instance.CurrentSetting = "en";

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
            Title = Lang["Dialog.SoftwareInfo"],
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = Lang["App.Name"], FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    new TextBlock { Text = Lang.Format("App.Version", ver), FontSize = 13 },
                    new TextBlock { Text = Lang["App.Description"], FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 380 },
                    new TextBlock { Text = Lang["App.TechStack"], FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 380 },
                },
            },
            PrimaryButtonText = Lang["Dialog.OK"],
            DefaultButton = FAContentDialogButton.Primary,
        }.ShowAsync();
    }

    [RelayCommand]
    private async Task CheckUpdates()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var spinner = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 4,
            Margin = new Avalonia.Thickness(0, 4),
        };
        var statusText = new TextBlock { Text = Lang["Update.Checking"], FontSize = 12 };
        var resultPanel = new StackPanel { Spacing = 10, Children = { spinner, statusText } };

        var dialog = new FAContentDialog
        {
            Title = Lang["Update.CheckUpdates"],
            Content = resultPanel,
            PrimaryButtonText = Lang["Dialog.OK"],
            DefaultButton = FAContentDialogButton.Primary,
        };

        var showTask = dialog.ShowAsync();

        try
        {
            var latest = await FetchLatestVersionAsync(cts.Token);
            var cur = CurrentVersion;

            if (latest is null)
            {
                resultPanel.Children[1] = new TextBlock { Text = Lang["Update.CheckFailed"], FontSize = 12 };
            }
            else if (Version.TryParse(latest, out var latestV) && Version.TryParse(cur, out var curV) && latestV > curV)
            {
                resultPanel.Children.Clear();
                resultPanel.Children.Add(new TextBlock { Text = Lang.Format("Update.NewVersion", latest), FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold });
                resultPanel.Children.Add(new TextBlock { Text = Lang.Format("Update.CurrentVersion", cur), FontSize = 12, Foreground = Avalonia.Media.Brushes.Gray });
                resultPanel.Spacing = 6;
                dialog.PrimaryButtonText = Lang["Update.JumpDownload"];
                dialog.CloseButtonText = Lang["Dialog.Cancel"];
            }
            else
            {
                resultPanel.Children.Clear();
                resultPanel.Children.Add(new TextBlock { Text = Lang["Update.UpToDate"], FontSize = 14 });
                resultPanel.Children.Add(new TextBlock { Text = $"v{cur}", FontSize = 12, Foreground = Avalonia.Media.Brushes.Gray });
            }
        }
        catch (OperationCanceledException)
        {
            resultPanel.Children[1] = new TextBlock { Text = Lang["Update.Timeout"], FontSize = 12 };
        }
        catch
        {
            resultPanel.Children[1] = new TextBlock { Text = Lang["Update.Failed"], FontSize = 12 };
        }

        if (await showTask == FAContentDialogResult.Primary && dialog.PrimaryButtonText == Lang["Update.JumpDownload"])
            await OpenUrl("https://github.com/PCL-Community/LiteSnap/releases");
    }

    [RelayCommand]
    private async Task OpenLicense()
    {
        await OpenUrl("https://github.com/PCL-Community/LiteSnap/blob/main/LICENSE");
    }

    private static async Task<string?> FetchLatestVersionAsync(CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync("repos/PCL-Community/LiteSnap/releases/latest", ct);
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


