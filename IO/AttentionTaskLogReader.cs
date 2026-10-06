using XOnERPStudio.Models;
using XOnERPStudio.SignalProcessing;

namespace XOnERPStudio.IO;

/// <summary>
/// LSLMarkerSender dikkat testinin (oddball) deneme sonuçlarını marker akışından yeniden kurar.
/// Uygulama ayrı bir log dosyası yazmadığı için tek güvenilir kaynak marker dizisidir:
/// her tepki markerı (response_correct / response_incorrect / response_miss) kendisinden önceki
/// en yakın uyarana, yanıt penceresi içinde kalıyorsa atanır.
/// </summary>
public static class AttentionTaskLogReader
{
    /// <summary>Tepki markerı uyaran penceresinin biraz dışında gelirse kabul edilecek pay (s).</summary>
    private const double SlackSeconds = 0.25;

    /// <param name="markers">MarkerClassifier ile sınıflandırılmış, zamana göre sıralı markerlar.</param>
    public static BehaviorStats Build(List<MarkerEvent> markers, ErpFilterSettings settings, string targetColor)
    {
        var stats = new BehaviorStats { TargetColor = targetColor };
        var stimuli = markers.Where(m => m.IsEpochTrigger).ToList();
        var responses = markers.Where(m => m.Category is MarkerCategory.ResponseHit
            or MarkerCategory.ResponseFalseAlarm or MarkerCategory.ResponseMiss).ToList();
        stats.HasResponseMarkers = responses.Count > 0;

        var trials = stimuli.Select((s, i) => new TrialResult
        {
            Index = i + 1,
            Stimulus = s,
            IsTarget = s.Condition == ErpCondition.Target,
            ColorName = s.ColorName,
        }).ToList();

        var responded = new bool[trials.Count];
        var missFlag = new bool[trials.Count];
        var stimTimes = stimuli.Select(s => s.Timestamp).ToArray();
        double window = settings.ResponseWindowMs / 1000.0 + SlackSeconds;

        foreach (var r in responses)
        {
            int k = Array.BinarySearch(stimTimes, r.Timestamp);
            if (k < 0) k = ~k - 1;           // r'den önceki son uyaran
            if (k < 0 || r.Timestamp - stimTimes[k] > window) continue;

            if (r.Category == MarkerCategory.ResponseMiss) { missFlag[k] = true; continue; }
            if (responded[k]) continue;       // yalnızca ilk tepki sayılır
            responded[k] = true;
            trials[k].ReactionTimeMs = (r.Timestamp - stimTimes[k]) * 1000.0;
        }

        for (int k = 0; k < trials.Count; k++)
        {
            var t = trials[k];
            if (t.IsTarget) t.Outcome = responded[k] && !missFlag[k] ? TrialOutcome.Hit : TrialOutcome.Miss;
            else t.Outcome = responded[k] ? TrialOutcome.FalseAlarm : TrialOutcome.CorrectRejection;
        }

        stats.Trials = trials;
        stats.TotalTrials = trials.Count;
        stats.TargetCount = trials.Count(t => t.IsTarget);
        stats.DistractorCount = trials.Count - stats.TargetCount;
        stats.Hits = trials.Count(t => t.Outcome == TrialOutcome.Hit);
        stats.Misses = trials.Count(t => t.Outcome == TrialOutcome.Miss);
        stats.FalseAlarms = trials.Count(t => t.Outcome == TrialOutcome.FalseAlarm);
        stats.CorrectRejections = trials.Count(t => t.Outcome == TrialOutcome.CorrectRejection);

        var rts = trials.Where(t => t.Outcome == TrialOutcome.Hit && t.ReactionTimeMs.HasValue)
                        .Select(t => t.ReactionTimeMs.Value).ToList();
        stats.MeanRtMs = Statistics.Mean(rts);
        stats.SdRtMs = Statistics.StdDev(rts);
        stats.MedianRtMs = Statistics.Median(rts);

        if (stats.HasResponseMarkers && stats.TargetCount > 0 && stats.DistractorCount > 0)
        {
            // Log-doğrusal düzeltme: oranlar 0 veya 1 olduğunda sonsuz z değerini önler
            double h = (stats.Hits + 0.5) / (stats.TargetCount + 1.0);
            double f = (stats.FalseAlarms + 0.5) / (stats.DistractorCount + 1.0);
            double zh = Statistics.InverseNormalCdf(h), zf = Statistics.InverseNormalCdf(f);
            stats.DPrime = zh - zf;
            stats.Criterion = -(zh + zf) / 2.0;
        }
        return stats;
    }
}
