using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using LiteSnap.App.Services;

namespace LiteSnap.App;

public class LanguageExtension : MarkupExtension
{
    public string? Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (Key is null) return "";
        return new Binding
        {
            Source = LanguageManager.Instance,
            Path = $"[{Key}]",
            Mode = BindingMode.OneWay,
        };
    }
}
