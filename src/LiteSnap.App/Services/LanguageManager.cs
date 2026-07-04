using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

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
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($"Locales.{lang}.json"));

        if (name is null)
        {
            _strings = [];
            PropertyChanged?.Invoke(this, new("Item"));
            PropertyChanged?.Invoke(this, new("Item[]"));
            return;
        }

        await using var stream = asm.GetManifestResourceStream(name);
        if (stream is null)
        {
            _strings = [];
            PropertyChanged?.Invoke(this, new("Item"));
            PropertyChanged?.Invoke(this, new("Item[]"));
            return;
        }

        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        _strings = data ?? [];

        _resolved = lang;
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
