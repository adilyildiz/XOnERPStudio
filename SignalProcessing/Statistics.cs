namespace XOnERPStudio.SignalProcessing;

/// <summary>Temel istatistik yardımcıları.</summary>
public static class Statistics
{
    public static double Mean(IReadOnlyList<double> x)
    {
        if (x.Count == 0) return double.NaN;
        double s = 0;
        for (int i = 0; i < x.Count; i++) s += x[i];
        return s / x.Count;
    }

    /// <summary>Örneklem standart sapması (n−1).</summary>
    public static double StdDev(IReadOnlyList<double> x)
    {
        if (x.Count < 2) return double.NaN;
        double m = Mean(x), s = 0;
        for (int i = 0; i < x.Count; i++) s += (x[i] - m) * (x[i] - m);
        return Math.Sqrt(s / (x.Count - 1));
    }

    public static double Median(IReadOnlyList<double> x)
    {
        if (x.Count == 0) return double.NaN;
        var a = x.OrderBy(v => v).ToArray();
        int n = a.Length;
        return n % 2 == 1 ? a[n / 2] : 0.5 * (a[n / 2 - 1] + a[n / 2]);
    }

    public static double Rms(double[] x, int start, int count)
    {
        if (count <= 0) return 0;
        double s = 0;
        for (int i = start; i < start + count; i++) s += x[i] * x[i];
        return Math.Sqrt(s / count);
    }

    /// <summary>
    /// Standart normal dağılımın ters kümülatif fonksiyonu (Acklam algoritması, |hata| &lt; 1.2e-9).
    /// </summary>
    public static double InverseNormalCdf(double p)
    {
        if (p <= 0) return double.NegativeInfinity;
        if (p >= 1) return double.PositiveInfinity;

        double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
        double[] b = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
        double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
        double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };
        const double pLow = 0.02425, pHigh = 1 - pLow;

        if (p < pLow)
        {
            double q = Math.Sqrt(-2 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                   ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        if (p <= pHigh)
        {
            double q = p - 0.5, r = q * q;
            return (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q /
                   (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
        }
        {
            double q = Math.Sqrt(-2 * Math.Log(1 - p));
            return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                    ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
    }
}
