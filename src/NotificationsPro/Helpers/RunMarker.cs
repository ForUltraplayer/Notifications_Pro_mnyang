// @file RunMarker.cs
// @author: rudals252
// @brief 주 인스턴스 실행 표식(세션 토큰·시작 시각·버전만 담은 run-state.json) 쓰기·정리·판정. 시작 때 이전 표식이 남아 있으면 이전 실행이 정상 종료되지 않은 것으로 보고, 정상 종료 경로에서는 자기 토큰의 표식만 지운다. 알림 내용은 기록하지 않는다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.IO;
using System.Text.Json;
using NotificationsPro.Models;

namespace NotificationsPro.Helpers;

/// <summary>
/// Run marker for design §9.2 (R08: tell the user at the next start that the previous run did not end normally).
/// <list type="bullet">
/// <item>Only the primary instance calls <see cref="BeginSession"/>, after the single-instance check.
/// CLI-forwarding second launches never touch the file (<see cref="BeginForPrimaryInstance"/>).</item>
/// <item>The file holds a session token, the start time and the app version only. It is local operational
/// metadata kept apart from settings, themes and profiles and is never exported.</item>
/// <item><see cref="EndSession"/> (normal quit, sign-out/shutdown, future update path) deletes the file only when it
/// still carries this run's token, so a marker from another run is never cleared by mistake.</item>
/// </list>
/// </summary>
public sealed class RunMarker
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Func<DateTimeOffset> _clock;
    private readonly object _sync = new();

    public RunMarker(string path, Func<DateTimeOffset>? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string Path { get; }

    /// <summary>Token written by this run; null before <see cref="BeginSession"/> or after the write failed.</summary>
    public string? CurrentToken { get; private set; }

    /// <summary>
    /// Starts the marker only for the primary instance. A second launch (CLI forwarding / show settings)
    /// returns null without reading or writing anything.
    /// </summary>
    public static RunMarkerStartResult? BeginForPrimaryInstance(bool isPrimaryInstance, RunMarker marker, string version)
    {
        ArgumentNullException.ThrowIfNull(marker);
        return isPrimaryInstance ? marker.BeginSession(version) : null;
    }

    /// <summary>
    /// Reads any marker left by an earlier run (its presence means that run did not end normally),
    /// then writes this run's marker with a fresh token.
    /// </summary>
    public RunMarkerStartResult BeginSession(string version)
    {
        lock (_sync)
        {
            var previousExists = false;
            DateTimeOffset? previousStartedAt = null;
            string? previousVersion = null;
            try
            {
                if (File.Exists(Path))
                {
                    previousExists = true;
                    var previous = TryRead();
                    previousStartedAt = previous?.StartedAt;
                    previousVersion = previous?.Version;
                }
            }
            catch
            {
                // Presence already decided the result; unreadable details stay unknown.
            }

            var token = Guid.NewGuid().ToString("N");
            try
            {
                var record = new MarkerRecord
                {
                    Format = FormatVersion,
                    SessionToken = token,
                    StartedAt = _clock(),
                    Version = version ?? string.Empty,
                };
                AtomicFileWriter.WriteAllText(Path, JsonSerializer.Serialize(record, JsonOptions));
                CurrentToken = token;
                return new RunMarkerStartResult(previousExists, previousStartedAt, previousVersion, false, null);
            }
            catch (Exception ex)
            {
                CurrentToken = null;
                return new RunMarkerStartResult(previousExists, previousStartedAt, previousVersion, true,
                    $"{ex.GetType().Name} 0x{ex.HResult:X8}");
            }
        }
    }

    /// <summary>
    /// Clears this run's marker. Returns true when the file is gone afterwards and false when it was left
    /// (another run's token, nothing begun, or an I/O error). Safe to call more than once.
    /// </summary>
    public bool EndSession()
    {
        lock (_sync)
        {
            if (CurrentToken == null)
                return false;

            try
            {
                if (!File.Exists(Path))
                {
                    CurrentToken = null;
                    return true;
                }

                var stored = TryRead();
                if (stored == null || !string.Equals(stored.SessionToken, CurrentToken, StringComparison.Ordinal))
                    return false;

                File.Delete(Path);
                CurrentToken = null;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private MarkerRecord? TryRead()
    {
        try
        {
            return JsonSerializer.Deserialize<MarkerRecord>(File.ReadAllText(Path));
        }
        catch
        {
            return null;
        }
    }

    private sealed class MarkerRecord
    {
        public int Format { get; set; }

        public string SessionToken { get; set; } = string.Empty;

        public DateTimeOffset StartedAt { get; set; }

        public string Version { get; set; } = string.Empty;
    }
}
