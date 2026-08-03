using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace YierPet;

/// <summary>
/// Speech bubble in an owned window so it always stacks above the pet
/// (macOS uses NSWindow.addChildWindow for the same reason).
/// </summary>
public sealed class SpeechBubble
{
    private readonly Window _window;
    private readonly TextBlock _label;
    private readonly BubbleCanvas _bubble;
    private readonly Window _parent;
    private System.Windows.Threading.DispatcherTimer? _hideTimer;

    private const double MaxTextWidth = 220;
    private const double Padding = 10;
    /// <summary>Overlap into the pet cell so the tail sits near the head.</summary>
    private const double OverlapIntoPet = 88;

    public SpeechBubble(Window parent)
    {
        _parent = parent;
        _label = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            FontFamily = new FontFamily("Microsoft YaHei UI, PingFang SC, Segoe UI"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = MaxTextWidth,
        };

        _bubble = new BubbleCanvas();
        _bubble.Children.Add(_label);

        _window = new Window
        {
            Owner = parent,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            IsHitTestVisible = false,
            Opacity = 0,
            Content = _bubble,
        };
        _window.Show();
        _parent.LocationChanged += (_, _) => SyncPosition();
    }

    public void Say(string text, double durationSeconds = 4)
    {
        _label.Text = text;
        _label.Measure(new Size(MaxTextWidth, double.PositiveInfinity));
        var textSize = _label.DesiredSize;
        var w = Math.Max(textSize.Width + Padding * 2, 40);
        var h = textSize.Height + Padding * 2 + BubbleCanvas.TailHeight;

        _bubble.Width = w;
        _bubble.Height = h;
        _bubble.Measure(new Size(w, h));
        _bubble.Arrange(new Rect(0, 0, w, h));
        _bubble.InvalidateVisual();

        Canvas.SetLeft(_label, Padding);
        Canvas.SetTop(_label, BubbleCanvas.TailHeight + Padding);
        _label.Width = textSize.Width;

        _window.Width = w;
        _window.Height = h;
        SyncPosition(w, h);

        _hideTimer?.Stop();
        _window.BeginAnimation(UIElement.OpacityProperty, null);
        _window.Opacity = 0;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
        {
            FillBehavior = FillBehavior.HoldEnd,
        };
        _window.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        _window.Topmost = true;

        _hideTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(durationSeconds),
        };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
        _hideTimer.Start();
    }

    private void Hide()
    {
        _hideTimer?.Stop();
        _window.BeginAnimation(UIElement.OpacityProperty, null);
        var fade = new DoubleAnimation(_window.Opacity, 0, TimeSpan.FromMilliseconds(300))
        {
            FillBehavior = FillBehavior.HoldEnd,
        };
        _window.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void SyncPosition(double? width = null, double? height = null)
    {
        var w = width ?? _window.Width;
        var h = height ?? _window.Height;
        if (w <= 0 || h <= 0) return;

        // Match macOS: bubble bottom overlaps pet top by OverlapIntoPet (WPF Y-down).
        _window.Left = _parent.Left + (_parent.Width - w) / 2;
        _window.Top = _parent.Top + OverlapIntoPet - h;
    }
}

internal sealed class BubbleCanvas : Canvas
{
    public const double TailHeight = 8;

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth < 4 || ActualHeight < 4) return;

        var r = 10.0;
        var tailW = 14.0;
        var minX = 1.0;
        var maxX = ActualWidth - 1;
        var minY = TailHeight + 1;
        var maxY = ActualHeight - 1;
        var midX = ActualWidth / 2;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(midX - tailW / 2, minY), false, false);
            ctx.LineTo(new Point(midX, 1), true, false);
            ctx.LineTo(new Point(midX + tailW / 2, minY), true, false);
            ctx.LineTo(new Point(maxX - r, minY), true, false);
            ctx.ArcTo(new Point(maxX, minY + r), new Size(r, r), 0, false,
                SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(maxX, maxY - r), true, false);
            ctx.ArcTo(new Point(maxX - r, maxY), new Size(r, r), 0, false,
                SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(minX + r, maxY), true, false);
            ctx.ArcTo(new Point(minX, maxY - r), new Size(r, r), 0, false,
                SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(minX, minY + r), true, false);
            ctx.ArcTo(new Point(minX + r, minY), new Size(r, r), 0, false,
                SweepDirection.Clockwise, true, false);
            ctx.Close();
        }
        geo.Freeze();

        dc.DrawGeometry(
            new SolidColorBrush(Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF)),
            new Pen(new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xB8)), 1),
            geo);
    }
}
