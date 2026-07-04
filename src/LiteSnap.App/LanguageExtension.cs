using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using LiteSnap.App.Services;

namespace LiteSnap.App;

public class LanguageExtension : MarkupExtension
{
    public string? Key { get; set; }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Dynamic Binding for i18n is by-design")]
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
