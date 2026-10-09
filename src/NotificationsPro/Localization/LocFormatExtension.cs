// @file LocFormatExtension.cs
// @author: rudals252
// @brief XAML 마크업 확장 {loc:LocFormat 키, Path=속성}. 바인딩 값을 리소스 서식 문자열({0})에 넣고, 언어가 바뀌면 다시 계산한다(기존 StringFormat 대체).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace NotificationsPro.Localization;

/// <summary>Usage: <c>Text="{loc:LocFormat Appearance_CurrentPreset, Path=DensityPreset}"</c>.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocFormatExtension : MarkupExtension
{
    public LocFormatExtension()
    {
    }

    public LocFormatExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    /// <summary>Optional: the bound value is a stored option value shown through <see cref="LocOptionExtension.Display"/>.</summary>
    public string? OptionPrefix { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var multi = new MultiBinding
        {
            Converter = new LocFormatConverter(Key, OptionPrefix),
            Mode = BindingMode.OneWay,
        };
        multi.Bindings.Add(new System.Windows.Data.Binding(Path));
        multi.Bindings.Add(new System.Windows.Data.Binding(nameof(LocalizationManager.Culture))
        {
            Source = LocalizationManager.Instance,
        });
        return multi.ProvideValue(serviceProvider);
    }

    private sealed class LocFormatConverter : IMultiValueConverter
    {
        private readonly string _key;
        private readonly string? _optionPrefix;

        public LocFormatConverter(string key, string? optionPrefix)
        {
            _key = key;
            _optionPrefix = optionPrefix;
        }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var value = values.Length > 0 ? values[0] : null;
            if (value == DependencyProperty.UnsetValue)
                value = null;
            if (_optionPrefix != null && value != null)
                value = LocOptionExtension.Display(_optionPrefix, value.ToString());
            return LocalizationManager.Format(_key, value);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
