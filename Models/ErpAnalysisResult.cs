namespace XOnERPStudio.Models;

public enum TrialOutcome
{
    Hit,
    Miss,
    FalseAlarm,
    CorrectRejection,
}

/// <summary>Dikkat testindeki tek bir deneme ve davranışsal sonucu.</summary>
public class TrialResult
{
    public int Index { get; set; }
    public MarkerEvent Stimulus { get; set; }
    public bool IsTarget { get; set; }
    public string ColorName { get; set; }
    public TrialOutcome Outcome { get; set; }

    /// <summary>Tepki süresi (ms); tepki yoksa null.</summary>
    public double? ReactionTimeMs { get; set; }

    /// <summary>Bu denemenin epoch'u artefakt nedeniyle reddedildi mi?</summary>
    public bool EpochRejected { get; set; }

    public static string OutcomeName(TrialOutcome o) => o switch
    {
        TrialOutcome.Hit => "Doğru (Hit)",
        TrialOutcome.Miss => "Kaçırma (Miss)",
        TrialOutcome.FalseAlarm => "Hatalı (FA)",
        _ => "Doğru Ret (CR)",
    };
}

/// <summary>Dikkat testi davranışsal özet istatistikleri ve sinyal tespit kuramı ölçüleri.</summary>
public class BehaviorStats
{
    public bool HasResponseMarkers { get; set; }
    public string TargetColor { get; set; }
    public int TotalTrials { get; set; }
    public int TargetCount { get; set; }
    public int DistractorCount { get; set; }
    public int Hits { get; set; }
    public int Misses { get; set; }
    public int FalseAlarms { get; set; }
    public int CorrectRejections { get; set; }

    public double HitRate => TargetCount > 0 ? (double)Hits / TargetCount : double.NaN;
    public double MissRate => TargetCount > 0 ? (double)Misses / TargetCount : double.NaN;
    public double FalseAlarmRate => DistractorCount > 0 ? (double)FalseAlarms / DistractorCount : double.NaN;
    public double Accuracy => TotalTrials > 0 ? (double)(Hits + CorrectRejections) / TotalTrials : double.NaN;

    /// <summary>Doğru tepkilerin ortalama tepki süresi (ms).</summary>
    public double MeanRtMs { get; set; } = double.NaN;
    public double SdRtMs { get; set; } = double.NaN;
    public double MedianRtMs { get; set; } = double.NaN;

    /// <summary>d' = Z(H) − Z(FA), log-doğrusal düzeltmeli (Hautus, 1995).</summary>
    public double DPrime { get; set; } = double.NaN;

    /// <summary>Tepki eğilimi c = −(Z(H) + Z(FA)) / 2.</summary>
    public double Criterion { get; set; } = double.NaN;

    public List<TrialResult> Trials { get; set; } = new();
}

/// <summary>Kanal bazında sinyal kalitesi (empedans yerine sinyalden kestirilir).</summary>
public class ChannelQuality
{
    public string ChannelName { get; set; }
    /// <summary>Yüksek geçiren sonrası RMS (µV).</summary>
    public double RmsUv { get; set; }
    /// <summary>Şebeke frekansı çevresindeki gücün toplam güce oranı (%).</summary>
    public double LineNoisePercent { get; set; }
    public bool IsFlat { get; set; }
    /// <summary>Bu kanalda reddedilen epoch oranı (%).</summary>
    public double RejectedEpochPercent { get; set; }
    public QualityLevel Level { get; set; }
}

public enum QualityLevel { Good, Fair, Poor }

/// <summary>Güç spektral yoğunluğu (Welch) ve frekans bandı güçleri.</summary>
public class SpectrumResult
{
    public string Label { get; set; }
    public double[] Frequencies { get; set; }
    /// <summary>Power[kanal][frekans] — µV²/Hz.</summary>
    public double[][] Power { get; set; }
    /// <summary>BandPower[kanal][bant] — µV² (mutlak).</summary>
    public double[][] BandPower { get; set; }
    /// <summary>BandRelative[kanal][bant] — 0..1 (bantların toplamına göre).</summary>
    public double[][] BandRelative { get; set; }
    public double SegmentSeconds { get; set; }
}

/// <summary>Bir analiz çalışmasının tüm çıktıları.</summary>
public class ErpAnalysisResult
{
    public EegRecordingSession Session { get; set; }
    public ErpFilterSettings Settings { get; set; }
    public DateTime AnalyzedAt { get; set; } = DateTime.Now;

    /// <summary>Filtrelenmiş sürekli sinyal (Session.Data ile aynı boyutta).</summary>
    public double[][] FilteredData { get; set; }

    public List<ErpEpoch> Epochs { get; set; } = new();

    /// <summary>Kanal adı → hedef/standart/fark dalgaları (7 kanal + sanal Pz*).</summary>
    public Dictionary<string, ChannelErp> Channels { get; set; } = new();

    public BehaviorStats Behavior { get; set; } = new();
    public List<ChannelQuality> Quality { get; set; } = new();
    public SpectrumResult Spectrum { get; set; }
    /// <summary>Dinlenme (rest_start..rest_end) bölümünün spektrumu; dinlenme yoksa null.</summary>
    public SpectrumResult RestSpectrum { get; set; }

    public List<string> Warnings { get; set; } = new();

    public int TargetEpochsTotal => Epochs.Count(e => e.Condition == ErpCondition.Target);
    public int StandardEpochsTotal => Epochs.Count(e => e.Condition == ErpCondition.Standard);
    public int TargetEpochsAccepted => Epochs.Count(e => e.Condition == ErpCondition.Target && !e.Rejected);
    public int StandardEpochsAccepted => Epochs.Count(e => e.Condition == ErpCondition.Standard && !e.Rejected);

    public ChannelErp Get(string channel) => Channels.TryGetValue(channel, out var c) ? c : null;

    /// <summary>P300'ün en büyük olduğu kanal (hedef dalgasında).</summary>
    public ChannelErp BestP300Channel()
    {
        ChannelErp best = null;
        foreach (var name in EegConstants.ChannelNames)
        {
            var c = Get(name);
            if (c?.Target == null || !c.Target.P300.Found) continue;
            if (best == null || c.Target.P300.AmplitudeUv > best.Target.P300.AmplitudeUv) best = c;
        }
        return best;
    }
}
