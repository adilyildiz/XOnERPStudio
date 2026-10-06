using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using XOnERPStudio.Models;

namespace XOnERPStudio.IO;

/// <summary>Oturum verisini CSV (EEG + marker) ve JSON (üst veri + markerlar) olarak dışa aktarır.</summary>
public static class SessionExporter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Birleşik CSV: timestamp, kanallar..., marker. Bu dosya CsvDataReader.LoadEeg ile geri okunabilir.
    /// </summary>
    public static void ExportCombinedCsv(EegRecordingSession s, string path, double[][] dataOverride = null)
    {
        var data = dataOverride ?? s.Data;
        var markerAt = new Dictionary<int, List<string>>();
        foreach (var m in s.Markers)
        {
            int i = s.IndexOfTime(m.Timestamp);
            if (i < 0) continue;
            if (!markerAt.TryGetValue(i, out var l)) markerAt[i] = l = new List<string>();
            l.Add(m.Value);
        }

        using var w = new StreamWriter(path, false, new UTF8Encoding(true));
        w.WriteLine("timestamp," + string.Join(",", s.ChannelNames) + ",marker");
        var sb = new StringBuilder();
        for (int i = 0; i < s.SampleCount; i++)
        {
            sb.Clear();
            sb.Append(s.Timestamps[i].ToString("F6", Inv));
            for (int ch = 0; ch < data.Length; ch++) sb.Append(',').Append(data[ch][i].ToString("F3", Inv));
            sb.Append(',');
            if (markerAt.TryGetValue(i, out var ms)) sb.Append('"').Append(string.Join("|", ms).Replace("\"", "'")).Append('"');
            w.WriteLine(sb.ToString());
        }
    }

    public static void ExportMarkersCsv(EegRecordingSession s, string path)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(true));
        w.WriteLine("timestamp,relative_s,marker,category,condition");
        foreach (var m in s.Markers)
            w.WriteLine(string.Join(",",
                m.Timestamp.ToString("F6", Inv),
                (m.Timestamp - s.StartTime).ToString("F4", Inv),
                Quote(m.Value), m.Category, m.Condition));
    }

    public static void ExportJson(EegRecordingSession s, string path, ErpAnalysisResult result = null)
    {
        var obj = new
        {
            s.ParticipantId,
            s.ParticipantName,
            s.ParticipantAge,
            s.Notes,
            s.RecordedAt,
            s.DeviceName,
            s.SourceDescription,
            s.SampleRate,
            s.ChannelNames,
            Samples = s.SampleCount,
            DurationSeconds = s.DurationSeconds,
            s.ImportNotes,
            Markers = s.Markers.Select(m => new { m.Timestamp, m.Value, Category = m.Category.ToString(), Condition = m.Condition.ToString() }),
            Analysis = result == null ? null : new
            {
                result.AnalyzedAt,
                result.Settings,
                result.TargetEpochsAccepted,
                result.TargetEpochsTotal,
                result.StandardEpochsAccepted,
                result.StandardEpochsTotal,
                Behavior = new
                {
                    result.Behavior.TargetColor, result.Behavior.TotalTrials, result.Behavior.TargetCount,
                    result.Behavior.DistractorCount, result.Behavior.Hits, result.Behavior.Misses,
                    result.Behavior.FalseAlarms, result.Behavior.CorrectRejections,
                    result.Behavior.MeanRtMs, result.Behavior.SdRtMs, result.Behavior.DPrime, result.Behavior.Criterion,
                },
                Channels = result.Channels.Values.Select(c => new
                {
                    c.ChannelName,
                    Target = Peaks(c.Target),
                    Standard = Peaks(c.Standard),
                    Difference = Peaks(c.Difference),
                }),
                result.Warnings,
            },
        };
        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            FloatFormatHandling = FloatFormatHandling.Symbol,
        };
        File.WriteAllText(path, JsonConvert.SerializeObject(obj, settings), new UTF8Encoding(false));
    }

    private static object Peaks(ErpWaveform w) => w == null || w.IsEmpty ? null : new
    {
        w.EpochCount, w.Snr,
        P300 = new { w.P300.LatencyMs, w.P300.AmplitudeUv },
        N200 = new { w.N200.LatencyMs, w.N200.AmplitudeUv },
        N100 = new { w.N100.LatencyMs, w.N100.AmplitudeUv },
    };

    internal static string Quote(string s) =>
        s.IndexOfAny(new[] { ',', '"', '\n', ';' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
