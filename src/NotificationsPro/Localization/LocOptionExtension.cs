// @file LocOptionExtension.cs
// @author: rudals252
// @brief 저장값 문자열 목록(글꼴 굵기·정렬·모드 등)을 콤보 상자에 보일 때만 번역하는 XAML 마크업 확장. 저장값은 그대로 두고, 키(접두어_값)가 없으면 저장값을 그대로 보여준다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace NotificationsPro.Localization;

/// <summary>
/// Usage inside an item template: <c>Text="{loc:LocOption}"</c> (prefix "Option") or
/// <c>Text="{loc:LocOption ThemeMode}"</c>. The bound item is the stored value; only the
/// displayed text changes.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocOptionExtension : MarkupExtension
{
    public const string DefaultPrefix = "Option";

    public LocOptionExtension()
    {
    }

    public LocOptionExtension(string prefix)
    {
        Prefix = prefix;
    }

    [ConstructorArgument("prefix")]
    public string Prefix { get; set; } = DefaultPrefix;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var multi = new MultiBinding
        {
            Converter = new OptionConverter(Prefix),
            Mode = BindingMode.OneWay,
        };
        multi.Bindings.Add(new System.Windows.Data.Binding());
        multi.Bindings.Add(new System.Windows.Data.Binding(nameof(LocalizationManager.Culture))
        {
            Source = LocalizationManager.Instance,
        });
        return multi.ProvideValue(serviceProvider);
    }

    /// <summary>
    /// Resource key for a stored option value: non-alphanumeric characters are dropped and each word
    /// starts upper-case, e.g. ("Option", "Framed Group") → "Option_FramedGroup", "Use global" → "Option_UseGlobal".
    /// </summary>
    public static string KeyFor(string prefix, string value)
    {
        var builder = new StringBuilder(prefix.Length + value.Length + 1);
        builder.Append(prefix).Append('_');
        var wordStart = true;
        foreach (var ch in value)
        {
            if (!char.IsAsciiLetterOrDigit(ch))
            {
                wordStart = true;
                continue;
            }

            builder.Append(wordStart ? char.ToUpperInvariant(ch) : ch);
            wordStart = false;
        }
        return builder.ToString();
    }

    /// <summary>Display text for a stored option value; unknown values are shown unchanged.</summary>
    public static string Display(string prefix, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;
        return LocalizationManager.Instance.TryGetString(KeyFor(prefix, value)) ?? value;
    }

    private sealed class OptionConverter : IMultiValueConverter
    {
        private readonly string _prefix;

        public OptionConverter(string prefix) => _prefix = prefix;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var value = values.Length > 0 ? values[0] : null;
            if (value == null || value == DependencyProperty.UnsetValue)
                return string.Empty;
            return Display(_prefix, value.ToString());
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
