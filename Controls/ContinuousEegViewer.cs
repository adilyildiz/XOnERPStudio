using System.Drawing.Drawing2D;
using XOnERPStudio.Models;

namespace XOnERPStudio.Controls;

/// <summary>
/// 7 (+AUX) kanallı sürekli EEG osiloskopu. Kanallar dikey bantlara ayrılır; marker anları renkli
/// kesikli çizgilerle işaretlenir. Fare sürükleme = kaydırma, tekerlek = zaman penceresi,
/// Ctrl+tekerlek = genlik ölçeği.
/// </summary>
public class ContinuousEegViewer : PlotControlBase
{
    public static readonly double[] WindowOptions = { 2, 5, 10, 30, 60 };
    public static readonly double[] ScaleOptions = { 10, 20, 50, 100, 200, 500 };

    private double[] _ts = Array.Empty<double>();
    private double[][] _data = Array.Empty<double[]>();
    private IList<string> _names = Array.Empty<string>();
    private IList<MarkerEvent> _markers = Array.Empty<MarkerEvent>();

    private double _windowSeconds = 10;
    private double _viewStart;
    private double _scaleUv = 50;
    private Point? _dragStart;
    private double _dragViewStart;

    private const int LeftMargin = 58, RightMargin = 14, TopMargin = 22, BottomMargin = 26;

    /// <summary>Zaman ekseni etiketleri için başlangıç (genellikle kayıt başlangıcı).</summary>
    public double TimeOrigin { get; set; }

    /// <summary>Canlı modda görünüm her zaman en son veriye kilitlenir.</summary>
    public bool FollowLatest { get; set; }

    public bool ShowMarkerLabels { get; set; } = true;

    public event EventHandler ViewChanged;

    public double WindowSeconds
    {
        get => _windowSeconds;
        set { _windowSeconds = Math.Max(0.5, value); ClampView(); Invalidate(); ViewChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>Kanal bandının yarı yüksekliğine karşılık gelen genlik (µV).</summary>
    public double ScaleUv
    {
        get => _scaleUv;
        set { _scaleUv = Math.Max(1, value); Invalidate(); ViewChanged?.Invoke(this, EventArgs.Empty); }
    }

    public double ViewStart
    {
        get => _viewStart;
        set { _viewStart = value; ClampView(); Invalidate(); ViewChanged?.Invoke(this, EventArgs.Empty); }
    }

    public double DataStart => _ts.Length > 0 ? _ts[0] : 0;
    public double DataEnd => _ts.Length > 0 ? _ts[^1] : 0;

    public void SetData(double[] timestamps, double[][] data, IList<string> names, IList<MarkerEvent> markers, bool resetView = false)
    {
        _ts = timestamps ?? Array.Empty<double>();
        _data = data ?? Array.Empty<double[]>();
        _names = names ?? Array.Empty<string>();
        _markers = markers ?? Array.Empty<MarkerEvent>();
        if (resetView) _viewStart = DataStart;
        ClampView();
        Invalidate();
    }

    public void Clear() => SetData(null, null, null, null, true);

    private void ClampView()
    {
        if (FollowLatest) { _viewStart = DataEnd - _windowSeconds; return; }
        double maxStart = Math.Max(DataStart, DataEnd - _windowSeconds);
        _viewStart = Math.Clamp(_viewStart, DataStart, maxStart);
    }

    // ------------------------------------------------------------------ fare

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left && !FollowLatest)
        {
            _dragStart = e.Location;
            _dragViewStart = _viewStart;
            Cursor = Cursors.SizeWE;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart.HasValue)
        {
            double plotW = Math.Max(1, Width - LeftMargin - RightMargin);
            double dt = (e.X - _dragStart.Value.X) / plotW * _windowSeconds;
            ViewStart = _dragViewStart - dt;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragStart = null;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((ModifierKeys & Keys.Control) != 0)
            ScaleUv = Step(ScaleOptions, _scaleUv, e.Delta < 0 ? 1 : -1);
        else
        {
            double center = _viewStart + _windowSeconds / 2;
            WindowSeconds = Step(WindowOptions, _windowSeconds, e.Delta < 0 ? 1 : -1);
            if (!FollowLatest) ViewStart = center - _windowSeconds / 2;
        }
    }

    private static double Step(double[] options, double current, int dir)
    {
        int idx = Array.FindIndex(options, o => o >= current - 1e-9);
        if (idx < 0) idx = options.Length - 1;
        return options[Math.Clamp(idx + dir, 0, options.Length - 1)];
    }

    // ------------------------------------------------------------------ çizim

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var plot = new RectangleF(LeftMargin, TopMargin, Width - LeftMargin - RightMargin, Height - TopMargin - BottomMargin);
        if (plot.Width < 20 || plot.Height < 20) return;

        using var textBrush = new SolidBrush(Theme.Text);
        using var mutedBrush = new SolidBrush(Theme.TextMuted);

        int nch = _data.Length;
        if (nch == 0 || _ts.Length == 0)
        {
            using var f = Theme.UiFont(10f);
            var msg = "Veri yok — bir dosya açın, simülasyon oluşturun veya canlı LSL akışına bağlanın.";
            var sz = g.MeasureString(msg, f);
            g.DrawString(msg, f, mutedBrush, plot.X + (plot.Width - sz.Width) / 2, plot.Y + (plot.Height - sz.Height) / 2);
            return;
        }

        double t0 = _viewStart, t1 = _viewStart + _windowSeconds;
        float X(double t) => plot.X + (float)((t - t0) / _windowSeconds * plot.Width);
        float bandH = plot.Height / nch;

        // Zaman ızgarası
        using (var gridPen = new Pen(Theme.Grid))
        using (var axisFont = Theme.UiFont(8f))
        {
            double step = Theme.NiceStep(_windowSeconds, 10);
            double first = Math.Ceiling((t0 - TimeOrigin) / step) * step + TimeOrigin;
            for (double t = first; t <= t1; t += step)
            {
                float x = X(t);
                g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
                string label = FormatTime(t - TimeOrigin);
                var sz = g.MeasureString(label, axisFont);
                g.DrawString(label, axisFont, mutedBrush, x - sz.Width / 2, plot.Bottom + 4);
            }
        }

        // Kanal bantları ve sinyaller
        int i0 = LowerBound(_ts, t0), i1 = Math.Min(_ts.Length - 1, LowerBound(_ts, t1));
        using var labelFont = Theme.UiFont(9f, FontStyle.Bold);
        for (int ch = 0; ch < nch; ch++)
        {
            float yMid = plot.Top + bandH * (ch + 0.5f);
            using (var zeroPen = new Pen(Theme.Grid) { DashStyle = DashStyle.Dot })
                g.DrawLine(zeroPen, plot.Left, yMid, plot.Right, yMid);

            string name = ch < _names.Count ? _names[ch] : $"Ch{ch + 1}";
            var color = EegConstants.IndexOf(name) >= 0 ? EegConstants.ColorOf(name) : Theme.TextMuted;
            using (var b = new SolidBrush(color))
                g.DrawString(name, labelFont, b, 6, yMid - 8);

            if (i1 > i0) DrawTrace(g, plot, _data[ch], i0, i1, yMid, bandH, X, color);
        }

        DrawMarkers(g, plot, t0, t1, X);

        // Ölçek çubuğu
        using (var scalePen = new Pen(Theme.Text, 2))
        using (var f = Theme.UiFont(8f))
        {
            float barH = bandH / 2;
            float x = plot.Right - 6, yb = plot.Bottom - 6;
            g.DrawLine(scalePen, x, yb, x, yb - barH);
            string s = $"{_scaleUv:0} µV";
            var sz = g.MeasureString(s, f);
            g.DrawString(s, f, textBrush, x - sz.Width - 4, yb - barH / 2 - sz.Height / 2);
        }

        using (var borderPen = new Pen(Theme.Border))
            g.DrawRectangle(borderPen, plot.X, plot.Y, plot.Width, plot.Height);

        using (var f = Theme.UiFont(8f))
            g.DrawString($"Pencere: {_windowSeconds:0.#} s   Ölçek: ±{_scaleUv:0} µV   (sürükle: kaydır · tekerlek: zaman · Ctrl+tekerlek: genlik)",
                f, mutedBrush, LeftMargin, 4);
    }

    private void DrawTrace(Graphics g, RectangleF plot, double[] y, int i0, int i1, float yMid, float bandH,
        Func<double, float> X, Color color)
    {
        double k = (bandH / 2) / _scaleUv;
        int n = i1 - i0 + 1;
        var region = g.Clip;
        g.SetClip(new RectangleF(plot.X, plot.Y, plot.Width, plot.Height));
        using var pen = new Pen(color, 1f);

        if (n <= plot.Width * 2)
        {
            var pts = new PointF[n];
            for (int i = 0; i < n; i++)
                pts[i] = new PointF(X(_ts[i0 + i]), yMid - (float)(y[i0 + i] * k));
            if (pts.Length > 1) g.DrawLines(pen, pts);
        }
        else
        {
            // Piksel sütunu başına min/max seyreltme
            int cols = (int)plot.Width;
            var pts = new List<PointF>(cols * 2);
            int idx = i0;
            for (int c = 0; c < cols; c++)
            {
                double tEnd = _viewStart + (c + 1) / (double)cols * _windowSeconds;
                double mn = double.MaxValue, mx = double.MinValue;
                int start = idx;
                while (idx <= i1 && _ts[idx] < tEnd)
                {
                    if (y[idx] < mn) mn = y[idx];
                    if (y[idx] > mx) mx = y[idx];
                    idx++;
                }
                if (idx == start) continue;
                float x = plot.X + c;
                pts.Add(new PointF(x, yMid - (float)(mx * k)));
                pts.Add(new PointF(x, yMid - (float)(mn * k)));
            }
            if (pts.Count > 1) g.DrawLines(pen, pts.ToArray());
        }
        g.Clip = region;
    }

    private void DrawMarkers(Graphics g, RectangleF plot, double t0, double t1, Func<double, float> X)
    {
        using var f = Theme.UiFont(7.5f);
        int row = 0;
        float lastX = float.MinValue;
        foreach (var m in _markers)
        {
            if (m.Timestamp < t0 || m.Timestamp > t1) continue;
            if (m.Category == MarkerCategory.StimulusDuplicate || m.Category == MarkerCategory.StimulusEnd) continue;
            float x = X(m.Timestamp);
            var color = MarkerEvent.DisplayColor(m.Category);
            using (var pen = new Pen(Theme.WithAlpha(color, 200), m.IsEpochTrigger ? 1.4f : 1f) { DashStyle = DashStyle.Dash })
                g.DrawLine(pen, x, plot.Top, x, plot.Bottom);

            if (!ShowMarkerLabels) continue;
            row = x - lastX < 90 ? (row + 1) % 3 : 0;
            lastX = x;
            string label = m.Value.Length > 22 ? m.Value[..22] + "…" : m.Value;
            var sz = g.MeasureString(label, f);
            var r = new RectangleF(x + 2, plot.Top + 2 + row * (sz.Height + 1), sz.Width, sz.Height);
            using (var bg = new SolidBrush(Theme.WithAlpha(Theme.Background, 200))) g.FillRectangle(bg, r);
            using (var b = new SolidBrush(color)) g.DrawString(label, f, b, r.X, r.Y);
        }
    }

    private static string FormatTime(double s)
    {
        if (Math.Abs(s) >= 60)
        {
            int m = (int)(s / 60);
            return $"{m}:{Math.Abs(s - m * 60):00.#}";
        }
        return $"{s:0.##} s";
    }

    private static int LowerBound(double[] a, double v)
    {
        int lo = 0, hi = a.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (a[mid] < v) lo = mid + 1; else hi = mid;
        }
        return Math.Min(lo, a.Length - 1);
    }
}
