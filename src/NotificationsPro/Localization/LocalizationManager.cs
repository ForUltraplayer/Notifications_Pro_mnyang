// @file LocalizationManager.cs
// @author: rudals252
// @brief 현재 UI 문화권(한국어/영어)을 결정·보관하고 .resx 문구를 조회한다. 문화권이 바뀌면 바인딩 갱신용 PropertyChanged와 CultureChanged를 알린다. 숫자·날짜 저장 형식(CurrentCulture)은 건드리지 않는다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace NotificationsPro.Localization;

/// <summary>
/// Single source of UI strings. Lookups use the explicitly selected culture (not the calling thread's
/// culture), so results are deterministic on every thread. Missing Korean keys fall back to English
/// through the neutral resources; a key missing everywhere returns the key itself.
/// Until <see cref="Apply"/> is called (app startup), the culture is English.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    public const string KoreanCultureName = "ko-KR";
    public const string EnglishCultureName = "en-US";
    internal const string ResourceBaseName = "NotificationsPro.Resources.Strings";

    /// <summary>OS display language captured before the app overrides the thread UI culture.</summary>
    public static CultureInfo OsUiCulture { get; } = CultureInfo.CurrentUICulture;

    internal static ResourceManager DefaultResourceManager { get; } =
        new(ResourceBaseName, typeof(LocalizationManager).Assembly);

    public static LocalizationManager Instance { get; } = new(DefaultResourceManager);

    private readonly ResourceManager _resources;
    private CultureInfo _culture = CultureInfo.GetCultureInfo(EnglishCultureName);

    internal LocalizationManager(ResourceManager resources)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised on the thread that changed the culture (the UI thread in the app).</summary>
    public event EventHandler? CultureChanged;

    /// <summary>Current UI culture used for every lookup.</summary>
    public CultureInfo Culture => _culture;

    /// <summary>Indexer used by XAML bindings (<c>Path=[Key]</c>).</summary>
    public string this[string key] => GetString(key);

    public string GetString(string key) => Lookup(_resources, key, _culture) ?? key;

    public string FormatString(string key, params object?[] args)
        => string.Format(_culture, GetString(key), args);

    /// <summary>True when the key exists in the neutral (English) resources.</summary>
    public bool HasKey(string key) => Lookup(_resources, key, CultureInfo.InvariantCulture) != null;

    /// <summary>Returns the localized text, or null when the key is unknown.</summary>
    public string? TryGetString(string key) => Lookup(_resources, key, _culture);

    public static string Get(string key) => Instance.GetString(key);

    public static string Format(string key, params object?[] args) => Instance.FormatString(key, args);

    /// <summary>
    /// Explicit choice wins ("ko-KR" / "en-US"); otherwise (null or unknown) an OS UI culture
    /// whose name starts with "ko" selects Korean and everything else selects English.
    /// </summary>
    public static CultureInfo ResolveCulture(string? uiLanguage, CultureInfo? osUiCulture)
    {
        if (string.Equals(uiLanguage, KoreanCultureName, StringComparison.OrdinalIgnoreCase))
            return CultureInfo.GetCultureInfo(KoreanCultureName);
        if (string.Equals(uiLanguage, EnglishCultureName, StringComparison.OrdinalIgnoreCase))
            return CultureInfo.GetCultureInfo(EnglishCultureName);

        var osName = osUiCulture?.Name ?? string.Empty;
        return osName.StartsWith("ko", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo(KoreanCultureName)
            : CultureInfo.GetCultureInfo(EnglishCultureName);
    }

    /// <summary>Applies the stored <c>UiLanguage</c> value (null = OS default).</summary>
    public void Apply(string? uiLanguage) => SetCulture(ResolveCulture(uiLanguage, OsUiCulture));

    /// <summary>
    /// Switches the UI culture: the calling (UI) thread and threads created afterwards get it as
    /// CurrentUICulture. CurrentCulture (number/date formatting) is left unchanged on purpose.
    /// </summary>
    public void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var changed = !string.Equals(culture.Name, _culture.Name, StringComparison.OrdinalIgnoreCase);
        _culture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        if (!changed)
            return;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static string? Lookup(ResourceManager resources, string key, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        try
        {
            return resources.GetString(key, culture);
        }
        catch (MissingManifestResourceException)
        {
            return null;
        }
        catch (MissingSatelliteAssemblyException)
        {
            return resources.GetString(key, CultureInfo.InvariantCulture);
        }
    }
}
