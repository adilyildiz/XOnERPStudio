namespace XOnERPStudio.Models;

/// <summary>Bir ERP bileşeninin tespit edilen tepe değeri.</summary>
public class ComponentPeak
{
    public string Name { get; set; }
    public bool Found { get; set; }
    public double LatencyMs { get; set; }
    public double AmplitudeUv { get; set; }

    /// <summary>Tepe, arama penceresinin kenarına düştüyse gerçek bir tepe olmayabilir.</summary>
    public bool AtWindowEdge { get; set; }

    public override string ToString() =>
        Found ? $"{Name}: {LatencyMs:0} ms, {AmplitudeUv:0.0} µV" : $"{Name}: —";
}

/// <summary>Bir kanal ve koşul için ortalama ERP dalgası, SEM ve tepe değerleri.</summary>
public class ErpWaveform
{
    public string ChannelName { get; set; }
    public string ConditionName { get; set; }

    /// <summary>Her örneğin uyarana göre zamanı (ms).</summary>
    public double[] TimesMs { get; set; }
    public double[] Mean { get; set; }
    public double[] Sem { get; set; }

    /// <summary>Ortalamaya giren epoch sayısı.</summary>
    public int EpochCount { get; set; }

    public ComponentPeak P300 { get; set; } = new() { Name = "P300" };
    public ComponentPeak N200 { get; set; } = new() { Name = "N200" };
    public ComponentPeak N100 { get; set; } = new() { Name = "N100" };

    /// <summary>P300 genliği / baseline RMS.</summary>
    public double Snr { get; set; }

    public double BaselineRms { get; set; }

    public bool IsEmpty => Mean == null || Mean.Length == 0 || EpochCount == 0;

    public int IndexOfTime(double ms)
    {
        if (TimesMs == null || TimesMs.Length == 0) return -1;
        int best = 0;
        double bestD = double.MaxValue;
        for (int i = 0; i < TimesMs.Length; i++)
        {
            double d = Math.Abs(TimesMs[i] - ms);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }
}

/// <summary>Bir kanalın hedef, standart ve fark dalgaları.</summary>
public class ChannelErp
{
    public string ChannelName { get; set; }
    public ErpWaveform Target { get; set; }
    public ErpWaveform Standard { get; set; }
    public ErpWaveform Difference { get; set; }
}
