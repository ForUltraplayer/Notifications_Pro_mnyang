// @file AppIdentityRegistry.cs
// @author: rudals252
// @brief 이번 세션에 관찰한 앱 표시 이름 ↔ AUMID 대응을 RAM에만 기록해, 같은 표시 이름에 여러 AUMID가 보이는 경우와 AUMID의 표시 이름을 앱 탭에 노출한다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Services;

/// <summary>
/// Session-only (RAM) map of app display name → observed AppUserModelIds. Used to surface the
/// "one display name, several AUMIDs" case so the user can choose (design §7.1). Holds app
/// identity strings only, never notification text, and is bounded in size.
/// </summary>
public sealed class AppIdentityRegistry
{
    public const int MaxDisplayNames = 512;
    public const int MaxAppIdsPerDisplayName = 16;

    private readonly Dictionary<string, HashSet<string>> _appIdsByDisplayName = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    /// <summary>Records one observation. Blank names or ids are ignored.</summary>
    public void Observe(string? displayName, string? appId)
    {
        var name = displayName?.Trim();
        var id = appId?.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id))
            return;

        lock (_sync)
        {
            if (!_appIdsByDisplayName.TryGetValue(name, out var ids))
            {
                if (_appIdsByDisplayName.Count >= MaxDisplayNames)
                    return;

                ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _appIdsByDisplayName[name] = ids;
            }

            if (ids.Count < MaxAppIdsPerDisplayName)
                ids.Add(id);
        }
    }

    public IReadOnlyCollection<string> GetAppIds(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return Array.Empty<string>();

        lock (_sync)
        {
            if (!_appIdsByDisplayName.TryGetValue(displayName.Trim(), out var ids))
                return Array.Empty<string>();

            return ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>The display name an AUMID was observed with this session, or null (first match by name order).</summary>
    public string? FindDisplayNameForAppId(string? appId)
    {
        var id = appId?.Trim();
        if (string.IsNullOrEmpty(id))
            return null;

        lock (_sync)
        {
            return _appIdsByDisplayName
                .Where(pair => pair.Value.Contains(id))
                .Select(pair => pair.Key)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
    }

    public bool HasMultipleAppIds(string? displayName) => GetAppIds(displayName).Count > 1;

    /// <summary>Display names for which more than one AUMID has been observed this session.</summary>
    public IReadOnlyList<string> GetAmbiguousDisplayNames()
    {
        lock (_sync)
        {
            return _appIdsByDisplayName
                .Where(pair => pair.Value.Count > 1)
                .Select(pair => pair.Key)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public void Clear()
    {
        lock (_sync)
            _appIdsByDisplayName.Clear();
    }
}
