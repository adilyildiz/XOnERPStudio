using XOnERPStudio.Models;

namespace XOnERPStudio.SignalProcessing;

/// <summary>FFT, Welch güç spektral yoğunluğu (PSD) ve EEG frekans bandı güçleri.</summary>
public static class FrequencyAnalysis
{
    public record Band(string Name, double Low, double High);

    public static readonly Band[] Bands =
    {
        new("Delta", 1, 4),
        new("Theta", 4, 8),
        new("Alpha", 8, 13),
        new("Beta", 13, 30),
    };

    /// <summary>Yerinde radix-2 Cooley–Tukey FFT. Uzunluk 2'nin kuvveti olmalı.</summary>
    public static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    double tr = re[b] * cr - im[b] * ci;
                    double ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    double ncr = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = ncr;
                }
            }
        }
    }

    /// <summary>
    /// Welch PSD: Hann pencereli, %50 örtüşmeli segmentlerin ortalama periodogramı (tek taraflı, µV²/Hz).
    /// Verilen [start, start+count) aralıkları üzerinde hesaplanır.
    /// </summary>
    public static double[] WelchPsd(double[] x, IEnumerable<(int start, int count)> ranges, double fs, int nfft, out double[] freqs)
    {
        int half = nfft / 2 + 1;
        freqs = new double[half];
        for (int k = 0; k < half; k++) freqs[k] = k * fs / nfft;

        var win = new double[nfft];
        double winPow = 0;
        for (int i = 0; i < nfft; i++)
        {
            win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (nfft - 1));
            winPow += win[i] * win[i];
        }

        var psd = new double[half];
        int segments = 0;
        var re = new double[nfft];
        var im = new double[nfft];
        int step = nfft / 2;

        foreach (var (start, count) in ranges)
        {
            for (int s = start; s + nfft <= start + count; s += step)
            {
                double mean = 0;
                for (int i = 0; i < nfft; i++) mean += x[s + i];
                mean /= nfft;
                for (int i = 0; i < nfft; i++) { re[i] = (x[s + i] - mean) * win[i]; im[i] = 0; }
                Fft(re, im);
                for (int k = 0; k < half; k++)
                {
                    double p = (re[k] * re[k] + im[k] * im[k]) / (fs * winPow);
                    if (k != 0 && k != nfft / 2) p *= 2;
                    psd[k] += p;
                }
                segments++;
            }
        }
        if (segments > 0)
            for (int k = 0; k < half; k++) psd[k] /= segments;
        return psd;
    }

    /// <summary>Tüm kanallar için spektrum ve bant güçlerini hesaplar.</summary>
    public static SpectrumResult Compute(string label, double[][] data, IList<string> channelNames, double fs,
        IEnumerable<(int start, int count)> ranges, double segmentSeconds = 2.0)
    {
        int nfft = 1;
        while (nfft < fs * segmentSeconds) nfft <<= 1;
        var rangeList = ranges.Where(r => r.count >= nfft).ToList();
        if (rangeList.Count == 0) return null;

        int chCount = Math.Min(data.Length, EegConstants.ChannelNames.Length);
        var result = new SpectrumResult
        {
            Label = label,
            Power = new double[chCount][],
            BandPower = new double[chCount][],
            BandRelative = new double[chCount][],
            SegmentSeconds = nfft / fs,
        };

        double[] freqs = null;
        for (int ch = 0; ch < chCount; ch++)
        {
            result.Power[ch] = WelchPsd(data[ch], rangeList, fs, nfft, out freqs);
            var bp = new double[Bands.Length];
            double df = freqs.Length > 1 ? freqs[1] - freqs[0] : 1;
            for (int b = 0; b < Bands.Length; b++)
                for (int k = 0; k < freqs.Length; k++)
                    if (freqs[k] >= Bands[b].Low && freqs[k] < Bands[b].High)
                        bp[b] += result.Power[ch][k] * df;
            double total = bp.Sum();
            result.BandPower[ch] = bp;
            result.BandRelative[ch] = bp.Select(v => total > 0 ? v / total : 0).ToArray();
        }
        result.Frequencies = freqs;
        return result;
    }

    /// <summary>f0 ± halfWidth Hz aralığındaki gücün 1..(fs/2) toplam güce oranı.</summary>
    public static double RelativePowerAround(double[] psd, double[] freqs, double f0, double halfWidth)
    {
        double band = 0, total = 0;
        for (int k = 0; k < freqs.Length; k++)
        {
            if (freqs[k] < 1) continue;
            total += psd[k];
            if (Math.Abs(freqs[k] - f0) <= halfWidth) band += psd[k];
        }
        return total > 0 ? band / total : 0;
    }
}
