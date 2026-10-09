// @file OptionItem.cs
// @author: rudals252
// @brief 콤보 상자용 (저장값, 표시 문구) 한 쌍. 저장값은 번역하지 않고 표시 문구만 바뀐다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.ViewModels;

/// <summary>One combo-box option: the stored <see cref="Value"/> and the display <see cref="Label"/>.</summary>
public sealed class OptionItem<T>
{
    public OptionItem(T value, string label)
    {
        Value = value;
        Label = label;
    }

    public T Value { get; }

    public string Label { get; }

    public override string ToString() => Label;
}
