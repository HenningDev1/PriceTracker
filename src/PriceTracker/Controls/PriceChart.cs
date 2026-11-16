using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using PriceTracker.Models;
using PriceTracker.Services;

namespace PriceTracker.Controls;

/// Lightweight step-line chart for two price series with a hover read-out.
public sealed class PriceChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<PricePoint>?> PointsProperty =
        AvaloniaProperty.Register<PriceChart, IReadOnlyList<PricePoint>?>(nameof(Points));
    public static readonly StyledProperty<string> SeriesANameProperty =
        AvaloniaProperty.Register<PriceChart, string>(nameof(SeriesAName), "A");
    public static readonly StyledProperty<string> SeriesBNameProperty =
        AvaloniaProperty.Register<PriceChart, string>(nameof(SeriesBName), "B");
    public static readonly StyledProperty<IBrush?> SeriesABrushProperty =
        AvaloniaProperty.Register<PriceChart, IBrush?>(nameof(SeriesABrush));
    public static readonly StyledProperty<IBrush?> SeriesBBrushProperty =
        AvaloniaProperty.Register<PriceChart, IBrush?>(nameof(SeriesBBrush));
    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<PriceChart, IBrush?>(nameof(GridBrush));
    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<PriceChart, IBrush?>(nameof(LabelBrush));
    public static readonly StyledProperty<IBrush?> TooltipBackgroundProperty =
        AvaloniaProperty.Register<PriceChart, IBrush?>(nameof(TooltipBackground));
    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner<PriceChart>();

    public IReadOnlyList<PricePoint>? Points { get => GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public string SeriesAName { get => GetValue(SeriesANameProperty); set => SetValue(SeriesANameProperty, value); }
    public string SeriesBName { get => GetValue(SeriesBNameProperty); set => SetValue(SeriesBNameProperty, value); }
    public IBrush? SeriesABrush { get => GetValue(SeriesABrushProperty); set => SetValue(SeriesABrushProperty, value); }
    public IBrush? SeriesBBrush { get => GetValue(SeriesBBrushProperty); set => SetValue(SeriesBBrushProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush? LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public IBrush? TooltipBackground { get => GetValue(TooltipBackgroundProperty); set => SetValue(TooltipBackgroundProperty, value); }
    public FontFamily FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    private Point? _hover;

    static PriceChart()
    {
        AffectsRender<PriceChart>(PointsProperty, SeriesABrushProperty, SeriesBBrushProperty, GridBrushProperty,
            LabelBrushProperty, FontFamilyProperty, TooltipBackgroundProperty);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _hover = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    private const double Left = 64, Right = 16, Top = 30, Bottom = 26;

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width; var h = Bounds.Height;
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));
        var label = LabelBrush ?? Brushes.Gray;
        var grid = GridBrush ?? new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));
        var typeface = new Typeface(FontFamily ?? FontFamily.Default);
        var brushA = SeriesABrush ?? Brushes.DodgerBlue;
        var brushB = SeriesBBrush ?? Brushes.Orange;

        var pts = (Points ?? Array.Empty<PricePoint>()).OrderBy(p => p.Time).ToList();
        var values = pts.SelectMany(p => new[] { p.A, p.B }).Where(v => v.HasValue).Select(v => (double)v!.Value).ToList();

        // Legend
        double lx = Left;
        DrawLegend(ctx, ref lx, 8, brushA, SeriesAName, typeface, label);
        if (pts.Any(p => p.B.HasValue)) DrawLegend(ctx, ref lx, 8, brushB, SeriesBName, typeface, label);

        if (values.Count == 0 || w < Left + Right + 20 || h < Top + Bottom + 20)
        {
            var t = Text("No price history yet — it fills in with every update.", typeface, 12, label);
            ctx.DrawText(t, new Point((w - t.Width) / 2, (h - t.Height) / 2));
            return;
        }

        double min = values.Min(), max = values.Max();
        if (max - min < 0.01) { min = Math.Max(0, min * 0.9); max = max * 1.1 + 1; }
        var pad = (max - min) * 0.12;
        min = Math.Max(0, min - pad); max += pad;
        var (niceMin, niceMax, step) = Nice(min, max, 4);
        min = niceMin; max = niceMax;

        var t0 = pts[0].Time;
        var t1 = DateTime.Now > pts[^1].Time ? DateTime.Now : pts[^1].Time;
        if ((t1 - t0).TotalHours < 24) t0 = t1.AddDays(-1);
        double span = (t1 - t0).TotalSeconds;

        var plot = new Rect(Left, Top, w - Left - Right, h - Top - Bottom);
        double X(DateTime t) => plot.Left + (t - t0).TotalSeconds / span * plot.Width;
        double Y(double v) => plot.Bottom - (v - min) / (max - min) * plot.Height;

        // Grid + y labels
        var gridPen = new Pen(grid, 1, dashStyle: new DashStyle(new double[] { 2, 4 }, 0));
        for (double v = min; v <= max + step / 2; v += step)
        {
            var y = Math.Round(Y(v)) + 0.5;
            ctx.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var t = Text(v.ToString(step < 1 ? "0.00" : "0", Format.Euro) + " €", typeface, 11, label);
            ctx.DrawText(t, new Point(plot.Left - t.Width - 8, y - t.Height / 2));
        }

        // X labels (3–5 dates)
        int ticks = Math.Clamp((int)(plot.Width / 110), 2, 6);
        for (int i = 0; i <= ticks; i++)
        {
            var time = t0.AddSeconds(span * i / ticks);
            var t = Text(time.ToString(span > 86400 * 300 ? "MMM yy" : "d MMM", CultureInfo.GetCultureInfo("en-GB")), typeface, 11, label);
            var x = X(time) - t.Width / 2;
            x = Math.Clamp(x, plot.Left - 4, plot.Right - t.Width + 4);
            ctx.DrawText(t, new Point(x, plot.Bottom + 7));
        }

        DrawSeries(ctx, pts, p => p.A, brushA, X, Y, t1, plot);
        DrawSeries(ctx, pts, p => p.B, brushB, X, Y, t1, plot);

        // Hover read-out
        if (_hover is { } hp && plot.Contains(hp))
        {
            var time = t0.AddSeconds((hp.X - plot.Left) / plot.Width * span);
            var at = pts.LastOrDefault(p => p.Time <= time) ?? pts[0];
            ctx.DrawLine(new Pen(label, 1), new Point(hp.X, plot.Top), new Point(hp.X, plot.Bottom));
            var lines = new List<(string, IBrush)> { (time.ToString("ddd, d MMM yyyy", CultureInfo.GetCultureInfo("en-GB")), label) };
            if (at.A.HasValue) lines.Add(($"{SeriesAName}: {Format.Money(at.A)}", brushA));
            if (at.B.HasValue) lines.Add(($"{SeriesBName}: {Format.Money(at.B)}", brushB));
            var texts = lines.Select(l => Text(l.Item1, typeface, 12, l.Item2, l.Item2 != label)).ToList();
            var bw = texts.Max(t => t.Width) + 20; var bh = texts.Sum(t => t.Height + 2) + 14;
            var bx = hp.X + 12 + bw > plot.Right ? hp.X - 12 - bw : hp.X + 12;
            var by = Math.Clamp(hp.Y - bh / 2, plot.Top, plot.Bottom - bh);
            ctx.DrawRectangle(TooltipBackground ?? Brushes.White, new Pen(grid, 1), new RoundedRect(new Rect(bx, by, bw, bh), 8));
            double ty = by + 7;
            foreach (var t in texts) { ctx.DrawText(t, new Point(bx + 10, ty)); ty += t.Height + 2; }
        }
    }

    private static void DrawSeries(DrawingContext ctx, List<PricePoint> pts, Func<PricePoint, decimal?> sel, IBrush brush,
        Func<DateTime, double> X, Func<double, double> Y, DateTime end, Rect plot)
    {
        var s = pts.Where(p => sel(p).HasValue).ToList();
        if (s.Count == 0) return;
        var geo = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var g = geo.Open())
        using (var f = fill.Open())
        {
            var start = new Point(X(s[0].Time), Y((double)sel(s[0])!.Value));
            g.BeginFigure(start, false);
            f.BeginFigure(new Point(start.X, plot.Bottom), true);
            f.LineTo(start);
            var prev = start;
            for (int i = 1; i < s.Count; i++)
            {
                var p = new Point(X(s[i].Time), Y((double)sel(s[i])!.Value));
                g.LineTo(new Point(p.X, prev.Y)); f.LineTo(new Point(p.X, prev.Y));   // step: price holds until it changes
                g.LineTo(p); f.LineTo(p);
                prev = p;
            }
            var last = new Point(X(end), prev.Y);
            g.LineTo(last); f.LineTo(last);
            f.LineTo(new Point(last.X, plot.Bottom));
            g.EndFigure(false);
            f.EndFigure(true);
        }
        var color = (brush as ISolidColorBrush)?.Color ?? Colors.Gray;
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(70, color.R, color.G, color.B), 0), new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1) },
        };
        ctx.DrawGeometry(gradient, null, fill);
        ctx.DrawGeometry(null, new Pen(brush, 2.2, lineJoin: PenLineJoin.Round, lineCap: PenLineCap.Round), geo);
        // Dots only where the price changed (and at both ends) keep the line readable.
        for (int i = 0; i < s.Count; i++)
        {
            bool changed = i == 0 || i == s.Count - 1 || sel(s[i]) != sel(s[i - 1]);
            if (changed) ctx.DrawEllipse(brush, null, new Point(X(s[i].Time), Y((double)sel(s[i])!.Value)), 3, 3);
        }
    }

    private static void DrawLegend(DrawingContext ctx, ref double x, double y, IBrush brush, string name, Typeface tf, IBrush label)
    {
        ctx.DrawEllipse(brush, null, new Point(x + 5, y + 8), 4.5, 4.5);
        var t = Text(name, tf, 12, label);
        ctx.DrawText(t, new Point(x + 14, y + 8 - t.Height / 2));
        x += 14 + t.Width + 18;
    }

    private static FormattedText Text(string s, Typeface tf, double size, IBrush brush, bool bold = false) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            bold ? new Typeface(tf.FontFamily, weight: FontWeight.SemiBold) : tf, size, brush);

    private static (double min, double max, double step) Nice(double min, double max, int ticks)
    {
        var range = max - min;
        var rough = range / ticks;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var norm = rough / mag;
        var step = (norm < 1.5 ? 1 : norm < 3 ? 2 : norm < 7 ? 5 : 10) * mag;
        return (Math.Floor(min / step) * step, Math.Ceiling(max / step) * step, step);
    }
}
