using XOnERPStudio.Models;

namespace XOnERPStudio.SignalProcessing;

/// <summary>İkinci dereceden IIR bölüm (biquad), a0 = 1 olacak şekilde normalize edilmiş.</summary>
public readonly struct Biquad
{
    public readonly double B0, B1, B2, A1, A2;

    public Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        B0 = b0 / a0; B1 = b1 / a0; B2 = b2 / a0; A1 = a1 / a0; A2 = a2 / a0;
    }

    /// <summary>DC kazancı H(z=1).</summary>
    public double DcGain => (B0 + B1 + B2) / (1 + A1 + A2);

    // RBJ Audio EQ Cookbook — iki doğrusal dönüşüm (bilinear), kesim frekansında ön-bükme dahil.
    public static Biquad LowPass(double fc, double fs, double q)
    {
        double w0 = 2 * Math.PI * fc / fs, cos = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * q);
        return new Biquad((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public static Biquad HighPass(double fc, double fs, double q)
    {
        double w0 = 2 * Math.PI * fc / fs, cos = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * q);
        return new Biquad((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    /// <summary>Çentik (notch) rezonatörü; Q = f0 / bant genişliği.</summary>
    public static Biquad Notch(double f0, double fs, double q)
    {
        double w0 = 2 * Math.PI * f0 / fs, cos = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * q);
        return new Biquad(1, -2 * cos, 1, 1 + alpha, -2 * cos, 1 - alpha);
    }
}

/// <summary>
/// Sıfır fazlı (ileri-geri) Butterworth bant geçiren ve çentik filtreleme, baseline düzeltme.
/// </summary>
public static class DigitalFilter
{
    /// <summary>Butterworth bölümlerinin Q değerleri (derece 2 ve 4 için).</summary>
    private static double[] ButterworthQs(int order) => order switch
    {
        4 => new[] { 0.54119610, 1.30656296 },
        _ => new[] { 1 / Math.Sqrt(2) },
    };

    /// <summary>Ayarlara göre biquad zincirini oluşturur (HP + LP + çentik).</summary>
    public static Biquad[] DesignChain(ErpFilterSettings s, double fs)
    {
        var list = new List<Biquad>();
        if (s.BandpassEnabled)
        {
            foreach (var q in ButterworthQs(s.FilterOrder)) list.Add(Biquad.HighPass(s.HighPassHz, fs, q));
            foreach (var q in ButterworthQs(s.FilterOrder)) list.Add(Biquad.LowPass(s.LowPassHz, fs, q));
        }
        if (s.NotchEnabled && s.NotchHz < fs / 2)
            list.Add(Biquad.Notch(s.NotchHz, fs, s.NotchQ));
        return list.ToArray();
    }

    /// <summary>Tek yönlü (nedensel) filtre; Direct Form II Transposed. z1/z2 başlangıç durumları.</summary>
    public static void FilterInPlace(double[] x, Biquad s, double z1 = 0, double z2 = 0)
    {
        for (int n = 0; n < x.Length; n++)
        {
            double xn = x[n];
            double y = s.B0 * xn + z1;
            z1 = s.B1 * xn - s.A1 * y + z2;
            z2 = s.B2 * xn - s.A2 * y;
            x[n] = y;
        }
    }

    /// <summary>Sabit c girişi için kararlı durum (lfilter_zi benzeri) başlangıç değerleri.</summary>
    private static (double z1, double z2) SteadyState(Biquad s, double c)
    {
        double h = s.DcGain * c;
        double z2 = (s.B2 * c) - s.A2 * h;
        double z1 = (s.B1 * c) - s.A1 * h + z2;
        return (z1, z2);
    }

    /// <summary>
    /// FiltFilt: sinyal tek-simetrik yansıtma ile uzatılır, filtre ileri ve geri uygulanır.
    /// Sonuç sıfır faz gecikmelidir (ERP tepe gecikmeleri kaymaz).
    /// </summary>
    public static double[] FiltFilt(double[] x, Biquad[] chain, int padLength)
    {
        int n = x.Length;
        if (n == 0 || chain.Length == 0) return (double[])x.Clone();
        int pad = Math.Clamp(padLength, 0, n - 1);

        var ext = new double[n + 2 * pad];
        double x0 = x[0], xn = x[n - 1];
        for (int i = 0; i < pad; i++)
        {
            ext[i] = 2 * x0 - x[pad - i];
            ext[pad + n + i] = 2 * xn - x[n - 2 - i];
        }
        Array.Copy(x, 0, ext, pad, n);

        foreach (var s in chain)
        {
            var (z1, z2) = SteadyState(s, ext[0]);
            FilterInPlace(ext, s, z1, z2);
        }
        Array.Reverse(ext);
        foreach (var s in chain)
        {
            var (z1, z2) = SteadyState(s, ext[0]);
            FilterInPlace(ext, s, z1, z2);
        }
        Array.Reverse(ext);

        var y = new double[n];
        Array.Copy(ext, pad, y, 0, n);
        return y;
    }

    /// <summary>Tüm kanalları paralel olarak sıfır fazlı filtreler.</summary>
    public static double[][] FilterChannels(double[][] data, ErpFilterSettings s, double fs)
    {
        var chain = DesignChain(s, fs);
        // Yüksek geçiren geçici rejiminin oturması için ~3 zaman sabiti kadar dolgu
        int pad = s.BandpassEnabled ? (int)Math.Ceiling(3.0 * fs / Math.Max(0.05, s.HighPassHz)) : (int)(fs * 2);
        pad = Math.Min(pad, (int)(fs * 10));

        var result = new double[data.Length][];
        Parallel.For(0, data.Length, ch =>
        {
            var x = (double[])data[ch].Clone();
            RemoveMean(x);
            result[ch] = FiltFilt(x, chain, pad);
        });
        return result;
    }

    public static void RemoveMean(double[] x)
    {
        if (x.Length == 0) return;
        double m = 0;
        foreach (var v in x) m += v;
        m /= x.Length;
        for (int i = 0; i < x.Length; i++) x[i] -= m;
    }

    /// <summary>x[t] − mean(x[baseStart..baseEnd)) — baseline düzeltme.</summary>
    public static void BaselineCorrect(double[] x, int baseStart, int baseEnd)
    {
        baseStart = Math.Clamp(baseStart, 0, x.Length);
        baseEnd = Math.Clamp(baseEnd, baseStart, x.Length);
        if (baseEnd <= baseStart) return;
        double m = 0;
        for (int i = baseStart; i < baseEnd; i++) m += x[i];
        m /= (baseEnd - baseStart);
        for (int i = 0; i < x.Length; i++) x[i] -= m;
    }
}

/// <summary>Canlı görüntüleme için durum tutan nedensel (gerçek zamanlı) çok kanallı filtre.</summary>
public class StreamingFilter
{
    private readonly Biquad[] _chain;
    private readonly double[,] _z1, _z2;

    public StreamingFilter(ErpFilterSettings s, double fs, int channels)
    {
        _chain = DigitalFilter.DesignChain(s, fs);
        _z1 = new double[channels, _chain.Length];
        _z2 = new double[channels, _chain.Length];
    }

    public double Process(int ch, double x)
    {
        for (int k = 0; k < _chain.Length; k++)
        {
            var s = _chain[k];
            double y = s.B0 * x + _z1[ch, k];
            _z1[ch, k] = s.B1 * x - s.A1 * y + _z2[ch, k];
            _z2[ch, k] = s.B2 * x - s.A2 * y;
            x = y;
        }
        return x;
    }
}
