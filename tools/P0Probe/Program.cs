// @file Program.cs
// @author: rudals252
// @brief P0 probe: measures unpackaged WinRT notification access, query, NotificationChanged
//        subscription and 1-second polling separately. Prints only technical metadata
//        (AUMID, notification ID, creation time, event kind). Never reads or prints toast text.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.ApplicationModel;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace P0Probe;

internal static class Program
{
    // Start menu AUMID of Windows PowerShell; commonly allowed to show toasts from unpackaged code.
    private const string DefaultToastAumid = "{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\WindowsPowerShell\\v1.0\\powershell.exe";
    private const string ToastGroup = "p0probe";

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object LogGate = new();
    private static StreamWriter? _logFile;

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    private static async Task<int> Main(string[] args)
    {
        var mode = args.Length > 0 && !args[0].StartsWith("--") ? args[0].ToLowerInvariant() : "probe";
        var opts = ParseOptions(args);
        if (opts.TryGetValue("out", out var outPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            _logFile = new StreamWriter(outPath, append: true) { AutoFlush = true };
        }

        try
        {
            return mode switch
            {
                "appdata" => AppDataCheck(),
                "exit" => await ExitModeAsync(opts),
                "probe" => await ProbeAsync(opts),
                _ => Usage()
            };
        }
        finally
        {
            _logFile?.Dispose();
        }
    }

    private static int Usage()
    {
        Console.WriteLine("usage: P0Probe [probe|appdata|exit] [--seconds N] [--toasts K] [--interval-ms 1000]");
        Console.WriteLine("       [--toast-aumid AUMID] [--set-aumid AUMID] [--skip-request] [--out file.jsonl]");
        Console.WriteLine("       exit --code N --sleep S   (scheduler exit-code semantics)");
        return 2;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) continue;
            var key = args[i][2..];
            var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--");
            map[key] = hasValue ? args[++i] : "true";
        }
        return map;
    }

    private static void Log(string evt, object? data = null)
    {
        var line = JsonSerializer.Serialize(new
        {
            t_ms = Clock.ElapsedMilliseconds,
            utc = DateTime.UtcNow.ToString("o"),
            evt,
            data
        });
        lock (LogGate)
        {
            Console.WriteLine(line);
            _logFile?.WriteLine(line);
        }
    }

    private static object Err(Exception ex) => new
    {
        type = ex.GetType().Name,
        hresult = $"0x{ex.HResult:X8}",
        message = ex.Message
    };

    private static object EnvInfo()
    {
        string? package = null;
        try { package = Package.Current.Id.FullName; } catch { }
        using var self = Process.GetCurrentProcess();
        return new
        {
            pid = Environment.ProcessId,
            session_id = self.SessionId,
            user = Environment.UserName,
            interactive = Environment.UserInteractive,
            os = Environment.OSVersion.VersionString,
            has_package_identity = package != null,
            package,
            apartment = Thread.CurrentThread.GetApartmentState().ToString()
        };
    }

    private static int AppDataCheck()
    {
        Log("appdata", new
        {
            env_APPDATA = Environment.GetEnvironmentVariable("APPDATA"),
            GetFolderPath_ApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        });
        return 0;
    }

    private static async Task<int> ExitModeAsync(Dictionary<string, string> opts)
    {
        var code = int.Parse(opts.GetValueOrDefault("code", "0"));
        var sleep = int.Parse(opts.GetValueOrDefault("sleep", "5"));
        Log("exit-mode-start", new { env = EnvInfo(), code, sleep });
        await Task.Delay(TimeSpan.FromSeconds(sleep));
        Log("exit-mode-end", new { code });
        return code;
    }

    private sealed record Meta(string Aumid, uint Id, string Created);

    private static Meta ToMeta(UserNotification n)
    {
        string aumid;
        try { aumid = n.AppInfo?.AppUserModelId ?? "(null)"; } catch (Exception ex) { aumid = $"(error {ex.HResult:X8})"; }
        return new Meta(aumid, n.Id, n.CreationTime.ToString("o"));
    }

    private static string Key(Meta m) => $"{m.Aumid}|{m.Id}|{m.Created}";

    private static async Task<T> WithTimeout<T>(Task<T> task, int ms)
    {
        var done = await Task.WhenAny(task, Task.Delay(ms));
        if (done != task) throw new TimeoutException($"timed out after {ms} ms");
        return await task;
    }

    private static async Task<int> ProbeAsync(Dictionary<string, string> opts)
    {
        var seconds = int.Parse(opts.GetValueOrDefault("seconds", "30"));
        var toastCount = int.Parse(opts.GetValueOrDefault("toasts", "2"));
        var intervalMs = int.Parse(opts.GetValueOrDefault("interval-ms", "1000"));
        var toastAumid = opts.GetValueOrDefault("toast-aumid", DefaultToastAumid);
        var runId = Guid.NewGuid().ToString("N")[..8];
        var summary = new Dictionary<string, object?> { ["run_id"] = runId };

        Log("env", EnvInfo());

        if (opts.TryGetValue("set-aumid", out var setAumid))
        {
            var hr = SetCurrentProcessExplicitAppUserModelID(setAumid);
            Log("set-aumid", new { aumid = setAumid, hresult = $"0x{hr:X8}" });
            summary["explicit_aumid"] = setAumid;
        }

        UserNotificationListener listener;
        try
        {
            listener = UserNotificationListener.Current;
            Log("listener-current", new { ok = true });
        }
        catch (Exception ex)
        {
            Log("listener-current", new { ok = false, error = Err(ex) });
            summary["listener_current"] = "failed";
            Log("summary", summary);
            return 10;
        }

        // (a) access status before/after RequestAccessAsync
        summary["access_before"] = SafeAccess(listener);
        if (!opts.ContainsKey("skip-request"))
        {
            try
            {
                var r = await WithTimeout(listener.RequestAccessAsync().AsTask(), 20000);
                summary["request_access"] = r.ToString();
                Log("request-access", new { ok = true, result = r.ToString() });
            }
            catch (Exception ex)
            {
                summary["request_access"] = $"error {ex.GetType().Name} 0x{ex.HResult:X8}";
                Log("request-access", new { ok = false, error = Err(ex) });
            }
        }
        summary["access_after"] = SafeAccess(listener);

        // (b) query
        var seen = new HashSet<string>();
        try
        {
            var sw = Stopwatch.StartNew();
            var items = await WithTimeout(listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask(), 10000);
            var metas = items.Select(ToMeta).ToList();
            foreach (var m in metas) seen.Add(Key(m));
            summary["query_ok"] = true;
            summary["query_count"] = metas.Count;
            summary["query_ms"] = sw.ElapsedMilliseconds;
            summary["query_aumids"] = metas.GroupBy(m => m.Aumid).ToDictionary(g => g.Key, g => g.Count());
            Log("query", new { ok = true, count = metas.Count, ms = sw.ElapsedMilliseconds, items = metas });
        }
        catch (Exception ex)
        {
            summary["query_ok"] = false;
            summary["query_error"] = $"{ex.GetType().Name} 0x{ex.HResult:X8}";
            Log("query", new { ok = false, error = Err(ex) });
        }

        // (c) subscription registration + actual event delivery
        var eventCounts = new Dictionary<string, int>();
        var sentAt = new List<long>();
        var probeEventAdded = 0;
        void OnChanged(UserNotificationListener s, UserNotificationChangedEventArgs e)
        {
            string kind; uint id;
            try { kind = e.ChangeKind.ToString(); id = e.UserNotificationId; }
            catch (Exception ex) { Log("event-args-error", Err(ex)); return; }
            string? aumid = null;
            try { aumid = s.GetNotification(id)?.AppInfo?.AppUserModelId; } catch { }
            lock (eventCounts)
            {
                eventCounts[kind] = eventCounts.GetValueOrDefault(kind) + 1;
                if (kind == "Added" && aumid == toastAumid) probeEventAdded++;
            }
            Log("event", new { kind, id, aumid, ms_since_last_toast = LastSendDelta(sentAt) });
        }

        var subscribed = false;
        try
        {
            listener.NotificationChanged += OnChanged;
            subscribed = true;
            summary["subscribe_ok"] = true;
            Log("subscribe", new { ok = true });
        }
        catch (Exception ex)
        {
            summary["subscribe_ok"] = false;
            summary["subscribe_error"] = $"{ex.GetType().Name} 0x{ex.HResult:X8}";
            Log("subscribe", new { ok = false, error = Err(ex) });
        }

        // (d) 1-second non-overlapping polling with a startup baseline (no retroactive handling)
        var probePollNew = 0;
        var pollErrors = 0;
        var polls = 0;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var lastAccess = SafeAccess(listener);
        var pollTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                var tickStart = Clock.ElapsedMilliseconds;
                try
                {
                    var access = SafeAccess(listener);
                    if (access != lastAccess)
                    {
                        Log("access-changed", new { from = lastAccess, to = access });
                        lastAccess = access;
                    }
                    var items = await WithTimeout(listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask(), 5000);
                    polls++;
                    var current = new HashSet<string>();
                    foreach (var n in items)
                    {
                        var m = ToMeta(n);
                        var k = Key(m);
                        current.Add(k);
                        if (seen.Add(k))
                        {
                            if (m.Aumid == toastAumid) probePollNew++;
                            Log("poll-new", new { m.Aumid, m.Id, m.Created, ms_since_last_toast = LastSendDelta(sentAt), poll = polls });
                        }
                    }
                    // Keep only keys still present (same retention rule as the PS1 watcher).
                    seen.IntersectWith(current);
                }
                catch (Exception ex)
                {
                    pollErrors++;
                    Log("poll-error", Err(ex));
                }
                var wait = intervalMs - (int)(Clock.ElapsedMilliseconds - tickStart);
                if (wait > 0)
                {
                    try { await Task.Delay(wait, cts.Token); } catch (TaskCanceledException) { }
                }
            }
        });

        // Local test toasts (generic technical text; content is never read back).
        var toastResults = new List<object>();
        await Task.Delay(3000);
        for (var i = 1; i <= toastCount && !cts.IsCancellationRequested; i++)
        {
            try
            {
                var xml = new XmlDocument();
                xml.LoadXml($"<toast><visual><binding template=\"ToastGeneric\"><text>P0Probe local test {i}</text><text>run {runId}</text></binding></visual><audio silent=\"true\"/></toast>");
                var toast = new ToastNotification(xml) { Tag = $"{runId}-{i}", Group = ToastGroup };
                var notifier = ToastNotificationManager.CreateToastNotifier(toastAumid);
                string setting;
                try { setting = notifier.Setting.ToString(); } catch (Exception ex) { setting = $"error 0x{ex.HResult:X8}"; }
                lock (sentAt) sentAt.Add(Clock.ElapsedMilliseconds);
                notifier.Show(toast);
                toastResults.Add(new { i, ok = true, setting });
                Log("toast-sent", new { i, aumid = toastAumid, notifier_setting = setting });
            }
            catch (Exception ex)
            {
                toastResults.Add(new { i, ok = false, error = $"{ex.GetType().Name} 0x{ex.HResult:X8}" });
                Log("toast-sent", new { i, ok = false, error = Err(ex) });
            }
            await Task.Delay(5000);
        }

        await pollTask;
        if (subscribed)
        {
            try { listener.NotificationChanged -= OnChanged; } catch { }
        }

        // Remove only this run's test toasts from the action center.
        for (var i = 1; i <= toastCount; i++)
        {
            try { ToastNotificationManager.History.Remove($"{runId}-{i}", ToastGroup, toastAumid); }
            catch (Exception ex) { Log("toast-cleanup-error", new { i, error = Err(ex) }); }
        }

        summary["toasts"] = toastResults;
        summary["event_counts"] = eventCounts;
        summary["event_added_from_toast_aumid"] = probeEventAdded;
        summary["polls"] = polls;
        summary["poll_errors"] = pollErrors;
        summary["poll_new_from_toast_aumid"] = probePollNew;
        summary["interval_ms"] = intervalMs;
        summary["seconds"] = seconds;
        Log("summary", summary);
        return 0;
    }

    private static long? LastSendDelta(List<long> sentAt)
    {
        lock (sentAt)
        {
            return sentAt.Count == 0 ? null : Clock.ElapsedMilliseconds - sentAt[^1];
        }
    }

    private static string SafeAccess(UserNotificationListener listener)
    {
        try { return listener.GetAccessStatus().ToString(); }
        catch (Exception ex) { return $"error {ex.GetType().Name} 0x{ex.HResult:X8}"; }
    }
}
