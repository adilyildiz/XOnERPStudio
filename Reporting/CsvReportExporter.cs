using System.Globalization;
using System.Text;
using XOnERPStudio.IO;
using XOnERPStudio.Models;
using XOnERPStudio.SignalProcessing;

namespace XOnERPStudio.Reporting;

/// <summary>
/// SPSS / R / Excel uyumlu istatistik dökümü. Uzun (long) formatta, nokta ondalık ayırıcılı,
/// virgül ayraçlı, UTF-8 (BOM'lu) CSV dosyaları üretir.
/// </summary>
public static class CsvReportExporter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string N(double v, string fmt = "0.###") => double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString(fmt, Inv);
    private static string Q(string s) => SessionExporter.Quote(s ?? "");

    /// <summary>Tüm CSV dosyalarını klasöre yazar; oluşturulan dosya yollarını döner.</summary>
    public static List<string> ExportAll(ErpAnalysisResult r, string folder)
    {
        Directory.CreateDirectory(folder);
        string id = string.Join("_", (string.IsNullOrWhiteSpace(r.Session.ParticipantId) ? "oturum" : r.Session.ParticipantId)
            .Split(Path.GetInvalidFileNameChars()));
        var files = new List<string>
        {
            Path.Combine(folder, $"{id}_erp_bilesenler.csv"),
            Path.Combine(folder, $"{id}_davranis_denemeler.csv"),
            Path.Combine(folder, $"{id}_davranis_ozet.csv"),
            Path.Combine(folder, $"{id}_erp_dalgalar.csv"),
            Path.Combine(folder, $"{id}_epochlar.csv"),
            Path.Combine(folder, $"{id}_bant_gucu.csv"),
        };
        ExportComponents(r, files[0]);
        ExportTrials(r, files[1]);
        ExportBehaviorSummary(r, files[2]);
        ExportWaveforms(r, files[3]);
        ExportEpochLog(r, files[4]);
        ExportBandPower(r, files[5]);
        return files;
    }

    private static StreamWriter Open(string path) => new(path, false, new UTF8Encoding(true));

    /// <summary>participant, channel, condition, component, latency_ms, amplitude_uv, ...</summary>
    public static void ExportComponents(ErpAnalysisResult r, string path)
    {
        using var w = Open(path);
        w.WriteLine("participant,channel,condition,component,latency_ms,amplitude_uv,at_window_edge,n_epochs,snr,baseline_rms_uv");
        foreach (var c in r.Channels.Values)
            foreach (var wave in new[] { c.Target, c.Standard, c.Difference })
            {
                if (wave is not { IsEmpty: false }) continue;
                foreach (var p in new[] { wave.N100, wave.N200, wave.P300 })
                    w.WriteLine(string.Join(",", Q(r.Session.ParticipantId), Q(c.ChannelName), Q(wave.ConditionName), p.Name,
                        p.Found ? N(p.LatencyMs, "0.#") : "", p.Found ? N(p.AmplitudeUv) : "", p.AtWindowEdge ? 1 : 0,
                        wave.EpochCount, N(wave.Snr, "0.##"), N(wave.BaselineRms)));
            }
    }

    public static void ExportTrials(ErpAnalysisResult r, string path)
    {
        using var w = Open(path);
        w.WriteLine("participant,trial,time_s,marker,color,is_target,outcome,rt_ms,epoch_rejected");
        foreach (var t in r.Behavior.Trials)
            w.WriteLine(string.Join(",", Q(r.Session.ParticipantId), t.Index,
                N(t.Stimulus.Timestamp - r.Session.StartTime, "0.0000"), Q(t.Stimulus.Value), Q(t.ColorName),
                t.IsTarget ? 1 : 0, t.Outcome, t.ReactionTimeMs.HasValue ? N(t.ReactionTimeMs.Value, "0.#") : "",
                t.EpochRejected ? 1 : 0));
    }

    public static void ExportBehaviorSummary(ErpAnalysisResult r, string path)
    {
        var b = r.Behavior;
        using var w = Open(path);
        w.WriteLine("participant,target_color,trials,targets,distractors,hits,misses,false_alarms,correct_rejections,hit_rate,fa_rate,accuracy,mean_rt_ms,sd_rt_ms,median_rt_ms,d_prime,criterion_c,target_epochs_accepted,standard_epochs_accepted");
        w.WriteLine(string.Join(",", Q(r.Session.ParticipantId), Q(b.TargetColor), b.TotalTrials, b.TargetCount, b.DistractorCount,
            b.Hits, b.Misses, b.FalseAlarms, b.CorrectRejections, N(b.HitRate, "0.####"), N(b.FalseAlarmRate, "0.####"),
            N(b.Accuracy, "0.####"), N(b.MeanRtMs, "0.#"), N(b.SdRtMs, "0.#"), N(b.MedianRtMs, "0.#"), N(b.DPrime), N(b.Criterion),
            r.TargetEpochsAccepted, r.StandardEpochsAccepted));
    }

    /// <summary>Geniş format: time_ms + her kanal/koşul için ortalama ve SEM sütunları.</summary>
    public static void ExportWaveforms(ErpAnalysisResult r, string path)
    {
        var cols = new List<(string name, ErpWaveform w)>();
        foreach (var c in r.Channels.Values)
        {
            if (c.Target is { IsEmpty: false }) cols.Add(($"{c.ChannelName}_target", c.Target));
            if (c.Standard is { IsEmpty: false }) cols.Add(($"{c.ChannelName}_standard", c.Standard));
            if (c.Difference is { IsEmpty: false }) cols.Add(($"{c.ChannelName}_diff", c.Difference));
        }
        using var w = Open(path);
        if (cols.Count == 0) { w.WriteLine("time_ms"); return; }
        w.WriteLine("time_ms," + string.Join(",", cols.Select(c => $"{c.name}_mean,{c.name}_sem")));
        var times = cols[0].w.TimesMs;
        for (int i = 0; i < times.Length; i++)
            w.WriteLine(N(times[i], "0.##") + "," + string.Join(",", cols.Select(c => $"{N(c.w.Mean[i], "0.####")},{N(c.w.Sem[i], "0.####")}")));
    }

    public static void ExportEpochLog(ErpAnalysisResult r, string path)
    {
        using var w = Open(path);
        w.WriteLine("epoch,time_s,marker,condition,rejected,reason," + string.Join(",", EegConstants.ChannelNames.Select(c => $"{c}_maxabs_uv,{c}_p2p_uv")));
        int k = 1;
        foreach (var e in r.Epochs)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(",", k++, N(e.Trigger.Timestamp - r.Session.StartTime, "0.0000"), Q(e.Trigger.Value), e.Condition,
                e.Rejected ? 1 : 0, Q(e.RejectReason)));
            for (int ch = 0; ch < e.MaxAbs.Length; ch++) sb.Append($",{N(e.MaxAbs[ch], "0.#")},{N(e.PeakToPeak[ch], "0.#")}");
            w.WriteLine(sb.ToString());
        }
    }

    public static void ExportBandPower(ErpAnalysisResult r, string path)
    {
        using var w = Open(path);
        w.WriteLine("participant,segment,channel,band,low_hz,high_hz,power_uv2,relative");
        foreach (var spec in new[] { r.Spectrum, r.RestSpectrum })
        {
            if (spec == null) continue;
            for (int ch = 0; ch < spec.BandPower.Length; ch++)
                for (int b = 0; b < FrequencyAnalysis.Bands.Length; b++)
                {
                    var band = FrequencyAnalysis.Bands[b];
                    w.WriteLine(string.Join(",", Q(r.Session.ParticipantId), Q(spec.Label), EegConstants.ChannelNames[ch], band.Name,
                        N(band.Low), N(band.High), N(spec.BandPower[ch][b], "0.####"), N(spec.BandRelative[ch][b], "0.####")));
                }
        }
    }
}
