using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace YierPet;

public sealed class PetController : IReminderCenterDelegate
{
    private readonly SpriteSheet _sheet;
    private readonly Window _window;
    private readonly Image _spriteImage;
    private readonly Rectangle _heatRect;
    private readonly SpeechBubble _bubble;
    private readonly ReminderCenter _reminderCenter = new();

    private PetState _state = PetState.Idle;
    private int _frameIndex;
    private DispatcherTimer? _frameTimer;

    private PetStyle _style = PetStyle.Classic;
    private PackLibrary? _packLibrary;
    private BitmapSource[] _stickerFrames = [];
    private double _stickerFps = 8;
    private int _stickerLoopsRemaining;
    private int _loopsRemaining;
    private DispatcherTimer? _behaviorTimer;
    private DispatcherTimer? _walkTimer;
    private double _walkDirection = 1;
    private bool _randomBehaviorEnabled = true;
    private bool _sleepy;

    private enum Mood { Normal, Angry, Happy, Love, Sad }
    private Mood _moodRaw = Mood.Normal;
    private DateTime _moodSetAt = DateTime.MinValue;
    private DateTime _lastInteraction = DateTime.UtcNow;
    private bool _wasAway;

    private DispatcherTimer? _physicsTimer;
    private double _throwVelX;
    private double _throwVelY;
    private double _maxImpactSpeed;
    private bool _isThrowing;

    private double _heatLevel;

    private bool _dragged;
    private readonly List<(double t, double dx, double dy)> _dragSamples = [];

    private const double PetWidth = 154;
    private const double PetHeight = 166;
    private const double ThrowSpeedThreshold = 550;
    private const double Gravity = 3000;
    private const double FloorBounce = 0.45;
    private const double WallBounce = 0.6;

    public PetController(SpriteSheet sheet)
    {
        _sheet = sheet;

        _spriteImage = new Image
        {
            Stretch = Stretch.Uniform,
            Width = PetWidth,
            Height = PetHeight,
        };

        _heatRect = new Rectangle
        {
            Fill = Brushes.Red,
            Opacity = 0,
            Width = PetWidth,
            Height = PetHeight,
            IsHitTestVisible = false,
        };
        _heatRect.OpacityMask = new ImageBrush { Stretch = Stretch.Uniform };

        var root = new Grid { Width = PetWidth, Height = PetHeight };
        root.Children.Add(_spriteImage);
        root.Children.Add(_heatRect);

        var area = Forms.Screen.PrimaryScreen!.WorkingArea;
        _window = new Window
        {
            Width = PetWidth,
            Height = PetHeight,
            Left = area.Left + (area.Width - PetWidth) / 2,
            Top = area.Bottom - PetHeight - 40,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            Content = root,
        };

        _window.MouseLeftButtonDown += OnMouseDown;
        _window.MouseMove += OnMouseMoveImpl;
        _window.MouseLeftButtonUp += OnMouseUp;
        _window.MouseRightButtonUp += OnRightClick;

        _window.Show();
        _bubble = new SpeechBubble(_window);
        _reminderCenter.Delegate = this;
        _reminderCenter.Start();

        ApplyStyle(UserSettings.SavedStyle);
        ScheduleRandomBehavior();
    }

    private Mood CurrentMood
    {
        get
        {
            var age = (DateTime.UtcNow - _moodSetAt).TotalSeconds;
            if (_moodRaw == Mood.Angry && age > 180 ||
                _moodRaw == Mood.Happy && age > 300 ||
                _moodRaw == Mood.Love && age > 600)
            {
                _moodRaw = Mood.Normal;
                return Mood.Normal;
            }
            return _moodRaw;
        }
    }

    private void SetMood(Mood m)
    {
        _moodRaw = m;
        _moodSetAt = DateTime.UtcNow;
    }

    private string[] ContextTags => CurrentMood switch
    {
        Mood.Angry => ["angry", "sad"],
        Mood.Sad => ["sad"],
        Mood.Love => ["love", "happy"],
        Mood.Happy => ["happy"],
        _ => _reminderCenter.FrontEmotionTags.Length > 0
            ? _reminderCenter.FrontEmotionTags
            : ["idle", "lazy", "happy"],
    };

    public void Say(string text, double duration = 4) => _bubble.Say(text, duration);

    public void HeatChanged(double level)
    {
        _heatLevel = Math.Clamp(level, 0, 1);
        _heatRect.Opacity = _heatLevel * 0.5;
    }

    public void SleepyChanged(bool sleepy) => _sleepy = sleepy;

    public void ReminderFired(ReminderKind kind, string message)
    {
        if (_isThrowing) return;
        StopWalk();
        Say(message, 6);

        if (_style.IsSticker())
        {
            HandleStickerReminder(kind);
            return;
        }

        switch (kind)
        {
            case ReminderKind.Sedentary:
                var area = WorkingArea();
                WalkTo(area.Left + (area.Width - PetWidth) / 2,
                    () => SetState(PetState.Jumping, 3));
                break;
            case ReminderKind.Water:
                PlayOnce(PetState.Waiting, 3);
                break;
            case ReminderKind.LateNight:
                PlayOnce(PetState.Review, 3);
                break;
            case ReminderKind.Slacking:
                PlayOnce(PetState.Review, 5);
                break;
            case ReminderKind.CpuHigh:
                HeatChanged(Math.Max(_heatLevel, 0.9));
                PlayOnce(PetState.Running, 3);
                break;
            case ReminderKind.MemoryPressure:
                PlayOnce(PetState.Waiting, 3);
                break;
            case ReminderKind.BatteryLow:
                PlayOnce(PetState.Failed, 2);
                break;
            case ReminderKind.BatteryFull:
                PlayOnce(PetState.Waving, 2);
                break;
            case ReminderKind.DiskFull:
                PlayOnce(PetState.Review, 3);
                break;
            case ReminderKind.MorningGreet:
                PlayOnce(PetState.Waving, 2);
                break;
            case ReminderKind.LunchTime:
                PlayOnce(PetState.Waiting, 2);
                break;
            case ReminderKind.AfternoonCoffee:
                PlayOnce(PetState.Review, 2);
                break;
            case ReminderKind.FridayEvening:
                PlayOnce(PetState.Jumping, 2);
                break;
            case ReminderKind.IdeOvertime:
                PlayOnce(PetState.Jumping, 3);
                break;
        }
    }

    private void HandleStickerReminder(ReminderKind kind)
    {
        if (kind == ReminderKind.CpuHigh)
            HeatChanged(Math.Max(_heatLevel, 0.9));

        var (tags, loops) = kind switch
        {
            ReminderKind.Sedentary or ReminderKind.Slacking => (new[] { "exercise", "idle" }, 3),
            ReminderKind.Water => (new[] { "eat", "idle" }, 3),
            ReminderKind.LateNight => (new[] { "love", "sleep", "lazy" }, 3),
            ReminderKind.CpuHigh => (new[] { "hot", "angry", "tired" }, 3),
            ReminderKind.MemoryPressure => (new[] { "tired", "sad" }, 3),
            ReminderKind.BatteryLow => (new[] { "eat", "sad" }, 2),
            ReminderKind.BatteryFull => (new[] { "happy" }, 2),
            ReminderKind.DiskFull => (new[] { "work", "tired" }, 3),
            ReminderKind.MorningGreet => (new[] { "happy", "love" }, 2),
            ReminderKind.LunchTime => (new[] { "eat", "happy" }, 2),
            ReminderKind.AfternoonCoffee => (new[] { "tired", "lazy" }, 2),
            ReminderKind.FridayEvening => (new[] { "happy", "exercise" }, 3),
            ReminderKind.IdeOvertime => (new[] { "exercise", "tired" }, 3),
            _ => (new[] { "idle" }, 2),
        };
        PlayStickerTags(tags, loops);
    }

    private void ShowFrame(BitmapSource frame)
    {
        _spriteImage.Source = frame;
        if (_heatRect.OpacityMask is ImageBrush brush)
            brush.ImageSource = frame;
    }

    private void SetState(PetState newState, int loops = 0)
    {
        if (_style.IsSticker()) return;
        _state = newState;
        _frameIndex = 0;
        _loopsRemaining = loops;
        StepFrame();
    }

    private void StepFrame()
    {
        _frameTimer?.Stop();
        var frames = _sheet.FramesFor(_state);
        if (frames.Length == 0) return;

        if (_frameIndex >= frames.Length)
        {
            _frameIndex = 0;
            if (_loopsRemaining > 0)
            {
                _loopsRemaining--;
                if (_loopsRemaining == 0)
                    _state = PetState.Idle;
            }
        }

        ShowFrame(frames[_frameIndex]);
        var ms = _state.DurationsMs()[_frameIndex];
        if (_sleepy && _state == PetState.Idle) ms = (int)(ms * 2.5);
        _frameIndex++;

        _frameTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ms),
        };
        _frameTimer.Tick += (_, _) => StepFrame();
        _frameTimer.Start();
    }

    private void PlayOnce(PetState action, int loops = 2)
    {
        StopWalk();
        SetState(action, loops);
    }

    private void ApplyStyle(PetStyle newStyle)
    {
        StopWalk();
        StopThrow();
        _frameTimer?.Stop();
        _stickerFrames = [];

        if (newStyle.IsSticker())
        {
            var lib = new PackLibrary(newStyle);
            if (lib.IsValid)
            {
                _style = newStyle;
                _packLibrary = lib;
                StickerIdle();
            }
            else
            {
                _style = PetStyle.Classic;
                _packLibrary = null;
                SetState(PetState.Idle);
            }
        }
        else
        {
            _style = PetStyle.Classic;
            _packLibrary = null;
            SetState(PetState.Idle);
        }

        UserSettings.SavedStyle = _style;
    }

    private void StickerIdle()
    {
        if (_packLibrary?.RandomStickerAnyOf(ContextTags) is { } sticker)
            PlaySticker(sticker, 0);
    }

    private void PlayStickerTags(string[] tags, int loops)
    {
        if (_packLibrary?.RandomStickerAnyOf(tags) is { } s)
            PlaySticker(s, loops);
    }

    private void PlaySticker(Sticker sticker, int loops)
    {
        var frames = sticker.LoadFrames();
        if (frames.Length == 0) return;
        _stickerFrames = frames;
        _stickerFps = sticker.Fps;
        _stickerLoopsRemaining = loops;
        _frameIndex = 0;
        StepStickerFrame();
    }

    private void StepStickerFrame()
    {
        _frameTimer?.Stop();
        if (_stickerFrames.Length == 0) return;

        if (_frameIndex >= _stickerFrames.Length)
        {
            _frameIndex = 0;
            if (_stickerLoopsRemaining > 0)
            {
                _stickerLoopsRemaining--;
                if (_stickerLoopsRemaining == 0)
                {
                    StickerIdle();
                    return;
                }
            }
        }

        ShowFrame(_stickerFrames[_frameIndex]);
        _frameIndex++;
        var interval = 1.0 / _stickerFps;
        if (_sleepy && _stickerLoopsRemaining == 0) interval *= 2;

        _frameTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(interval),
        };
        _frameTimer.Tick += (_, _) => StepStickerFrame();
        _frameTimer.Start();
    }

    private bool StickerBusy => _style.IsSticker() && _stickerLoopsRemaining > 0;

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragged = false;
        _dragSamples.Clear();
        _lastMouseScreen = _window.PointToScreen(e.GetPosition(_window));
        _window.CaptureMouse();
    }

    private Point _lastMouseScreen;

    private void OnMouseMoveImpl(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        _dragged = true;
        var screen = _window.PointToScreen(e.GetPosition(_window));
        if (_lastMouseScreen != default)
        {
            var dx = screen.X - _lastMouseScreen.X;
            var dy = screen.Y - _lastMouseScreen.Y;
            _window.Left += dx;
            _window.Top += dy;
            _dragSamples.Add((Environment.TickCount64 / 1000.0, dx, dy));
            if (_dragSamples.Count > 8) _dragSamples.RemoveAt(0);
            HandleDrag(dx);
        }
        _lastMouseScreen = screen;
    }

    // Fix mouse handlers - I'll rewrite OnMouseMove properly in patch

    private void HandleDrag(double dx)
    {
        StopWalk();
        StopThrow();
        if (_style.IsSticker()) return;
        var target = dx >= 0 ? PetState.RunningRight : PetState.RunningLeft;
        if (_state != target) SetState(target);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _window.ReleaseMouseCapture();
        if (_dragged)
            HandleDragEnd(ReleaseVelocity());
        else
            HandleClick();
        _dragged = false;
        _dragSamples.Clear();
        _lastMouseScreen = default;
    }

    private (double vx, double vy) ReleaseVelocity()
    {
        var now = Environment.TickCount64 / 1000.0;
        var recent = _dragSamples.Where(s => now - s.t < 0.12).ToList();
        if (recent.Count < 2) return (0, 0);
        var first = recent[0];
        var dt = now - first.t;
        if (dt < 0.005) return (0, 0);
        var dx = recent.Sum(s => s.dx);
        var dy = recent.Sum(s => s.dy);
        return (dx / dt, dy / dt);
    }

    private void HandleClick()
    {
        _lastInteraction = DateTime.UtcNow;
        switch (CurrentMood)
        {
            case Mood.Angry:
                if (_style.IsSticker()) PlayStickerTags(["angry"], 1);
                else PlayOnce(PetState.Failed);
                Say("哼！还在生气呢！");
                break;
            case Mood.Sad:
                SetMood(Mood.Happy);
                Say("你终于来陪我啦！");
                if (_style.IsSticker()) PlayStickerTags(["happy", "love"], 1);
                else PlayOnce(PetState.Waving);
                break;
            default:
                SetMood(Mood.Happy);
                if (_style.IsSticker()) PlayStickerTags(["happy", "love"], 1);
                else PlayOnce(PetState.Waving);
                break;
        }
    }

    private void HandleDragEnd((double vx, double vy) velocity)
    {
        var speed = Math.Sqrt(velocity.vx * velocity.vx + velocity.vy * velocity.vy);
        if (speed > ThrowSpeedThreshold)
            StartThrow(velocity.vx, velocity.vy);
        else if (_style.IsSticker())
        {
            _lastInteraction = DateTime.UtcNow;
            PlayStickerTags(["happy", "love"], 1);
        }
        else
        {
            _lastInteraction = DateTime.UtcNow;
            SetState(PetState.Jumping, 1);
        }
    }

    private void StartThrow(double vx, double vy)
    {
        StopWalk();
        if (!_style.IsSticker()) _frameTimer?.Stop();
        _isThrowing = true;
        _throwVelX = vx;
        _throwVelY = vy;
        _maxImpactSpeed = 0;
        _physicsTimer?.Stop();
        _physicsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.0 / 60.0),
        };
        _physicsTimer.Tick += (_, _) => StepPhysics();
        _physicsTimer.Start();
    }

    private void StepPhysics()
    {
        const double dt = 1.0 / 60.0;
        _throwVelY += Gravity * dt;
        var left = _window.Left + _throwVelX * dt;
        var top = _window.Top + _throwVelY * dt;

        var area = WorkingArea();
        var floor = area.Bottom - PetHeight;
        var landed = false;

        if (left < area.Left)
        {
            left = area.Left;
            _throwVelX = Math.Abs(_throwVelX) * WallBounce;
        }
        else if (left + PetWidth > area.Right)
        {
            left = area.Right - PetWidth;
            _throwVelX = -Math.Abs(_throwVelX) * WallBounce;
        }

        if (top < area.Top)
        {
            top = area.Top;
            _throwVelY = Math.Abs(_throwVelY) * WallBounce;
        }

        if (top >= floor)
        {
            top = floor;
            var impact = Math.Abs(_throwVelY);
            _maxImpactSpeed = Math.Max(_maxImpactSpeed, impact);
            _throwVelY = impact * FloorBounce;
            _throwVelX *= 0.8;
            if (_throwVelY < 220 && Math.Abs(_throwVelX) < 60)
                landed = true;
        }

        _window.Left = left;
        _window.Top = top;

        if (!_style.IsSticker())
        {
            var frames = _sheet.FramesFor(PetState.Jumping);
            if (frames.Length >= 5)
            {
                var idx = _throwVelY < -80 ? 1 : _throwVelY > 80 ? 3 : 2;
                ShowFrame(frames[idx]);
            }
        }

        if (landed) FinishThrow();
    }

    private void FinishThrow()
    {
        StopThrow();
        if (_maxImpactSpeed > 1600)
        {
            SetMood(Mood.Angry);
            Say("请轻拿轻放！");
            if (_style.IsSticker()) PlayStickerTags(["angry", "sad"], 2);
            else SetState(PetState.Failed, 1);
        }
        else if (_style.IsSticker())
            PlayStickerTags(["happy"], 1);
        else
            SetState(PetState.Jumping, 1);
    }

    private void StopThrow()
    {
        _physicsTimer?.Stop();
        _physicsTimer = null;
        _isThrowing = false;
    }

    private Forms.Rectangle WorkingArea()
    {
        var helper = new WindowInteropHelper(_window);
        if (helper.Handle != IntPtr.Zero)
            return Forms.Screen.FromHandle(helper.Handle).WorkingArea;
        return Forms.Screen.PrimaryScreen!.WorkingArea;
    }

    private void ScheduleRandomBehavior()
    {
        _behaviorTimer?.Stop();
        var delay = Random.Shared.NextDouble() * 8 + 7;
        _behaviorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(delay),
        };
        _behaviorTimer.Tick += (_, _) =>
        {
            _behaviorTimer?.Stop();
            PerformRandomBehavior();
            ScheduleRandomBehavior();
        };
        _behaviorTimer.Start();
    }

    private void PerformRandomBehavior()
    {
        if (!_randomBehaviorEnabled || _isThrowing) return;

        var idle = ActivityMonitor.IdleSeconds();
        if (idle >= 30 * 60)
        {
            _wasAway = true;
            return;
        }

        if (_wasAway)
        {
            _wasAway = false;
            _lastInteraction = DateTime.UtcNow;
            if (_style.IsSticker() && !StickerBusy)
            {
                PlayStickerTags(["love", "happy"], 2);
                Say("你回来啦！我好想你呀～");
                return;
            }
        }

        if ((DateTime.UtcNow - _lastInteraction).TotalSeconds >= 30 * 60 &&
            CurrentMood == Mood.Normal)
            SetMood(Mood.Sad);

        if (_style.IsSticker())
        {
            if (StickerBusy) return;
            switch (Random.Shared.Next(10))
            {
                case <= 2: StartWalk(); break;
                case <= 6: StickerIdle(); break;
            }
            return;
        }

        if (_state != PetState.Idle) return;
        switch (Random.Shared.Next(10))
        {
            case <= 3: StartWalk(); break;
            case 4: PlayOnce(PetState.Waving); break;
            case 5: PlayOnce(PetState.Jumping); break;
            case 6: PlayOnce(PetState.Waiting); break;
            case 7: PlayOnce(PetState.Review); break;
            case 8: PlayOnce(PetState.Running); break;
        }
    }

    private void StartWalk()
    {
        StopWalk();
        _walkDirection = Random.Shared.Next(2) == 0 ? -1 : 1;
        if (!_style.IsSticker())
            SetState(_walkDirection > 0 ? PetState.RunningRight : PetState.RunningLeft);

        var deadline = DateTime.UtcNow.AddSeconds(Random.Shared.NextDouble() * 2 + 2);
        _walkTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.0 / 60.0),
        };
        _walkTimer.Tick += (_, _) =>
        {
            var area = WorkingArea();
            var left = _window.Left + _walkDirection * 1.4;
            if (left < area.Left)
            {
                left = area.Left;
                _walkDirection = 1;
                if (!_style.IsSticker()) SetState(PetState.RunningRight);
            }
            else if (left + PetWidth > area.Right)
            {
                left = area.Right - PetWidth;
                _walkDirection = -1;
                if (!_style.IsSticker()) SetState(PetState.RunningLeft);
            }
            _window.Left = left;
            if (DateTime.UtcNow >= deadline)
            {
                StopWalk();
                if (!_style.IsSticker()) SetState(PetState.Idle);
            }
        };
        _walkTimer.Start();
    }

    private void StopWalk()
    {
        _walkTimer?.Stop();
        _walkTimer = null;
    }

    private void WalkTo(double targetX, Action completion)
    {
        StopWalk();
        if (Math.Abs(targetX - _window.Left) < 8)
        {
            completion();
            return;
        }

        _walkDirection = targetX > _window.Left ? 1 : -1;
        if (!_style.IsSticker())
            SetState(_walkDirection > 0 ? PetState.RunningRight : PetState.RunningLeft);

        _walkTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.0 / 60.0),
        };
        _walkTimer.Tick += (_, _) =>
        {
            var left = _window.Left + _walkDirection * 2.2;
            var arrived = (_walkDirection > 0 && left >= targetX) ||
                (_walkDirection < 0 && left <= targetX);
            if (arrived) left = targetX;
            _window.Left = left;
            if (arrived)
            {
                StopWalk();
                completion();
            }
        };
        _walkTimer.Start();
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        var menu = BuildMenu();
        menu.PlacementTarget = _window;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var styleMenu = new MenuItem { Header = "形象" };
        foreach (var s in PetStyleExtensions.All)
        {
            var item = new MenuItem
            {
                Header = s.DisplayName(),
                IsCheckable = true,
                IsChecked = s == _style,
            };
            var captured = s;
            item.Click += (_, _) => ApplyStyle(captured);
            styleMenu.Items.Add(item);
        }
        menu.Items.Add(styleMenu);

        if (!_style.IsSticker())
        {
            var stateMenu = new MenuItem { Header = "动作" };
            foreach (var st in PetStateExtensions.All)
            {
                var item = new MenuItem
                {
                    Header = st.DisplayName(),
                    IsCheckable = true,
                    IsChecked = st == _state,
                };
                var captured = st;
                item.Click += (_, _) =>
                {
                    StopWalk();
                    if (captured is PetState.Idle or PetState.RunningRight
                        or PetState.RunningLeft)
                        SetState(captured);
                    else
                        SetState(captured, 3);
                };
                stateMenu.Items.Add(item);
            }
            menu.Items.Add(stateMenu);
        }

        var randomItem = new MenuItem
        {
            Header = _randomBehaviorEnabled ? "暂停随机行为" : "开启随机行为",
        };
        randomItem.Click += (_, _) => _randomBehaviorEnabled = !_randomBehaviorEnabled;
        menu.Items.Add(randomItem);

        menu.Items.Add(BuildReminderSubmenu("提醒设置", ReminderKindExtensions.HealthCases));
        menu.Items.Add(BuildReminderSubmenu("系统哨兵", ReminderKindExtensions.SystemCases));
        menu.Items.Add(BuildReminderSubmenu("陪伴模式", ReminderKindExtensions.CompanionCases));

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            var testMenu = new MenuItem { Header = "测试提醒" };
            foreach (var kind in ReminderKindExtensions.All)
            {
                var item = new MenuItem { Header = "触发" + kind.Title() };
                var captured = kind;
                item.Click += (_, _) => _reminderCenter.Fire(captured);
                testMenu.Items.Add(item);
            }
            menu.Items.Add(testMenu);
        }

        menu.Items.Add(new Separator());
        var quit = new MenuItem { Header = "退出" };
        quit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(quit);

        return menu;
    }

    private MenuItem BuildReminderSubmenu(string title, ReminderKind[] kinds)
    {
        var root = new MenuItem { Header = title };
        foreach (var kind in kinds)
        {
            var item = new MenuItem
            {
                Header = kind.Title(),
                IsCheckable = true,
                IsChecked = _reminderCenter.IsEnabled(kind),
            };
            var captured = kind;
            item.Click += (_, _) =>
                _reminderCenter.SetEnabled(captured, !_reminderCenter.IsEnabled(captured));
            root.Items.Add(item);
        }
        return root;
    }
}
