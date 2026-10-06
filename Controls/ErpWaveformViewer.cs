using System.Drawing.Drawing2D;
using XOnERPStudio.Models;

namespace XOnERPStudio.Controls;

/// <summary>
/// ERP dalga formu karşılaştırma grafiği: Hedef (kırmızı, ±SEM), Standart (mavi, ±SEM) ve
/// Fark dalgası (yeşil). P300/N200/N100 tepe etiketleri, bileşen pencereleri ve fare imleci okuması.
/// </summary>
public class ErpWaveformViewer : PlotControlBase
{
    private ChannelErp _erp;
    private ErpFilterSettings _settings = new();
    private int _cursorX = -1;

    public bool ShowTarget { get; set; } = true;
    public bool ShowStandard { get; set; } = true;
    public bool ShowDifference { get; set; } = true;
    public bool ShowSem { get; set; } = true;
    public bool ShowPeaks { get; set; } = true;
    public bool ShowWindows { get; set; } = true;
    public bool NegativeUp { get; set; }

    /// <summary>Sabit Y aralığı (±µV); 0 = otomatik.</summary>
    public double FixedRangeUv { get; set; }

    public string Title { get; set; }

    private const int MarginL = 56, MarginR = 16, MarginT = 34, MarginB = 34;

    public void SetData(ChannelErp erp, ErpFilterSettings settings)
    {
        _erp = erp;
        if (settings != null) { _settings = settings; NegativeUp = settings.NegativeUp; }
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _cursorX = e.X;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _cursorX = -1;
        Invalidate();
    }

    private IEnumerable<(ErpWaveform w, Color c, bool sem)> Series()
    {
        if (_erp == null) yield break;
        if (ShowStandard && _erp.Standard is { IsEmpty: false }) yield return (_erp.Standard, Theme.StandardColor, ShowSem);
        if (ShowTarget && _erp.Target is { IsEmpty: false }) yield return (_erp.Target, Theme.TargetColor, ShowSem);
        if (ShowDifference && _erp.Difference is { IsEmpty: false }) yield return (_erp.Difference, Theme.DifferenceColor, false);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var plot = new RectangleF(MarginL, MarginT, Width - MarginL - MarginR, Height - MarginT - MarginB);
        if (plot.Width < 40 || plot.Height < 40) return;

        using var muted = new SolidBrush(Theme.TextMuted);
        using var text = new SolidBrush(Theme.Text);
        using var axisFont = Theme.UiFont(8f);
        using var titleFont = Theme.UiFont(10.5f, FontStyle.Bold);

        var series = Series().ToList();
        string title = Title ?? (_erp != null ? $"{_erp.ChannelName} — ERP" : "ERP");
        g.DrawString(title, titleFont, text, MarginL, 8);

        if (series.Count == 0)
        {
            using var f = Theme.UiFont(10f);
            const string msg = "ERP verisi yok — önce analizi çalıştırın.";
            var sz = g.MeasureString(msg, f);
            g.DrawString(msg, f, muted, plot.X + (plot.Width - sz.Width) / 2, plot.Y + (plot.Height - sz.Height) / 2);
            return;
        }

        double tMin = _settings.EpochStartMs, tMax = _settings.EpochEndMs;
        double range = FixedRangeUv > 0 ? FixedRangeUv : AutoRange(series);
        float X(double ms) => plot.X + (float)((ms - tMin) / (tMax - tMin) * plot.Width);
        float Y(double uv) => plot.Y + plot.Height / 2 - (float)((NegativeUp ? -uv : uv) / range * plot.Height / 2);

        // Bileşen pencereleri
        if (ShowWindows)
        {
            ShadeWindow(g, plot, X, _settings.P300StartMs, _settings.P300EndMs, Theme.PeakColor, "P300");
            ShadeWindow(g, plot, X, _settings.N200StartMs, _settings.N200EndMs, Theme.PeakNegColor, "N200");
        }

        // Izgara
        using (var gridPen = new Pen(Theme.Grid))
        {
            double ystep = Theme.NiceStep(range * 2, 8);
            for (double v = -Math.Floor(range / ystep) * ystep; v <= range + 1e-9; v += ystep)
            {
                float y = Y(v);
                g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
                string s = v.ToString("0.#");
                var sz = g.MeasureString(s, axisFont);
                g.DrawString(s, axisFont, muted, plot.Left - sz.Width - 4, y - sz.Height / 2);
            }
            double xstep = Theme.NiceStep(tMax - tMin, 10);
            for (double t = Math.Ceiling(tMin / xstep) * xstep; t <= tMax + 1e-9; t += xstep)
            {
                float x = X(t);
                g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
                string s = t.ToString("0");
                var sz = g.MeasureString(s, axisFont);
                g.DrawString(s, axisFont, muted, x - sz.Width / 2, plot.Bottom + 4);
            }
        }
        g.DrawString("ms", axisFont, muted, plot.Right - 16, plot.Bottom + 16);
        g.DrawString(NegativeUp ? "µV (− yukarı)" : "µV", axisFont, muted, 4, MarginT - 16);

        // Eksenler: 0 µV ve uyaran başlangıcı
        using (var axisPen = new Pen(Theme.GridStrong, 1.2f))
            g.DrawLine(axisPen, plot.Left, Y(0), plot.Right, Y(0));
        using (var onsetPen = new Pen(Theme.Text, 2f))
            g.DrawLine(onsetPen, X(0), plot.Top, X(0), plot.Bottom);

        // Seriler
        var clip = g.Clip;
        g.SetClip(plot);
        foreach (var (w, c, sem) in series)
        {
            if (sem) DrawSemBand(g, w, X, Y, c);
            var pts = w.TimesMs.Select((t, i) => new PointF(X(t), Y(w.Mean[i]))).ToArray();
            using var pen = new Pen(c, w == _erp.Difference ? 1.6f : 2f);
            if (w == _erp.Difference) pen.DashStyle = DashStyle.Dash;
            g.DrawLines(pen, pts);
        }
        g.Clip = clip;

        // Tepe etiketleri
        if (ShowPeaks)
        {
            if (ShowTarget && _erp.Target is { IsEmpty: false })
            {
                DrawPeak(g, _erp.Target.P300, X, Y, Theme.PeakColor, true);
                DrawPeak(g, _erp.Target.N200, X, Y, Theme.PeakNegColor, false);
                DrawPeak(g, _erp.Target.N100, X, Y, Theme.PeakNegColor, false);
            }
            if (ShowDifference && _erp.Difference is { IsEmpty: false })
                DrawPeak(g, _erp.Difference.P300, X, Y, Theme.DifferenceColor, true, "Fark ");
        }

        DrawLegend(g, plot, series);
        DrawCursor(g, plot, series, X, Y, tMin, tMax);

        using (var borderPen = new Pen(Theme.Border))
            g.DrawRectangle(borderPen, plot.X, plot.Y, plot.Width, plot.Height);
    }

    private static double AutoRange(List<(ErpWaveform w, Color c, bool sem)> series)
    {
        double m = 1;
        foreach (var (w, _, sem) in series)
            for (int i = 0; i < w.Mean.Length; i++)
                m = Math.Max(m, Math.Abs(w.Mean[i]) + (sem ? w.Sem[i] : 0));
        double step = Theme.NiceStep(m * 1.15, 4);
        return Math.Ceiling(m * 1.15 / step) * step;
    }

    private static void ShadeWindow(Graphics g, RectangleF plot, Func<double, float> X, double a, double b, Color c, string label)
    {
        float x0 = Math.Max(plot.Left, X(a)), x1 = Math.Min(plot.Right, X(b));
        if (x1 <= x0) return;
        using var br = new SolidBrush(Theme.WithAlpha(c, 18));
        g.FillRectangle(br, x0, plot.Top, x1 - x0, plot.Height);
        using var f = Theme.UiFont(7.5f);
        using var tb = new SolidBrush(Theme.WithAlpha(c, 150));
        g.DrawString(label, f, tb, x0 + 2, plot.Top + 2);
    }

    private static void DrawSemBand(Graphics g, ErpWaveform w, Func<double, float> X, Func<double, float> Y, Color c)
    {
        int n = w.Mean.Length;
        var poly = new PointF[n * 2];
        for (int i = 0; i < n; i++)
        {
            poly[i] = new PointF(X(w.TimesMs[i]), Y(w.Mean[i] + w.Sem[i]));
            poly[2 * n - 1 - i] = new PointF(X(w.TimesMs[i]), Y(w.Mean[i] - w.Sem[i]));
        }
        using var br = new SolidBrush(Theme.WithAlpha(c, 45));
        g.FillPolygon(br, poly);
    }

    private static void DrawPeak(Graphics g, ComponentPeak p, Func<double, float> X, Func<double, float> Y, Color c, bool above, string prefix = "")
    {
        if (p == null || !p.Found) return;
        float x = X(p.LatencyMs), y = Y(p.AmplitudeUv);
        using var br = new SolidBrush(c);
        using var pen = new Pen(c, 1.5f);
        g.FillEllipse(br, x - 4, y - 4, 8, 8);
        string label = $"{prefix}{p.Name}: {p.LatencyMs:0} ms, {p.AmplitudeUv:0.0} µV" + (p.AtWindowEdge ? " (kenar)" : "");
        using var f = Theme.UiFont(8f, FontStyle.Bold);
        var sz = g.MeasureString(label, f);
        float ly = above ? y - sz.Height - 10 : y + 8;
        var r = new RectangleF(x - sz.Width / 2, ly, sz.Width, sz.Height);
        using var bg = new SolidBrush(Theme.WithAlpha(Theme.Background, 210));
        g.FillRectangle(bg, r);
        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        g.DrawString(label, f, br, r.X, r.Y);
    }

    private void DrawLegend(Graphics g, RectangleF plot, List<(ErpWaveform w, Color c, bool sem)> series)
    {
        using var f = Theme.UiFont(8.5f);
        float x = plot.Right - 8, y = 10;
        foreach (var (w, c, _) in Enumerable.Reverse(series))
        {
            string s = $"{w.ConditionName} (n={w.EpochCount})";
            var sz = g.MeasureString(s, f);
            x -= sz.Width + 26;
            using var pen = new Pen(c, 2.5f);
            g.DrawLine(pen, x, y + sz.Height / 2, x + 16, y + sz.Height / 2);
            using var b = new SolidBrush(Theme.Text);
            g.DrawString(s, f, b, x + 20, y);
        }
    }

    private void DrawCursor(Graphics g, RectangleF plot, List<(ErpWaveform w, Color c, bool sem)> series,
        Func<double, float> X, Func<double, float> Y, double tMin, double tMax)
    {
        if (_cursorX < plot.Left || _cursorX > plot.Right) return;
        double ms = tMin + (_cursorX - plot.Left) / plot.Width * (tMax - tMin);
        using var pen = new Pen(Theme.WithAlpha(Theme.Text, 90)) { DashStyle = DashStyle.Dot };
        g.DrawLine(pen, _cursorX, plot.Top, _cursorX, plot.Bottom);

        var lines = new List<(string, Color)> { ($"{ms:0} ms", Theme.Text) };
        foreach (var (w, c, _) in series)
        {
            int i = w.IndexOfTime(ms);
            if (i < 0) continue;
            lines.Add(($"{w.ConditionName}: {w.Mean[i]:0.00} µV", c));
            using var b = new SolidBrush(c);
            float y = Y(w.Mean[i]);
            g.FillEllipse(b, _cursorX - 3, y - 3, 6, 6);
        }
        using var f = Theme.UiFont(8f);
        float lh = f.Height + 1, wMax = lines.Max(l => g.MeasureString(l.Item1, f).Width);
        float bx = _cursorX + 10;
        if (bx + wMax + 10 > plot.Right) bx = _cursorX - wMax - 18;
        var box = new RectangleF(bx, plot.Top + 6, wMax + 10, lh * lines.Count + 6);
        using var bg = new SolidBrush(Theme.WithAlpha(Theme.Surface, 230));
        g.FillRectangle(bg, box);
        for (int k = 0; k < lines.Count; k++)
        {
            using var b = new SolidBrush(lines[k].Item2);
            g.DrawString(lines[k].Item1, f, b, box.X + 5, box.Y + 3 + k * lh);
        }
    }
}
