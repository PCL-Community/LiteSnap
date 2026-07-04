using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Platform;

namespace LiteSnap.App.Services;

public class LanguageManager : INotifyPropertyChanged
{
    public static LanguageManager Instance { get; } = new();

    private Dictionary<string, string> _strings = [];
    private string _setting = "auto";
    private string _resolved = "zh";

    public string CurrentSetting
    {
        get => _setting;
        set
        {
            if (_setting == value) return;
            _setting = value;
            _ = LoadAsync(Resolve(value));
            PropertyChanged?.Invoke(this, new(nameof(CurrentSetting)));
            PropertyChanged?.Invoke(this, new(nameof(IsAuto)));
            PropertyChanged?.Invoke(this, new(nameof(IsZh)));
            PropertyChanged?.Invoke(this, new(nameof(IsEn)));
        }
    }

    public string CurrentLanguage => _resolved;
    public bool IsAuto => _setting == "auto";
    public bool IsZh => _setting == "zh";
    public bool IsEn => _setting == "en";

    private static string Resolve(string setting) => setting switch
    {
        "zh" => "zh",
        "en" => "en",
        _ => System.Globalization.CultureInfo.CurrentCulture.Name.StartsWith("zh") ? "zh" : "en",
    };

    public string this[string key] =>
        _strings.TryGetValue(key, out var v) ? v : $"?{key}";

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAsync(string lang)
    {
        try
        {
            var uri = new Uri($"avares://LiteSnap.App/Locales/{lang}.json");
            using var stream = AssetLoader.Open(uri);
            using var doc = await JsonDocument.ParseAsync(stream);
            var dict = new Dictionary<string, string>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.GetString() ?? "";
            }
            _strings = dict;
            _resolved = lang;
        }
        catch
        {
            _strings = [];
        }

        PropertyChanged?.Invoke(this, new(nameof(CurrentLanguage)));
        PropertyChanged?.Invoke(this, new("Item"));
        PropertyChanged?.Invoke(this, new("Item[]"));
    }

    public string Format(string key, params object?[] args)
    {
        var template = this[key];
        return args.Length > 0 ? string.Format(template, args) : template;
    }
}
