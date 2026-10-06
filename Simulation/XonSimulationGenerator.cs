using XOnERPStudio.Models;

namespace XOnERPStudio.Simulation;

public class SimulationOptions
{
    public int Trials { get; set; } = 100;
    public double TargetRatio { get; set; } = 0.25;
    public double SampleRate { get; set; } = 250;
    public double RestSeconds { get; set; } = 10;
    public string TargetColor { get; set; } = "Kırmızı";
    public string[] Colors { get; set; } = { "Kırmızı", "Mavi", "Yeşil", "Sarı", "Mor" };
    /// <summary>Hedef P300 genliği (µV, parietal).</summary>
    public double P300AmplitudeUv { get; set; } = 10;
    public double P300LatencyMs { get; set; } = 350;
    /// <summary>Arka plan EEG gürültü düzeyi çarpanı.</summary>
    public double NoiseLevel { get; set; } = 1.0;
    /// <summary>Ortalama göz kırpma aralığı (s); 0 = kırpma yok.</summary>
    public double BlinkIntervalSeconds { get; set; } = 7;
    public double HitProbability { get; set; } = 0.92;
    public double FalseAlarmProbability { get; set; } = 0.04;
    public int Seed { get; set; } = 0;
    /// <summary>Sahte LSL saat başlangıcı (s).</summary>
    public double ClockStart { get; set; } = 10000;
}

/// <summary>
/// Cihazsız test için 7 kanallı (+AUX) gerçekçi EEG ve LSLMarkerSender dikkat testi marker dizisi üretir.
/// Arka plan: 1/f benzeri gürültü + değişken genlikli alfa + yavaş kayma + 50 Hz şebeke + göz kırpmaları.
/// Olaylar: N100, P200, N200 ve hedeflerde parietal ağırlıklı P300 (deneme başına gecikme/genlik sapması).
/// </summary>
public static class XonSimulationGenerator
{
    // Bileşenlerin kanal ağırlıkları (F3, F4, C3, Cz, C4, P3, P4)
    private static readonly double[] TopoN100 = { 0.7, 0.7, 0.85, 1.0, 0.85, 0.6, 0.6 };
    private static readonly double[] TopoN200 = { 0.9, 0.9, 0.9, 1.0, 0.9, 0.6, 0.6 };
    private static readonly double[] TopoP300 = { 0.40, 0.40, 0.70, 0.85, 0.70, 1.0, 1.0 };
    private static readonly double[] TopoAlpha = { 0.35, 0.35, 0.6, 0.6, 0.6, 1.0, 1.0 };
    private static readonly double[] TopoBlink = { 1.0, 1.0, 0.45, 0.5, 0.45, 0.18, 0.18 };

    public static EegRecordingSession Generate(SimulationOptions o)
    {
        var rng = o.Seed == 0 ? new Random() : new Random(o.Seed);
        double fs = o.SampleRate;
        var markers = new List<MarkerEvent>();
        var events = new List<(double t, bool target)>();

        // ---- Görev zaman çizelgesi (LSLMarkerSender AttentionTaskForm ile aynı sıra) ----
        double t = 1.0;
        markers.Add(new MarkerEvent(t, "task_start"));
        t += 0.5;
        if (o.RestSeconds > 0)
        {
            markers.Add(new MarkerEvent(t, "rest_start"));
            t += o.RestSeconds;
            markers.Add(new MarkerEvent(t, "rest_end"));
            t += 0.5;
        }
        markers.Add(new MarkerEvent(t, "target_color_" + o.TargetColor));
        t += 5.0;

        int nTarget = (int)Math.Round(o.Trials * o.TargetRatio);
        var isTarget = Enumerable.Range(0, o.Trials).Select(i => i < nTarget).OrderBy(_ => rng.Next()).ToArray();
        var others = o.Colors.Where(c => c != o.TargetColor).ToArray();

        foreach (bool target in isTarget)
        {
            string color = target ? o.TargetColor : others[rng.Next(others.Length)];
            markers.Add(new MarkerEvent(t, "color_onset_" + color));
            if (target) markers.Add(new MarkerEvent(t + 0.0005, "stimulus_start"));
            events.Add((t, target));

            if (target)
            {
                if (rng.NextDouble() < o.HitProbability)
                    markers.Add(new MarkerEvent(t + Clamp(Normal(rng, 0.48, 0.09), 0.25, 1.4), "response_correct"));
                else
                    markers.Add(new MarkerEvent(t + 1.5, "response_miss"));
            }
            else if (rng.NextDouble() < o.FalseAlarmProbability)
            {
                markers.Add(new MarkerEvent(t + Clamp(Normal(rng, 0.52, 0.1), 0.25, 1.4), "response_incorrect"));
            }
            if (target) markers.Add(new MarkerEvent(t + 1.0, "stimulus_end"));
            t += 1.5 + rng.NextDouble() * 0.3;
        }
        t += 0.5;
        markers.Add(new MarkerEvent(t, "task_end"));
        double duration = t + 3.0;

        // ---- Sürekli EEG ----
        int n = (int)(duration * fs);
        int nch = EegConstants.ChannelNames.Length;
        var data = new double[nch + 1][];
        for (int ch = 0; ch <= nch; ch++) data[ch] = new double[n];

        // Ortak (kaynak) bileşenler: kanallar arası korelasyon için
        var alphaEnv = SmoothNoise(rng, n, fs, 0.3);
        var restMask = new double[n];
        var rs = markers.FirstOrDefault(m => m.Value == "rest_start");
        var re = markers.FirstOrDefault(m => m.Value == "rest_end");
        if (rs != null && re != null)
            for (int i = (int)(rs.Timestamp * fs); i < Math.Min(n, (int)(re.Timestamp * fs)); i++) restMask[i] = 1;

        double alphaFreq = 9.5 + rng.NextDouble() * 1.5;
        for (int ch = 0; ch < nch; ch++)
        {
            var x = data[ch];
            double a1 = 0, a2 = 0, a3 = 0;
            double driftPhase = rng.NextDouble() * 2 * Math.PI, alphaPhase = rng.NextDouble() * 0.6;
            for (int i = 0; i < n; i++)
            {
                double tt = i / fs;
                // 1/f benzeri: farklı zaman sabitli AR(1) süreçlerinin toplamı
                a1 = 0.995 * a1 + Normal(rng, 0, 1.0);
                a2 = 0.95 * a2 + Normal(rng, 0, 1.6);
                a3 = 0.6 * a3 + Normal(rng, 0, 2.0);
                double bg = (0.35 * a1 + 0.9 * a2 + 1.0 * a3) * o.NoiseLevel;
                double alpha = (4 + 6 * Math.Max(0, alphaEnv[i]) + 8 * restMask[i]) * TopoAlpha[ch]
                               * Math.Sin(2 * Math.PI * alphaFreq * tt + alphaPhase);
                double drift = 12 * Math.Sin(2 * Math.PI * 0.07 * tt + driftPhase);
                double line = 2.5 * Math.Sin(2 * Math.PI * 50 * tt + ch);
                x[i] = bg + alpha + drift + line;
            }
        }

        // ---- Göz kırpmaları (artefakt reddini test etmek için) ----
        if (o.BlinkIntervalSeconds > 0)
        {
            double bt = 2 + rng.NextDouble() * o.BlinkIntervalSeconds;
            while (bt < duration - 1)
            {
                double amp = 90 + rng.NextDouble() * 80;
                AddGaussian(data, bt, 0.0, 0.07, amp, TopoBlink, fs, nch);
                bt += o.BlinkIntervalSeconds * (0.4 + rng.NextDouble() * 1.2);
            }
        }

        // ---- Olay ilişkili potansiyeller ----
        foreach (var (et, target) in events)
        {
            double gain = Clamp(Normal(rng, 1.0, 0.25), 0.4, 1.6);
            AddGaussian(data, et, 0.100 + Normal(rng, 0, 0.008), 0.022, -5.0 * gain, TopoN100, fs, nch);
            AddGaussian(data, et, 0.190 + Normal(rng, 0, 0.012), 0.028, 4.0 * gain, TopoN100, fs, nch);
            AddGaussian(data, et, 0.245 + Normal(rng, 0, 0.015), 0.025, (target ? -6.0 : -2.0) * gain, TopoN200, fs, nch);
            double p3Amp = target ? o.P300AmplitudeUv : o.P300AmplitudeUv * 0.22;
            double p3Lat = o.P300LatencyMs / 1000.0 + Normal(rng, 0, 0.03);
            AddGaussian(data, et, p3Lat, 0.060, p3Amp * gain, TopoP300, fs, nch);
        }

        // AUX: düşük genlikli gürültü (bipolar kanal temsili)
        for (int i = 0; i < n; i++) data[nch][i] = Normal(rng, 0, 2);

        var session = new EegRecordingSession
        {
            ParticipantId = "DEMO-001",
            ParticipantName = "Simülasyon Katılımcısı",
            SourceDescription = $"Simülasyon ({o.Trials} deneme, hedef %{o.TargetRatio * 100:0})",
            DeviceName = "X.on Simülatör",
            SampleRate = fs,
            ChannelNames = EegConstants.ChannelNames.Append(EegConstants.AuxChannelName).ToList(),
            Data = data,
            Timestamps = Enumerable.Range(0, n).Select(i => o.ClockStart + i / fs).ToArray(),
        };
        foreach (var m in markers) m.Timestamp += o.ClockStart;
        session.Markers = markers;
        return session;
    }

    private static void AddGaussian(double[][] data, double onset, double latency, double sigma, double amp,
        double[] topo, double fs, int nch)
    {
        double center = onset + latency;
        int from = Math.Max(0, (int)((center - 4 * sigma) * fs));
        int to = Math.Min(data[0].Length - 1, (int)((center + 4 * sigma) * fs));
        for (int i = from; i <= to; i++)
        {
            double d = (i / fs - center) / sigma;
            double g = amp * Math.Exp(-0.5 * d * d);
            for (int ch = 0; ch < nch; ch++) data[ch][i] += g * topo[ch];
        }
    }

    /// <summary>Yavaş değişen, yaklaşık [-1,1] aralığında düzgün gürültü (zarf için).</summary>
    private static double[] SmoothNoise(Random rng, int n, double fs, double hz)
    {
        var y = new double[n];
        double a = Math.Exp(-2 * Math.PI * hz / fs), v = 0, scale = Math.Sqrt(1 - a * a);
        for (int i = 0; i < n; i++)
        {
            v = a * v + scale * Normal(rng, 0, 1);
            y[i] = v;
        }
        return y;
    }

    public static double Normal(Random rng, double mean, double sd)
    {
        double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
        return mean + sd * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));
}
