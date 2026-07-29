using System.Diagnostics;
using System.Runtime.InteropServices;

namespace YierPet;

public enum AppCategory
{
    Ide,
    Terminal,
    Video,
    Music,
    Social,
    Game,
}

public static class AppCategoryExtensions
{
    public static string[] EmotionTags(this AppCategory cat) => cat switch
    {
        AppCategory.Ide or AppCategory.Terminal => ["work"],
        AppCategory.Video or AppCategory.Music => ["lazy", "happy"],
        AppCategory.Social => ["love", "happy"],
        AppCategory.Game => ["happy"],
        _ => [],
    };
}

/// <summary>Tracks input idle time and the foreground process (no extra permissions).</summary>
public sealed class ActivityMonitor
{
    /// <summary>Process names (without .exe) treated as slacking / video apps.</summary>
    public static readonly HashSet<string> SlackProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bilibili", "哔哩哔哩", "QQPlayer", "QQLive", "iQIYI", "YouKuDesktop",
        "youku", "PotPlayerMini64", "PotPlayer", "vlc", "CloudMusic",
        "QQMusic", "ThunderPlayer", "mpv", "WMP", "wmplayer",
    };

    private static readonly Dictionary<string, AppCategory> AppCategories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // IDE
            ["devenv"] = AppCategory.Ide,
            ["Code"] = AppCategory.Ide,
            ["idea64"] = AppCategory.Ide,
            ["idea"] = AppCategory.Ide,
            ["pycharm64"] = AppCategory.Ide,
            ["WebStorm64"] = AppCategory.Ide,
            ["rider64"] = AppCategory.Ide,
            ["goland64"] = AppCategory.Ide,
            ["clion64"] = AppCategory.Ide,
            ["datagrip64"] = AppCategory.Ide,
            ["Cursor"] = AppCategory.Ide,
            // Terminal
            ["WindowsTerminal"] = AppCategory.Terminal,
            ["wt"] = AppCategory.Terminal,
            ["powershell"] = AppCategory.Terminal,
            ["cmd"] = AppCategory.Terminal,
            // Video
            ["bilibili"] = AppCategory.Video,
            ["QQPlayer"] = AppCategory.Video,
            ["QQLive"] = AppCategory.Video,
            ["iQIYI"] = AppCategory.Video,
            ["YouKuDesktop"] = AppCategory.Video,
            ["PotPlayerMini64"] = AppCategory.Video,
            ["vlc"] = AppCategory.Video,
            // Music
            ["CloudMusic"] = AppCategory.Music,
            ["QQMusic"] = AppCategory.Music,
            ["Spotify"] = AppCategory.Music,
            // Social
            ["WeChat"] = AppCategory.Social,
            ["QQ"] = AppCategory.Social,
            ["TIM"] = AppCategory.Social,
            // Game
            ["steam"] = AppCategory.Game,
            ["Steam"] = AppCategory.Game,
        };

    private string? _frontProcessName;
    private DateTime _frontSince = DateTime.UtcNow;

    public ActivityMonitor()
    {
        RefreshForeground();
        SystemEvents.ForegroundChanged += (_, _) => RefreshForeground();
    }

    public string? FrontProcessName => _frontProcessName;

    public AppCategory? FrontCategory
    {
        get
        {
            if (_frontProcessName == null) return null;
            return AppCategories.GetValueOrDefault(_frontProcessName);
        }
    }

    public double SlackingSeconds
    {
        get
        {
            if (_frontProcessName == null ||
                !SlackProcessNames.Contains(_frontProcessName))
                return 0;
            return (DateTime.UtcNow - _frontSince).TotalSeconds;
        }
    }

    public static double IdleSeconds()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return double.PositiveInfinity;
        var idleMs = Environment.TickCount64 - info.dwTime;
        return idleMs / 1000.0;
    }

    private void RefreshForeground()
    {
        var name = GetForegroundProcessName();
        if (name != _frontProcessName)
        {
            _frontProcessName = name;
            _frontSince = DateTime.UtcNow;
        }
    }

    private static string? GetForegroundProcessName()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;
        try
        {
            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>Polls foreground window when Win32 events are unavailable.</summary>
    private static class SystemEvents
    {
        public static event EventHandler? ForegroundChanged;
        private static readonly System.Windows.Threading.DispatcherTimer Timer = new()
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        private static string? _last;

        static SystemEvents()
        {
            Timer.Tick += (_, _) =>
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return;
                _ = GetWindowThreadProcessId(hwnd, out var pid);
                string? name = null;
                try { name = Process.GetProcessById((int)pid).ProcessName; }
                catch { /* ignore */ }
                if (name != _last)
                {
                    _last = name;
                    ForegroundChanged?.Invoke(null, EventArgs.Empty);
                }
            };
            Timer.Start();
        }
    }
}
