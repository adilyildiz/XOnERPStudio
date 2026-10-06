using System.Drawing.Drawing2D;
using XOnERPStudio.Models;
using XOnERPStudio.SignalProcessing;

namespace XOnERPStudio.Controls;

/// <summary>
/// Üst bölüm: kanal başına güç spektral yoğunluğu (dB, 1–45 Hz) ve renkli frekans bantları.
/// Alt bölüm: her kanal için Delta / Theta / Alpha / Beta göreli güç çubukları.
/// </summary>
public class FrequencySpectrumViewer : PlotControlBase
{
    private SpectrumResult _spec;
    private double _maxFreq = 45;

    private static readonly Color[] BandColors =
    {
        Color.FromArgb(155, 89, 182), Color.FromArgb(52, 152, 219), Color.FromArgb(46, 204, 113), Color.FromArgb(241, 196, 15),
    };

    /// <summary>Gösterilecek kanal; null ise tüm kanallar.</summary>
    public string HighlightChannel { get; set; }

    public double MaxFrequency
    {
        get => _maxFreq;
        set { _maxFreq = value; Invalidate(); }
    }

    public void SetData(SpectrumResult spectrum)
    {
        _spec = spectrum;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var muted = new SolidBrush(Theme.TextMuted);
        using var text = new SolidBrush(Theme.Text);
        using var titleFont = Theme.UiFont(10.5f, FontStyle.Bold);
        using var axisFont = Theme.UiFont(8f);

        g.DrawString(_spec != null ? $"Güç Spektrumu (Welch, {_spec.SegmentSeconds:0.#} s segment) — {_spec.Label}" : "Güç Spektrumu",
            titleFont, text, 10, 8);
        if (_spec == null)
        {
            g.DrawString("Spektrum yok — analiz çalıştırın (en az birkaç saniyelik kayıt gerekir).", axisFont, muted, 10, 34);
            return;
        }

        int split = (int)(Height * 0.60);
        var psdRect = new RectangleF(58, 52, Width - 58 - 150, split - 52 - 24);
        var barRect = new RectangleF(58, split + 24, Width - 58 - 20, Height - split - 24 - 30);
        if (psdRect.Width < 50 || psdRect.Height < 40) return;

        // ---- PSD ----
        var f = _spec.Frequencies;
        int kMax = Array.FindLastIndex(f, x => x <= _maxFreq);
        int kMin = Array.FindIndex(f, x => x >= 1);
        if (kMax <= kMin) return;

        double dbMin = double.MaxValue, dbMax = double.MinValue;
        for (int ch = 0; ch < _spec.Power.Length; ch++)
            for (int k = kMin; k <= kMax; k++)
            {
                double db = 10 * Math.Log10(Math.Max(_spec.Power[ch][k], 1e-6));
                dbMin = Math.Min(dbMin, db); dbMax = Math.Max(dbMax, db);
            }
        dbMin = Math.Floor(dbMin / 5) * 5; dbMax = Math.Ceiling(dbMax / 5) * 5;
        if (dbMax - dbMin < 10) dbMax = dbMin + 10;

        float X(double hz) => psdRect.X + (float)((hz - f[kMin]) / (f[kMax] - f[kMin]) * psdRect.Width);
        float Y(double db) => psdRect.Bottom - (float)((db - dbMin) / (dbMax - dbMin) * psdRect.Height);

        for (int b = 0; b < FrequencyAnalysis.Bands.Length; b++)
        {
            var band = FrequencyAnalysis.Bands[b];
            float x0 = X(Math.Max(band.Low, f[kMin])), x1 = X(Math.Min(band.High, f[kMax]));
            using var br = new SolidBrush(Theme.WithAlpha(BandColors[b], 22));
            g.FillRectangle(br, x0, psdRect.Top, x1 - x0, psdRect.Height);
            using var lb = new SolidBrush(Theme.WithAlpha(BandColors[b], 170));
            g.DrawString(band.Name, axisFont, lb, x0 + 2, psdRect.Top + 2);
        }

        using (var grid = new Pen(Theme.Grid))
        {
            for (double db = dbMin; db <= dbMax; db += 5)
            {
                g.DrawLine(grid, psdRect.Left, Y(db), psdRect.Right, Y(db));
                g.DrawString($"{db:0}", axisFont, muted, psdRect.Left - 28, Y(db) - 7);
            }
            for (double hz = 5; hz <= f[kMax]; hz += 5)
            {
                g.DrawLine(grid, X(hz), psdRect.Top, X(hz), psdRect.Bottom);
                g.DrawString($"{hz:0}", axisFont, muted, X(hz) - 6, psdRect.Bottom + 3);
            }
        }
        g.DrawString("dB (µV²/Hz)", axisFont, muted, 4, psdRect.Top - 14);
        g.DrawString("Hz", axisFont, muted, psdRect.Right - 12, psdRect.Bottom + 14);

        var clip = g.Clip;
        g.SetClip(psdRect);
        for (int ch = 0; ch < _spec.Power.Length; ch++)
        {
            string name = EegConstants.ChannelNames[ch];
            bool hl = HighlightChannel == null || HighlightChannel == name;
            var pts = new PointF[kMax - kMin + 1];
            for (int k = kMin; k <= kMax; k++)
                pts[k - kMin] = new PointF(X(f[k]), Y(10 * Math.Log10(Math.Max(_spec.Power[ch][k], 1e-6))));
            using var pen = new Pen(Theme.WithAlpha(EegConstants.ChannelColors[ch], hl ? 255 : 50), hl ? 1.8f : 1f);
            g.DrawLines(pen, pts);
        }
        g.Clip = clip;
        using (var border = new Pen(Theme.Border)) g.DrawRectangle(border, psdRect.X, psdRect.Y, psdRect.Width, psdRect.Height);

        // Lejant ve alfa tepe frekansı
        float ly = psdRect.Top;
        for (int ch = 0; ch < _spec.Power.Length; ch++)
        {
            using var b = new SolidBrush(EegConstants.ChannelColors[ch]);
            g.FillRectangle(b, psdRect.Right + 14, ly + 4, 12, 4);
            double paf = PeakFrequency(ch, 7, 14);
            g.DrawString($"{EegConstants.ChannelNames[ch]}  α-tepe {paf:0.0} Hz", axisFont, text, psdRect.Right + 30, ly - 2);
            ly += 17;
        }

        // ---- Göreli bant gücü çubukları ----
        g.DrawString("Göreli bant gücü (%)", titleFont, text, 10, split);
        int nch = _spec.BandRelative.Length, nb = FrequencyAnalysis.Bands.Length;
        float groupW = barRect.Width / nch, barW = Math.Max(4, (groupW - 16) / nb);
        using (var grid = new Pen(Theme.Grid))
            for (int p = 0; p <= 100; p += 25)
            {
                float y = barRect.Bottom - p / 100f * barRect.Height;
                g.DrawLine(grid, barRect.Left, y, barRect.Right, y);
                g.DrawString($"{p}", axisFont, muted, barRect.Left - 26, y - 7);
            }
        for (int ch = 0; ch < nch; ch++)
        {
            float gx = barRect.X + ch * groupW + 8;
            for (int b = 0; b < nb; b++)
            {
                float h = (float)(_spec.BandRelative[ch][b] * barRect.Height);
                using var br = new SolidBrush(BandColors[b]);
                g.FillRectangle(br, gx + b * barW, barRect.Bottom - h, barW - 2, h);
            }
            using var cb = new SolidBrush(EegConstants.ChannelColors[ch]);
            var name = EegConstants.ChannelNames[ch];
            var sz = g.MeasureString(name, axisFont);
            g.DrawString(name, axisFont, cb, gx + (nb * barW - sz.Width) / 2, barRect.Bottom + 3);
        }
        float lx = Width - 20;
        for (int b = nb - 1; b >= 0; b--)
        {
            var band = FrequencyAnalysis.Bands[b];
            string s = $"{band.Name} {band.Low:0}–{band.High:0} Hz";
            var sz = g.MeasureString(s, axisFont);
            lx -= sz.Width + 18;
            using var br = new SolidBrush(BandColors[b]);
            g.FillRectangle(br, lx, split + 5, 10, 10);
            g.DrawString(s, axisFont, text, lx + 13, split + 2);
        }
    }

    private double PeakFrequency(int ch, double lo, double hi)
    {
        int best = -1;
        for (int k = 0; k < _spec.Frequencies.Length; k++)
        {
            if (_spec.Frequencies[k] < lo || _spec.Frequencies[k] > hi) continue;
            if (best < 0 || _spec.Power[ch][k] > _spec.Power[ch][best]) best = k;
        }
        return best >= 0 ? _spec.Frequencies[best] : double.NaN;
    }
}
