// @file LocExtension.cs
// @author: rudals252
// @brief XAML 마크업 확장 {loc:Loc 키}. LocalizationManager 인덱서에 OneWay 바인딩하므로 언어를 바꾸면 화면 문구가 즉시 갱신된다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Windows.Data;
using System.Windows.Markup;

namespace NotificationsPro.Localization;

/// <summary>Usage: <c>Text="{loc:Loc Common_Save}"</c>.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new System.Windows.Data.Binding($"[{Key}]")
        {
            Source = LocalizationManager.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
