// @file OptionItem.cs
// @author: rudals252
// @brief 콤보 상자용 (저장값, 표시 문구) 한 쌍. 저장값은 번역하지 않고 표시 문구만 바뀐다. 리소스 키로 만든 항목은 UI 언어가 바뀌면 Label 변경을 알린다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.ComponentModel;
using NotificationsPro.Localization;

namespace NotificationsPro.ViewModels;

/// <summary>One combo-box option: the stored <see cref="Value"/> and the display <see cref="Label"/>.</summary>
public sealed class OptionItem<T> : INotifyPropertyChanged
{
    private readonly Func<string> _label;

    /// <summary>Fixed label (not translated, e.g. an app ID).</summary>
    public OptionItem(T value, string label)
    {
        Value = value;
        _label = () => label;
    }

    /// <summary>
    /// Label produced on demand from the UI-language resources. Listens weakly for culture changes so
    /// bound combo boxes refresh without keeping the item alive.
    /// </summary>
    public OptionItem(T value, Func<string> label)
    {
        Value = value;
        _label = label ?? throw new ArgumentNullException(nameof(label));
        PropertyChangedEventManager.AddHandler(LocalizationManager.Instance, OnCultureChanged, nameof(LocalizationManager.Culture));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public T Value { get; }

    public string Label => _label();

    /// <summary>Option whose label is the resource string <paramref name="key"/>.</summary>
    public static OptionItem<T> Localized(T value, string key) => new(value, () => LocalizationManager.Get(key));

    public override string ToString() => Label;

    private void OnCultureChanged(object? sender, PropertyChangedEventArgs e)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
}
