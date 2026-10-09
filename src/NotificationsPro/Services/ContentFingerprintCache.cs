// @file ContentFingerprintCache.cs
// @author: rudals252
// @brief 오버레이 밖(표시 수 초과)으로 밀려난 알림의 내용 중복 판정용 RAM 지문 캐시. 본문 대신 SHA-256 앞 128비트만, 크기·TTL 제한으로 보관하고 디스크에 쓰지 않는다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Security.Cryptography;
using System.Text;

namespace NotificationsPro.Services;

/// <summary>
/// RAM-only content fingerprints for notifications that passed policy but were not kept in the
/// visible overlay queue, so the user's DeduplicationEnabled window still applies to them.
/// Stores a truncated SHA-256 of (app, title, body) plus a timestamp — never the text — with a
/// bounded entry count and age-based pruning. Not thread-safe (used on the UI dispatcher only).
/// </summary>
public sealed class ContentFingerprintCache
{
    public const int DefaultMaxEntries = 256;

    private readonly int _maxEntries;
    private readonly Func<DateTime> _clock;
    private readonly List<(UInt128 Fingerprint, DateTime RecordedAt)> _entries = new();

    public ContentFingerprintCache(int maxEntries = DefaultMaxEntries, Func<DateTime>? clock = null)
    {
        _maxEntries = Math.Max(1, maxEntries);
        _clock = clock ?? (() => DateTime.Now);
    }

    public int Count => _entries.Count;

    public static UInt128 ComputeFingerprint(string? appName, string? title, string? body)
    {
        var text = string.Concat(appName ?? string.Empty, "\u001F", title ?? string.Empty, "\u001F", body ?? string.Empty);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(text), hash);
        var upper = BitConverter.ToUInt64(hash[..8]);
        var lower = BitConverter.ToUInt64(hash.Slice(8, 8));
        return new UInt128(upper, lower);
    }

    /// <summary>True when the same fingerprint was recorded less than <paramref name="window"/> ago.</summary>
    public bool Contains(UInt128 fingerprint, TimeSpan window)
    {
        var now = _clock();
        Prune(now, window);
        foreach (var entry in _entries)
        {
            if (entry.Fingerprint == fingerprint && now - entry.RecordedAt < window)
                return true;
        }

        return false;
    }

    public void Record(UInt128 fingerprint, TimeSpan window)
    {
        var now = _clock();
        Prune(now, window);
        _entries.Add((fingerprint, now));
        while (_entries.Count > _maxEntries)
            _entries.RemoveAt(0);
    }

    public void Clear() => _entries.Clear();

    private void Prune(DateTime now, TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
        {
            _entries.Clear();
            return;
        }

        _entries.RemoveAll(entry => now - entry.RecordedAt >= window);
    }
}
