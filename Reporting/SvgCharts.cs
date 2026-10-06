using System.Globalization;
using System.Text;
using XOnERPStudio.Models;
using XOnERPStudio.SignalProcessing;

namespace XOnERPStudio.Reporting;

/// <summary>Rapor için bağımsız (harici kütüphanesiz) vektörel SVG grafik üreticileri.</summary>
public static class SvgCharts
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public const string TargetHex = "#d64545";
    public const string StandardHex = "#3b7dd8";
    public const string DiffHex = "#2e9e5b";
    public const string PeakHex = "#c99400";

    private static string F(double v) => v.ToString("0.##", Inv);
    private static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");

    /// <summary>Tek kanal ERP karşılaştırma grafiği (Hedef ± SEM, Standart ± SEM, Fark, tepe etiketleri).</summary>
    public static string ErpChart(ChannelErp erp, ErpFilterSettings s, int width = 620, int height = 300, string title = null)
    {
        var sb = new StringBuilder();
        const int L = 48, R = 14, T = 30, B = 30;
        double pw = width - L - R, ph = height - T - B;
        var waves = new[] { erp?.Standard, erp?.Target, erp?.Difference }.Where(w => w is { IsEmpty: false }).ToList();
        sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {width} {height}' class='chart' role='img' aria-label='{Esc(title ?? erp?.ChannelName)} ERP'>");
        sb.Append($"<text x='{L}' y='18' class='ct'>{Esc(title ?? (erp?.ChannelName + " — ERP"))}</text>");
        if (waves.Count == 0)
        {
            sb.Append($"<text x='{width / 2}' y='{height / 2}' text-anchor='middle' class='muted'>Veri yok</text></svg>");
            return sb.ToString();
        }

        double range = 1;
        foreach (var w in waves)
            for (int i = 0; i < w.Mean.Length; i++) range = Math.Max(range, Math.Abs(w.Mean[i]) + (w == erp.Difference ? 0 : w.Sem[i]));
        double ystep = NiceStep(range * 2.2, 6);
        range = Math.Ceiling(range * 1.1 / ystep) * ystep;
        double t0 = s.EpochStartMs, t1 = s.EpochEndMs;
        double sign = s.NegativeUp ? -1 : 1;
        double X(double ms) => L + (ms - t0) / (t1 - t0) * pw;
        double Y(double uv) => T + ph / 2 - sign * uv / range * ph / 2;

        // P300 penceresi
        sb.Append($"<rect x='{F(X(s.P300StartMs))}' y='{T}' width='{F(X(s.P300EndMs) - X(s.P300StartMs))}' height='{F(ph)}' fill='{PeakHex}' fill-opacity='0.08'/>");
        // Izgara
        for (double v = -range; v <= range + 1e-9; v += ystep)
        {
            sb.Append($"<line x1='{L}' x2='{F(L + pw)}' y1='{F(Y(v))}' y2='{F(Y(v))}' class='grid'/>");
            sb.Append($"<text x='{L - 5}' y='{F(Y(v) + 3)}' text-anchor='end' class='ax'>{F(v)}</text>");
        }
        double xstep = NiceStep(t1 - t0, 10);
        for (double t = Math.Ceiling(t0 / xstep) * xstep; t <= t1 + 1e-9; t += xstep)
        {
            sb.Append($"<line x1='{F(X(t))}' x2='{F(X(t))}' y1='{T}' y2='{F(T + ph)}' class='grid'/>");
            sb.Append($"<text x='{F(X(t))}' y='{F(T + ph + 14)}' text-anchor='middle' class='ax'>{F(t)}</text>");
        }
        sb.Append($"<text x='{F(L + pw)}' y='{height - 2}' text-anchor='end' class='ax'>ms</text>");
        sb.Append($"<text x='4' y='{T - 6}' class='ax'>µV{(s.NegativeUp ? " (− yukarı)" : "")}</text>");
        sb.Append($"<line x1='{L}' x2='{F(L + pw)}' y1='{F(Y(0))}' y2='{F(Y(0))}' class='zero'/>");
        sb.Append($"<line x1='{F(X(0))}' x2='{F(X(0))}' y1='{T}' y2='{F(T + ph)}' class='onset'/>");

        foreach (var w in waves)
        {
            string color = w == erp.Target ? TargetHex : w == erp.Standard ? StandardHex : DiffHex;
            if (w != erp.Difference)
            {
                var band = new StringBuilder();
                for (int i = 0; i < w.Mean.Length; i++) band.Append($"{F(X(w.TimesMs[i]))},{F(Y(w.Mean[i] + w.Sem[i]))} ");
                for (int i = w.Mean.Length - 1; i >= 0; i--) band.Append($"{F(X(w.TimesMs[i]))},{F(Y(w.Mean[i] - w.Sem[i]))} ");
                sb.Append($"<polygon points='{band}' fill='{color}' fill-opacity='0.16' stroke='none'/>");
            }
            var line = new StringBuilder();
            for (int i = 0; i < w.Mean.Length; i++) line.Append($"{F(X(w.TimesMs[i]))},{F(Y(w.Mean[i]))} ");
            string dash = w == erp.Difference ? " stroke-dasharray='5 3'" : "";
            sb.Append($"<polyline points='{line}' fill='none' stroke='{color}' stroke-width='1.8'{dash}/>");
        }

        if (erp.Target is { IsEmpty: false })
        {
            PeakLabel(sb, erp.Target.P300, X, Y, PeakHex, true);
            PeakLabel(sb, erp.Target.N200, X, Y, "#0f8b94", false);
        }

        // Lejant
        double lx = L + pw - 4;
        foreach (var w in Enumerable.Reverse(waves))
        {
            string color = w == erp.Target ? TargetHex : w == erp.Standard ? StandardHex : DiffHex;
            string txt = $"{w.ConditionName} (n={w.EpochCount})";
            double tw = txt.Length * 5.6 + 22;
            lx -= tw;
            sb.Append($"<line x1='{F(lx)}' x2='{F(lx + 14)}' y1='14' y2='14' stroke='{color}' stroke-width='3'/>");
            sb.Append($"<text x='{F(lx + 18)}' y='18' class='lg'>{Esc(txt)}</text>");
        }
        sb.Append($"<rect x='{L}' y='{T}' width='{F(pw)}' height='{F(ph)}' class='frame'/>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void PeakLabel(StringBuilder sb, ComponentPeak p, Func<double, double> X, Func<double, double> Y, string color, bool above)
    {
        if (p is not { Found: true }) return;
        double x = X(p.LatencyMs), y = Y(p.AmplitudeUv);
        sb.Append($"<circle cx='{F(x)}' cy='{F(y)}' r='3.5' fill='{color}'/>");
        string t = $"{p.Name}: {p.LatencyMs:0} ms, {p.AmplitudeUv.ToString("0.0", Inv)} µV";
        sb.Append($"<text x='{F(x)}' y='{F(above ? y - 8 : y + 15)}' text-anchor='middle' class='pk' fill='{color}'>{Esc(t)}</text>");
    }

    /// <summary>7 kanallı kafa topografisi (mini ERP grafikleri).</summary>
    public static string Topography(ErpAnalysisResult r, int size = 520)
    {
        var sb = new StringBuilder();
        double cx = size / 2.0, cy = size / 2.0 + 10, rad = size * 0.42;
        sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {size} {size + 20}' class='chart topo' role='img' aria-label='7 kanal topografi'>");
        sb.Append($"<circle cx='{F(cx)}' cy='{F(cy)}' r='{F(rad)}' class='head'/>");
        sb.Append($"<polyline points='{F(cx - rad * 0.12)},{F(cy - rad * 0.99)} {F(cx)},{F(cy - rad * 1.13)} {F(cx + rad * 0.12)},{F(cy - rad * 0.99)}' class='head' fill='none'/>");
        sb.Append($"<ellipse cx='{F(cx - rad * 1.0)}' cy='{F(cy)}' rx='{F(rad * 0.06)}' ry='{F(rad * 0.17)}' class='head'/>");
        sb.Append($"<ellipse cx='{F(cx + rad * 1.0)}' cy='{F(cy)}' rx='{F(rad * 0.06)}' ry='{F(rad * 0.17)}' class='head'/>");

        double range = 2;
        foreach (var name in EegConstants.ChannelNames)
        {
            var c = r.Get(name);
            foreach (var w in new[] { c?.Target, c?.Standard }.Where(w => w is { IsEmpty: false }))
                for (int i = 0; i < w.Mean.Length; i++) range = Math.Max(range, Math.Abs(w.Mean[i]));
        }
        range = Math.Ceiling(range * 1.1);
        double bw = rad * 0.56, bh = rad * 0.40;
        var s = r.Settings;
        double sign = s.NegativeUp ? -1 : 1;

        for (int ch = 0; ch < EegConstants.ChannelNames.Length; ch++)
        {
            string name = EegConstants.ChannelNames[ch];
            var pos = EegConstants.TopoPositions[ch];
            double bx = cx + pos.X * rad - bw / 2, by = cy + pos.Y * rad - bh / 2;
            sb.Append($"<rect x='{F(bx)}' y='{F(by)}' width='{F(bw)}' height='{F(bh)}' rx='5' class='box'/>");
            sb.Append($"<text x='{F(bx + 5)}' y='{F(by + 13)}' class='bn'>{name}</text>");
            var c = r.Get(name);
            if (c == null) continue;
            double ix = bx + 5, iy = by + 18, iw = bw - 10, ih = bh - 22;
            double X(double ms) => ix + (ms - s.EpochStartMs) / (s.EpochEndMs - s.EpochStartMs) * iw;
            double Y(double uv) => iy + ih / 2 - sign * uv / range * ih / 2;
            sb.Append($"<line x1='{F(ix)}' x2='{F(ix + iw)}' y1='{F(Y(0))}' y2='{F(Y(0))}' class='grid'/>");
            sb.Append($"<line x1='{F(X(0))}' x2='{F(X(0))}' y1='{F(iy)}' y2='{F(iy + ih)}' class='grid'/>");
            foreach (var (w, color) in new[] { (c.Standard, StandardHex), (c.Target, TargetHex) })
            {
                if (w is not { IsEmpty: false }) continue;
                var line = new StringBuilder();
                for (int i = 0; i < w.Mean.Length; i++) line.Append($"{F(X(w.TimesMs[i]))},{F(Y(w.Mean[i]))} ");
                sb.Append($"<polyline points='{line}' fill='none' stroke='{color}' stroke-width='1.3'/>");
            }
            var p = c.Target?.P300;
            if (p is { Found: true })
                sb.Append($"<text x='{F(bx + bw - 5)}' y='{F(by + 13)}' text-anchor='end' class='ax'>P300 {p.AmplitudeUv.ToString("0.0", Inv)} µV</text>");
        }
        sb.Append($"<text x='8' y='{size + 14}' class='ax'>Ortak ölçek ±{range:0} µV · kırmızı: hedef, mavi: standart</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>Kanal başına göreli bant gücü yığılmış çubuk grafiği.</summary>
    public static string BandPowerChart(SpectrumResult spec, int width = 620, int height = 220)
    {
        if (spec == null) return "";
        string[] colors = { "#9b59b6", "#3498db", "#2ecc71", "#f1c40f" };
        var sb = new StringBuilder();
        const int L = 40, T = 26, B = 24;
        double ph = height - T - B, pw = width - L - 10;
        int n = spec.BandRelative.Length;
        double gw = pw / n, bw = gw * 0.6;
        sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {width} {height}' class='chart' role='img' aria-label='bant gücü'>");
        for (int p = 0; p <= 100; p += 25)
        {
            double y = T + ph - p / 100.0 * ph;
            sb.Append($"<line x1='{L}' x2='{F(L + pw)}' y1='{F(y)}' y2='{F(y)}' class='grid'/><text x='{L - 4}' y='{F(y + 3)}' text-anchor='end' class='ax'>{p}%</text>");
        }
        for (int ch = 0; ch < n; ch++)
        {
            double x = L + ch * gw + (gw - bw) / 2, y = T + ph;
            for (int b = 0; b < spec.BandRelative[ch].Length; b++)
            {
                double h = spec.BandRelative[ch][b] * ph;
                y -= h;
                sb.Append($"<rect x='{F(x)}' y='{F(y)}' width='{F(bw)}' height='{F(h)}' fill='{colors[b]}'/>");
            }
            sb.Append($"<text x='{F(x + bw / 2)}' y='{F(T + ph + 14)}' text-anchor='middle' class='ax'>{EegConstants.ChannelNames[ch]}</text>");
        }
        double lx = L;
        for (int b = 0; b < FrequencyAnalysis.Bands.Length; b++)
        {
            var band = FrequencyAnalysis.Bands[b];
            sb.Append($"<rect x='{F(lx)}' y='8' width='10' height='10' fill='{colors[b]}'/><text x='{F(lx + 14)}' y='17' class='lg'>{band.Name} {band.Low:0}–{band.High:0} Hz</text>");
            lx += 130;
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static double NiceStep(double range, int ticks)
    {
        if (range <= 0) return 1;
        double raw = range / ticks, mag = Math.Pow(10, Math.Floor(Math.Log10(raw))), n = raw / mag;
        return (n < 1.5 ? 1 : n < 3 ? 2 : n < 7 ? 5 : 10) * mag;
    }
}
