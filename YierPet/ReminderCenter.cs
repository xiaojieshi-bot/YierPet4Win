using System.Globalization;

namespace YierPet;

public enum ReminderKind
{
    Sedentary,
    Water,
    LateNight,
    Slacking,
    CpuHigh,
    MemoryPressure,
    BatteryLow,
    BatteryFull,
    DiskFull,
    MorningGreet,
    LunchTime,
    AfternoonCoffee,
    FridayEvening,
    IdeOvertime,
}

public static class ReminderKindExtensions
{
    public static string Title(this ReminderKind kind) => kind switch
    {
        ReminderKind.Sedentary => "久坐提醒",
        ReminderKind.Water => "喝水提醒",
        ReminderKind.LateNight => "深夜关怀",
        ReminderKind.Slacking => "摸鱼检测",
        ReminderKind.CpuHigh => "CPU 红温",
        ReminderKind.MemoryPressure => "内存压力",
        ReminderKind.BatteryLow => "电量提醒",
        ReminderKind.BatteryFull => "充满提醒",
        ReminderKind.DiskFull => "磁盘空间",
        ReminderKind.MorningGreet => "早晨问候",
        ReminderKind.LunchTime => "午饭提醒",
        ReminderKind.AfternoonCoffee => "下午咖啡",
        ReminderKind.FridayEvening => "周五撒欢",
        ReminderKind.IdeOvertime => "编码久战",
        _ => kind.ToString(),
    };

    public static bool IsSystem(this ReminderKind kind) => kind switch
    {
        ReminderKind.CpuHigh or ReminderKind.MemoryPressure or ReminderKind.BatteryLow
            or ReminderKind.BatteryFull or ReminderKind.DiskFull => true,
        _ => false,
    };

    public static bool IsCompanion(this ReminderKind kind) => kind switch
    {
        ReminderKind.MorningGreet or ReminderKind.LunchTime or ReminderKind.AfternoonCoffee
            or ReminderKind.FridayEvening or ReminderKind.IdeOvertime => true,
        _ => false,
    };

    public static string DefaultsKey(this ReminderKind kind) =>
        $"reminder.{kind.ToString().ToLowerInvariant()}.enabled";

    public static ReminderKind[] All { get; } = Enum.GetValues<ReminderKind>();
    public static ReminderKind[] HealthCases { get; } =
        All.Where(k => !k.IsSystem() && !k.IsCompanion()).ToArray();
    public static ReminderKind[] SystemCases { get; } =
        All.Where(k => k.IsSystem()).ToArray();
    public static ReminderKind[] CompanionCases { get; } =
        All.Where(k => k.IsCompanion()).ToArray();
}

public interface IReminderCenterDelegate
{
    void ReminderFired(ReminderKind kind, string message);
    void SleepyChanged(bool sleepy);
    void HeatChanged(double level);
}

public sealed class ReminderCenter
{
    public IReminderCenterDelegate? Delegate { get; set; }

    private readonly ActivityMonitor _monitor = new();
    private readonly SystemMonitor _systemMonitor = new();
    private System.Windows.Threading.DispatcherTimer? _timer;

    private const double Tick = 30;
    private const double PresenceIdleLimit = 5 * 60;
    private const double SedentaryLimit = 60 * 60;
    private const double WaterInterval = 30 * 60;
    private const double SlackLimit = 30 * 60;
    private const double SlackRepeat = 15 * 60;
    private const double LateCareInterval = 30 * 60;
    private const double GlobalGap = 90;

    private const double CpuHighThreshold = 0.85;
    private const int CpuHighTicksNeeded = 3;
    private const double CpuRepeat = 10 * 60;
    private const double MemRepeat = 10 * 60;
    private const double DiskFreeThreshold = 0.10;
    private const double DiskRepeat = 6 * 3600;

    private double _activeSeconds;
    private double _waterSeconds;
    private DateTime _lastSlackFire = DateTime.MinValue;
    private DateTime _lastLateFire = DateTime.MinValue;
    private DateTime _lastAnyFire = DateTime.MinValue;
    public bool Sleepy { get; private set; }

    private int _cpuHighTicks;
    private DateTime _lastCpuFire = DateTime.MinValue;
    private DateTime _lastMemFire = DateTime.MinValue;
    private DateTime _lastDiskFire = DateTime.MinValue;
    private int _batteryStage;
    private int _pendingBatteryStage;
    private bool _batteryFullFired;
    private readonly HashSet<ReminderKind> _firedToday = [];
    private int _lastResetDay = DateTime.Now.Day;
    private double _ideWorkSeconds;
    private int _fridayFiredWeek;
    private readonly Dictionary<ReminderKind, string> _pendingDetail = new();

    private static readonly Dictionary<ReminderKind, string[]> Messages = new()
    {
        [ReminderKind.Sedentary] =
        [
            "坐了一个小时啦，起来动动嘛！",
            "屁股要生根啦～站起来伸个懒腰吧！",
            "陪我走两步？你都坐一小时了！",
            "久坐伤腰！起来倒杯水顺便活动一下～",
        ],
        [ReminderKind.Water] =
        [
            "咕噜咕噜～该喝水啦！",
            "你已经半小时没喝水了哦，抿一口嘛",
            "多喝水才有精神呀，去接杯水吧！",
            "我都替你渴了……喝口水好不好？",
        ],
        [ReminderKind.LateNight] =
        [
            "这么晚了还在忙呀，早点休息嘛……",
            "夜深了，工作再多也要爱惜自己哦",
            "我都困了 zzZ……你也快睡吧？",
            "熬夜会秃的！明天再做也来得及～",
            "夜深了，有我陪你呀，但也别熬太晚哦～",
            "再忙也有我在身边，加油之余记得休息～",
            "夜里静悄悄的，有我陪着你，忙完就去睡吧～",
        ],
        [ReminderKind.Slacking] =
        [
            "都摸鱼半小时了哦……我可什么都没看见",
            "视频好看吗？要不要考虑干点活？",
            "嘘——老板来了！（骗你的，但也该收心啦）",
            "摸鱼一时爽，一直摸鱼一直慌哦～",
        ],
        [ReminderKind.CpuHigh] =
        [
            "好烫好烫！CPU 都 {v} 了，是谁在偷偷挖矿！",
            "呼——风扇都要起飞啦，CPU 飙到 {v}！",
            "红温警告！我要被烤熟了……快看看哪个 App 在发疯！",
            "炫……烫烫烫！CPU {v}，快救救我！",
        ],
        [ReminderKind.MemoryPressure] =
        [
            "内存要被挤爆啦，关几个 App 让我喘口气～",
            "好挤呀……内存不够用了，清理一下嘛",
            "App 开太多啦，我都快被挤出屏幕了！",
        ],
        [ReminderKind.BatteryLow] =
        [
            "电量只剩 {v} 啦，快给我充电！",
            "肚子饿了……电池只有 {v} 了，插电插电！",
            "再不充电我们就要一起睡着了哦（{v}）",
        ],
        [ReminderKind.BatteryFull] =
        [
            "吃饱啦～电池充满了，可以拔线啦！",
            "满电出发！记得拔掉充电线哦～",
        ],
        [ReminderKind.DiskFull] =
        [
            "磁盘只剩 {v} 空位啦，该大扫除了！",
            "装不下啦……磁盘剩 {v}，删点东西嘛",
        ],
        [ReminderKind.MorningGreet] =
        [
            "早上好呀！今天也要元气满满哦～",
            "新的一天开始啦，冲鸭冲鸭！",
            "早安早安～今天想做点什么呀？",
            "太阳晒屁股啦，今天也请多指教！",
        ],
        [ReminderKind.LunchTime] =
        [
            "咕咕咕～到饭点啦，去吃点好的！",
            "十二点啦！干饭人干饭魂，走起走起～",
            "肚子是不是咕咕叫啦？该吃午饭咯！",
            "再忙也要好好吃饭呀，午饭时间到！",
        ],
        [ReminderKind.AfternoonCoffee] =
        [
            "下午三点啦，来杯咖啡提提神？",
            "下午茶时间到～来点咖啡或小点心嘛！",
            "有点困了吧？喝口咖啡满血复活！",
        ],
        [ReminderKind.FridayEvening] =
        [
            "周五啦！晚上要不要犒劳一下自己～",
            "耶！周五晚上到咯，尽情撒欢吧！",
            "辛苦一周啦，今晚好好放松一下嘛～",
            "周末近在眼前，先嗨一个再说！",
        ],
        [ReminderKind.IdeOvertime] =
        [
            "代码写两个小时啦，起来伸个懒腰嘛！",
            "敲了好久键盘啦，让眼睛休息一下下～",
            "连续奋战两小时！起来走走喝口水吧？",
            "卷了两个钟头啦……劳逸结合才高效哦！",
        ],
    };

    private static readonly Dictionary<ReminderKind, string> TestDetails = new()
    {
        [ReminderKind.CpuHigh] = "97%",
        [ReminderKind.BatteryLow] = "18%",
        [ReminderKind.DiskFull] = "9GB",
    };

    public string[] FrontEmotionTags =>
        _monitor.FrontCategory?.EmotionTags() ?? [];

    public bool IsEnabled(ReminderKind kind) =>
        UserSettings.ReminderEnabled(kind);

    public void SetEnabled(ReminderKind kind, bool enabled) =>
        UserSettings.SetReminderEnabled(kind, enabled);

    public void Start()
    {
        _timer?.Stop();
        _timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Tick),
        };
        _timer.Tick += (_, _) => TickNow();
        _timer.Start();
        _systemMonitor.PrimeCpuCounter();
        _ = _systemMonitor.CpuUsage();
        UpdateSleepy();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    public void Fire(ReminderKind kind)
    {
        switch (kind)
        {
            case ReminderKind.Sedentary: _activeSeconds = 0; break;
            case ReminderKind.Water: _waterSeconds = 0; break;
            case ReminderKind.Slacking: _lastSlackFire = DateTime.UtcNow; break;
            case ReminderKind.LateNight: _lastLateFire = DateTime.UtcNow; break;
            case ReminderKind.CpuHigh:
                _lastCpuFire = DateTime.UtcNow;
                _cpuHighTicks = 0;
                break;
            case ReminderKind.MemoryPressure: _lastMemFire = DateTime.UtcNow; break;
            case ReminderKind.BatteryLow:
                _batteryStage = Math.Max(_batteryStage, _pendingBatteryStage);
                break;
            case ReminderKind.BatteryFull: _batteryFullFired = true; break;
            case ReminderKind.DiskFull: _lastDiskFire = DateTime.UtcNow; break;
            case ReminderKind.MorningGreet:
            case ReminderKind.LunchTime:
            case ReminderKind.AfternoonCoffee:
                _firedToday.Add(kind);
                break;
            case ReminderKind.FridayEvening:
                _fridayFiredWeek = ISOWeek.GetWeekOfYear(DateTime.Now);
                break;
            case ReminderKind.IdeOvertime:
                _ideWorkSeconds = 0;
                break;
        }

        _lastAnyFire = DateTime.UtcNow;
        var pool = Messages.GetValueOrDefault(kind);
        var message = pool != null && pool.Length > 0
            ? pool[Random.Shared.Next(pool.Length)]
            : "";
        var detail = _pendingDetail.GetValueOrDefault(kind)
            ?? TestDetails.GetValueOrDefault(kind) ?? "";
        message = message.Replace("{v}", detail);
        Delegate?.ReminderFired(kind, message);
    }

    private void UpdateSleepy()
    {
        var hour = DateTime.Now.Hour;
        var night = hour >= 23 || hour < 5;
        if (night != Sleepy)
        {
            Sleepy = night;
            Delegate?.SleepyChanged(night);
        }
    }

    private void TickNow()
    {
        var today = DateTime.Now.Day;
        if (today != _lastResetDay)
        {
            _firedToday.Clear();
            _lastResetDay = today;
        }

        var idle = ActivityMonitor.IdleSeconds();
        var present = idle < PresenceIdleLimit;

        if (present)
        {
            _activeSeconds += Tick;
            _waterSeconds += Tick;
        }
        else
        {
            _activeSeconds = 0;
        }

        var cat = _monitor.FrontCategory;
        if (present && (cat == AppCategory.Ide || cat == AppCategory.Terminal))
            _ideWorkSeconds += Tick;
        if (!present) _ideWorkSeconds = 0;

        UpdateSleepy();

        var sysCandidates = SampleSystem();

        if ((DateTime.UtcNow - _lastAnyFire).TotalSeconds < GlobalGap) return;

        var candidates = new List<ReminderKind>(sysCandidates);
        if (IsEnabled(ReminderKind.Sedentary) && _activeSeconds >= SedentaryLimit)
            candidates.Add(ReminderKind.Sedentary);
        if (IsEnabled(ReminderKind.LateNight) && Sleepy && present &&
            (DateTime.UtcNow - _lastLateFire).TotalSeconds >= LateCareInterval)
            candidates.Add(ReminderKind.LateNight);
        if (IsEnabled(ReminderKind.Water) && _waterSeconds >= WaterInterval)
            candidates.Add(ReminderKind.Water);
        if (IsEnabled(ReminderKind.Slacking) &&
            _monitor.SlackingSeconds >= SlackLimit &&
            (DateTime.UtcNow - _lastSlackFire).TotalSeconds >= SlackRepeat)
            candidates.Add(ReminderKind.Slacking);

        var now = DateTime.Now;
        var hour = now.Hour;
        var weekday = (int)now.DayOfWeek;
        var weekOfYear = ISOWeek.GetWeekOfYear(now);
        // DayOfWeek: Sunday=0 … Friday=5
        if (IsEnabled(ReminderKind.MorningGreet) && present && hour is >= 6 and <= 10 &&
            !_firedToday.Contains(ReminderKind.MorningGreet))
            candidates.Add(ReminderKind.MorningGreet);
        if (IsEnabled(ReminderKind.LunchTime) && present && hour == 12 &&
            !_firedToday.Contains(ReminderKind.LunchTime))
            candidates.Add(ReminderKind.LunchTime);
        if (IsEnabled(ReminderKind.AfternoonCoffee) && present && hour == 15 &&
            !_firedToday.Contains(ReminderKind.AfternoonCoffee))
            candidates.Add(ReminderKind.AfternoonCoffee);
        if (IsEnabled(ReminderKind.FridayEvening) && present && weekday == 5 &&
            hour is >= 17 and <= 19 && _fridayFiredWeek != weekOfYear)
            candidates.Add(ReminderKind.FridayEvening);
        if (IsEnabled(ReminderKind.IdeOvertime) && _ideWorkSeconds >= 2 * 3600)
            candidates.Add(ReminderKind.IdeOvertime);

        if (candidates.Count > 0)
            Fire(candidates[0]);
    }

    private List<ReminderKind> SampleSystem()
    {
        var candidates = new List<ReminderKind>();

        var cpu = _systemMonitor.CpuUsage();
        var heat = Math.Clamp((cpu - 0.6) / 0.4, 0, 1);
        Delegate?.HeatChanged(IsEnabled(ReminderKind.CpuHigh) ? heat : 0);

        if (cpu >= CpuHighThreshold) _cpuHighTicks++;
        else _cpuHighTicks = 0;

        if (IsEnabled(ReminderKind.CpuHigh) &&
            _cpuHighTicks >= CpuHighTicksNeeded &&
            (DateTime.UtcNow - _lastCpuFire).TotalSeconds >= CpuRepeat)
        {
            _pendingDetail[ReminderKind.CpuHigh] = $"{(int)(cpu * 100)}%";
            candidates.Add(ReminderKind.CpuHigh);
        }

        _systemMonitor.SampleMemoryPressure();
        if (IsEnabled(ReminderKind.MemoryPressure) &&
            _systemMonitor.MemoryPressureCritical &&
            (DateTime.UtcNow - _lastMemFire).TotalSeconds >= MemRepeat)
            candidates.Add(ReminderKind.MemoryPressure);

        var battery = _systemMonitor.QueryBatteryState();
        if (battery != null)
        {
            if (battery.OnACPower)
            {
                _batteryStage = 0;
                if (IsEnabled(ReminderKind.BatteryFull) && battery.Percent >= 98 &&
                    !_batteryFullFired)
                    candidates.Add(ReminderKind.BatteryFull);
            }
            else
            {
                _batteryFullFired = false;
                if (battery.Percent > 25) _batteryStage = 0;
                _pendingBatteryStage = battery.Percent <= 10 ? 2 :
                    battery.Percent <= 20 ? 1 : 0;
                if (IsEnabled(ReminderKind.BatteryLow) &&
                    _pendingBatteryStage > _batteryStage)
                {
                    _pendingDetail[ReminderKind.BatteryLow] = $"{battery.Percent}%";
                    candidates.Add(ReminderKind.BatteryLow);
                }
            }
        }

        var disk = _systemMonitor.DiskFree();
        if (IsEnabled(ReminderKind.DiskFull) && disk != null &&
            disk.Value.ratio < DiskFreeThreshold &&
            (DateTime.UtcNow - _lastDiskFire).TotalSeconds >= DiskRepeat)
        {
            _pendingDetail[ReminderKind.DiskFull] = $"{disk.Value.freeGB:F0}GB";
            candidates.Add(ReminderKind.DiskFull);
        }

        return candidates;
    }
}
