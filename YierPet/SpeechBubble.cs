using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace YierPet;

public sealed class SpeechBubble
{
    private readonly Window _window;
    private readonly TextBlock _label;
    private readonly BubbleCanvas _bubble;
    private readonly Window _parent;
    private System.Windows.Threading.DispatcherTimer? _hideTimer;

    private const double MaxTextWidth = 220;
    private const double Padding = 10;
    private const double OverlapIntoPet = 88;

    public SpeechBubble(Window parent)
    {
        _parent = parent;
        _label = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = MaxTextWidth,
        };

        _bubble = new BubbleCanvas();
        _bubble.Children.Add(_label);

        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            IsHitTestVisible = false,
            Opacity = 0,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = _bubble,
        };
        _window.Show();
        SyncPosition();
        _parent.LocationChanged += (_, _) => SyncPosition();
    }

    public void Say(string text, double durationSeconds = 4)
    {
        _label.Text = text;
        _label.Measure(new Size(MaxTextWidth, double.PositiveInfinity));
        var textSize = _label.DesiredSize;
        var w = textSize.Width + Padding * 2;
        var h = textSize.Height + Padding * 2 + BubbleCanvas.TailHeight;

        _bubble.Width = w;
        _bubble.Height = h;
        Canvas.SetLeft(_label, Padding);
        Canvas.SetTop(_label, BubbleCanvas.TailHeight + Padding);
        _label.Width = textSize.Width;

        SyncPosition(w, h);

        _hideTimer?.Stop();
        var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200));
        _window.BeginAnimation(UIElement.OpacityProperty, fadeIn);

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
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
        _window.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void SyncPosition(double? width = null, double? height = null)
    {
        var pf = _parent;
        var w = width ?? _bubble.Width;
        var h = height ?? _bubble.Height;
        if (w <= 0 || h <= 0) return;
        _window.Left = pf.Left + (pf.Width - w) / 2;
        _window.Top = pf.Top - h + OverlapIntoPet;
    }
}

internal sealed class BubbleCanvas : Canvas
{
    public const double TailHeight = 8;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
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
