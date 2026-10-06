using System.Drawing.Drawing2D;
using XOnERPStudio.Models;

namespace XOnERPStudio.Controls;

/// <summary>
/// Kanal bazlı sinyal kalitesi göstergesi: bant sınırlı RMS gürültü (1–45 Hz), şebeke gürültüsü oranı
/// ve artefakt nedeniyle reddedilen epoch yüzdesi. Akış empedans bilgisi taşımadığından kalite
/// sinyalin kendisinden kestirilir (X.On LSL köprüsünün empedans ekranını tamamlayıcıdır).
/// Canlı modda <see cref="SetLiveRms"/> ile anlık RMS değerleri gösterilebilir.
/// </summary>
public class SignalQualityGauge : PlotControlBase
{
    private List<ChannelQuality> _quality = new();
    private const double MaxRms = 100;

    public void SetData(IEnumerable<ChannelQuality> quality)
    {
        _quality = quality?.ToList() ?? new();
        Invalidate();
    }

    /// <summary>Canlı görüntü için yalnızca RMS değerleriyle besleme.</summary>
    public void SetLiveRms(IList<string> names, IList<double> rms)
    {
        _quality = names.Select((n, i) => new ChannelQuality
        {
            ChannelName = n,
            RmsUv = rms[i],
            IsFlat = rms[i] < 0.5,
            Level = rms[i] < 0.5 || rms[i] > 80 ? QualityLevel.Poor : rms[i] > 40 ? QualityLevel.Fair : QualityLevel.Good,
        }).ToList();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var text = new SolidBrush(Theme.Text);
        using var muted = new SolidBrush(Theme.TextMuted);
        using var titleFont = Theme.UiFont(10.5f, FontStyle.Bold);
        using var f = Theme.UiFont(8.5f);
        using var fb = Theme.UiFont(9f, FontStyle.Bold);

        g.DrawString("Sinyal Kalitesi (RMS 1–45 Hz)", titleFont, text, 10, 8);
        if (_quality.Count == 0)
        {
            g.DrawString("Veri yok.", f, muted, 10, 36);
            return;
        }

        float top = 38, rowH = Math.Min(40, (Height - top - 30) / _quality.Count);
        float barX = 60, barW = Math.Max(60, Width - barX - 230);
        for (int i = 0; i < _quality.Count; i++)
        {
            var q = _quality[i];
            float y = top + i * rowH;
            var color = q.Level switch { QualityLevel.Good => Theme.Good, QualityLevel.Fair => Theme.Fair, _ => Theme.Poor };

            using (var cb = new SolidBrush(EegConstants.ColorOf(q.ChannelName)))
                g.DrawString(q.ChannelName, fb, cb, 10, y + rowH / 2 - 9);

            var track = new RectangleF(barX, y + rowH * 0.25f, barW, rowH * 0.5f);
            using (var p = Theme.RoundedRect(track, track.Height / 2))
            using (var b = new SolidBrush(Theme.SurfaceAlt))
                g.FillPath(b, p);

            // Eşik işaretleri
            using (var tp = new Pen(Theme.GridStrong) { DashStyle = DashStyle.Dot })
                foreach (var th in new[] { 40.0, 80.0 })
                {
                    float tx = barX + (float)(th / MaxRms * barW);
                    g.DrawLine(tp, tx, track.Top - 2, tx, track.Bottom + 2);
                }

            float w = (float)(Math.Clamp(q.RmsUv / MaxRms, 0.02, 1) * barW);
            var fill = new RectangleF(barX, track.Y, w, track.Height);
            using (var p = Theme.RoundedRect(fill, track.Height / 2))
            using (var b = new LinearGradientBrush(new RectangleF(fill.X - 1, fill.Y, fill.Width + 2, fill.Height), Theme.WithAlpha(color, 140), color, 0f))
                g.FillPath(b, p);

            string level = q.IsFlat ? "Düz sinyal!" : q.Level switch { QualityLevel.Good => "İyi", QualityLevel.Fair => "Orta", _ => "Zayıf" };
            string detail = $"{q.RmsUv:0.0} µV · {level}";
            if (q.LineNoisePercent > 0) detail += $" · 50Hz %{q.LineNoisePercent:0}";
            if (q.RejectedEpochPercent > 0) detail += $" · red %{q.RejectedEpochPercent:0}";
            using (var db = new SolidBrush(color))
                g.DrawString(detail, f, db, barX + barW + 10, y + rowH / 2 - 8);
        }
        g.DrawString("Eşikler: <40 µV iyi · 40–80 µV orta · >80 µV veya düz sinyal zayıf", f, muted, 10, Height - 20);
    }
}
