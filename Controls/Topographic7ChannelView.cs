using System.Drawing.Drawing2D;
using XOnERPStudio.Models;

namespace XOnERPStudio.Controls;

/// <summary>
/// 10-20 anatomik kafa düzeni: stilize kafa konturu (burun yukarıda) üzerinde 7 kanalın mini ERP
/// grafikleri. Tüm kutular ortak Y ölçeğini kullanır; böylece parietal P300 artışı tek bakışta görülür.
/// Kutu çerçevesinin rengi P300 genliğiyle ısınır. Bir kanala tıklamak ChannelSelected olayını tetikler.
/// </summary>
public class Topographic7ChannelView : PlotControlBase
{
    private ErpAnalysisResult _result;
    private readonly Dictionary<string, RectangleF> _boxes = new();
    private string _hover;

    public bool DifferenceOnly { get; set; }
    public string SelectedChannel { get; set; }

    public event Action<string> ChannelSelected;

    public void SetData(ErpAnalysisResult result)
    {
        _result = result;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        string h = _boxes.FirstOrDefault(kv => kv.Value.Contains(e.Location)).Key;
        if (h != _hover) { _hover = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (_hover != null)
        {
            SelectedChannel = _hover;
            Invalidate();
            ChannelSelected?.Invoke(_hover);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        _boxes.Clear();

        float size = Math.Min(Width - 40, Height - 60);
        if (size < 120) return;
        float cx = Width / 2f, cy = Height / 2f + 12, r = size / 2f;

        // Kafa konturu
        using (var headPen = new Pen(Theme.GridStrong, 2.5f))
        using (var headFill = new SolidBrush(Color.FromArgb(30, 32, 40)))
        {
            g.FillEllipse(headFill, cx - r, cy - r, 2 * r, 2 * r);
            g.DrawEllipse(headPen, cx - r, cy - r, 2 * r, 2 * r);
            // Burun
            var nose = new[] { new PointF(cx - r * 0.12f, cy - r * 0.99f), new PointF(cx, cy - r * 1.14f), new PointF(cx + r * 0.12f, cy - r * 0.99f) };
            g.DrawLines(headPen, nose);
            // Kulaklar
            g.DrawArc(headPen, cx - r * 1.08f, cy - r * 0.18f, r * 0.16f, r * 0.36f, 90, 180);
            g.DrawArc(headPen, cx + r * 0.92f, cy - r * 0.18f, r * 0.16f, r * 0.36f, 270, 180);
        }
        using (var guide = new Pen(Theme.Grid) { DashStyle = DashStyle.Dot })
        {
            g.DrawLine(guide, cx, cy - r, cx, cy + r);
            g.DrawLine(guide, cx - r, cy, cx + r, cy);
        }

        using var muted = new SolidBrush(Theme.TextMuted);
        using var text = new SolidBrush(Theme.Text);
        using var titleFont = Theme.UiFont(10.5f, FontStyle.Bold);
        g.DrawString(DifferenceOnly ? "Topografi — Fark dalgası (Hedef − Standart)" : "Topografi — Hedef (kırmızı) / Standart (mavi)",
            titleFont, text, 10, 8);

        if (_result == null)
        {
            using var f = Theme.UiFont(10f);
            const string msg = "Analiz sonucu yok.";
            var sz = g.MeasureString(msg, f);
            g.DrawString(msg, f, muted, cx - sz.Width / 2, cy - sz.Height / 2);
            return;
        }

        // Ortak ölçek ve P300 ısı aralığı
        double range = 2, maxP3 = 1e-6;
        foreach (var name in EegConstants.ChannelNames)
        {
            var c = _result.Get(name);
            if (c == null) continue;
            foreach (var w in Waves(c))
                for (int i = 0; i < w.Mean.Length; i++) range = Math.Max(range, Math.Abs(w.Mean[i]));
            var p = (DifferenceOnly ? c.Difference : c.Target)?.P300;
            if (p is { Found: true }) maxP3 = Math.Max(maxP3, p.AmplitudeUv);
        }
        range = Math.Ceiling(range * 1.1);

        float bw = r * 0.56f, bh = r * 0.40f;
        using var labelFont = Theme.UiFont(9f, FontStyle.Bold);
        using var smallFont = Theme.UiFont(7.5f);
        for (int ch = 0; ch < EegConstants.ChannelNames.Length; ch++)
        {
            string name = EegConstants.ChannelNames[ch];
            var pos = EegConstants.TopoPositions[ch];
            var box = new RectangleF(cx + pos.X * r - bw / 2, cy + pos.Y * r - bh / 2, bw, bh);
            _boxes[name] = box;
            var c = _result.Get(name);

            var wave = (DifferenceOnly ? c?.Difference : c?.Target)?.P300;
            double heat = wave is { Found: true } ? Math.Clamp(wave.AmplitudeUv / maxP3, 0, 1) : 0;
            var frame = Blend(Theme.Border, Theme.PeakColor, heat);
            bool sel = name == SelectedChannel, hov = name == _hover;

            using (var path = Theme.RoundedRect(box, 6))
            using (var fill = new SolidBrush(hov ? Theme.SurfaceAlt : Theme.Surface))
            using (var pen = new Pen(sel ? Theme.Accent : frame, sel ? 2.5f : 1.5f + (float)heat))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            var inner = RectangleF.Inflate(box, -6, -16);
            inner.Y += 6;
            if (c != null) DrawMini(g, inner, c, range);

            float nameW = g.MeasureString(name, labelFont).Width;
            using (var b = new SolidBrush(EegConstants.ChannelColors[ch]))
                g.DrawString(name, labelFont, b, box.X + 5, box.Y + 2);
            if (wave is { Found: true })
            {
                string s = $"P300 {wave.AmplitudeUv:0.0} µV · {wave.LatencyMs:0} ms";
                var sz = g.MeasureString(s, smallFont);
                if (sz.Width > box.Width - nameW - 12) { s = $"{wave.AmplitudeUv:0.0} µV · {wave.LatencyMs:0} ms"; sz = g.MeasureString(s, smallFont); }
                g.DrawString(s, smallFont, muted, Math.Max(box.X + nameW + 8, box.Right - sz.Width - 4), box.Y + 3);
            }
        }

        using (var f = Theme.UiFont(8f))
            g.DrawString($"Ortak ölçek: ±{range:0} µV · Pencere {_result.Settings.EpochStartMs:0}…{_result.Settings.EpochEndMs:0} ms · Kanala tıklayarak ERP sekmesinde açın",
                f, muted, 10, Height - 20);
    }

    private IEnumerable<ErpWaveform> Waves(ChannelErp c)
    {
        if (DifferenceOnly) { if (c.Difference is { IsEmpty: false }) yield return c.Difference; yield break; }
        if (c.Standard is { IsEmpty: false }) yield return c.Standard;
        if (c.Target is { IsEmpty: false }) yield return c.Target;
    }

    private void DrawMini(Graphics g, RectangleF r, ChannelErp c, double range)
    {
        var s = _result.Settings;
        bool negUp = s.NegativeUp;
        float X(double ms) => r.X + (float)((ms - s.EpochStartMs) / (s.EpochEndMs - s.EpochStartMs) * r.Width);
        float Y(double uv) => r.Y + r.Height / 2 - (float)((negUp ? -uv : uv) / range * r.Height / 2);

        using (var axis = new Pen(Theme.Grid))
        {
            g.DrawLine(axis, r.Left, Y(0), r.Right, Y(0));
            g.DrawLine(axis, X(0), r.Top, X(0), r.Bottom);
        }
        foreach (var w in Waves(c))
        {
            var color = w == c.Difference ? Theme.DifferenceColor : w == c.Target ? Theme.TargetColor : Theme.StandardColor;
            var pts = w.TimesMs.Select((t, i) => new PointF(X(t), Y(w.Mean[i]))).ToArray();
            using var pen = new Pen(color, 1.4f);
            if (pts.Length > 1) g.DrawLines(pen, pts);
        }
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
}
