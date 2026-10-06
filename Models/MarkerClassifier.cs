using System.Globalization;
using System.Text;

namespace XOnERPStudio.Models;

/// <summary>
/// LSLMarkerSender marker dizisini bağlamıyla birlikte sınıflandırır.
///
/// Dikkat testi her denemede <c>color_onset_&lt;Renk&gt;</c> gönderir; hedef denemelerde aynı anda
/// <c>stimulus_start</c> da gelir. Bu nedenle epoch tetikleyicisi color_onset'tir ve eşlik eden
/// stimulus_start "eş marker" olarak işaretlenir (çift sayımı önler). color_onset yoksa
/// (genel marker gönderimi) stimulus_start tek başına hedef tetikleyicisi olur.
/// </summary>
public static class MarkerClassifier
{
    public const string PrefixColorOnset = "color_onset_";
    public const string PrefixTargetColor = "target_color_";
    public const string StimulusStart = "stimulus_start";
    public const string StimulusEnd = "stimulus_end";
    public const string ResponseCorrect = "response_correct";
    public const string ResponseIncorrect = "response_incorrect";
    public const string ResponseMiss = "response_miss";

    private static readonly HashSet<string> RestMarkers = new(StringComparer.OrdinalIgnoreCase) { "rest_start", "rest_end" };
    private static readonly HashSet<string> ControlMarkers = new(StringComparer.OrdinalIgnoreCase)
        { "task_start", "task_end", "task_aborted", "trial_start" };

    /// <summary>Markerları zamana göre sıralar ve Category/Condition alanlarını doldurur. Hedef rengini döner.</summary>
    public static string Classify(List<MarkerEvent> markers, ErpFilterSettings settings)
    {
        markers.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        var customT = new HashSet<string>(settings.CustomTargetList, StringComparer.OrdinalIgnoreCase);
        var customS = new HashSet<string>(settings.CustomStandardList, StringComparer.OrdinalIgnoreCase);
        string overrideColor = string.IsNullOrWhiteSpace(settings.TargetColorOverride) ? null : settings.TargetColorOverride.Trim();
        double tol = settings.DuplicateToleranceMs / 1000.0;

        // 1. geçiş: bağlamsız temel kategoriler
        foreach (var m in markers)
        {
            string v = (m.Value ?? "").Trim();
            m.Condition = ErpCondition.None;
            m.ColorName = null;

            if (customT.Contains(v)) { m.Category = MarkerCategory.TargetStimulus; m.Condition = ErpCondition.Target; }
            else if (customS.Contains(v)) { m.Category = MarkerCategory.DistractorStimulus; m.Condition = ErpCondition.Standard; }
            else if (v.StartsWith(PrefixTargetColor, StringComparison.OrdinalIgnoreCase))
            { m.Category = MarkerCategory.TaskControl; m.ColorName = v[PrefixTargetColor.Length..]; }
            else if (v.StartsWith(PrefixColorOnset, StringComparison.OrdinalIgnoreCase))
            { m.Category = MarkerCategory.DistractorStimulus; m.ColorName = v[PrefixColorOnset.Length..]; }
            else if (Eq(v, StimulusStart)) m.Category = MarkerCategory.TargetStimulus;
            else if (Eq(v, StimulusEnd)) m.Category = MarkerCategory.StimulusEnd;
            else if (Eq(v, ResponseCorrect)) m.Category = MarkerCategory.ResponseHit;
            else if (Eq(v, ResponseIncorrect)) m.Category = MarkerCategory.ResponseFalseAlarm;
            else if (Eq(v, ResponseMiss)) m.Category = MarkerCategory.ResponseMiss;
            else if (RestMarkers.Contains(v)) m.Category = MarkerCategory.RestState;
            else if (ControlMarkers.Contains(v)) m.Category = MarkerCategory.TaskControl;
            else m.Category = MarkerCategory.Custom;
        }

        // 2. geçiş: hedef rengi ve stimulus_start eşleşmesi ile koşulları çöz
        string currentTarget = overrideColor;
        string lastTarget = overrideColor;
        for (int i = 0; i < markers.Count; i++)
        {
            var m = markers[i];
            string v = m.Value.Trim();
            if (customT.Contains(v) || customS.Contains(v)) continue;

            if (m.Category == MarkerCategory.TaskControl && m.ColorName != null)
            {
                if (overrideColor == null) currentTarget = lastTarget = m.ColorName;
            }
            else if (v.StartsWith(PrefixColorOnset, StringComparison.OrdinalIgnoreCase))
            {
                bool paired = HasNeighbor(markers, i, tol, x => Eq(x.Value.Trim(), StimulusStart));
                bool colorMatch = currentTarget != null && SameColor(m.ColorName, currentTarget);
                bool isTarget = paired || colorMatch;
                m.Category = isTarget ? MarkerCategory.TargetStimulus : MarkerCategory.DistractorStimulus;
                m.Condition = isTarget ? ErpCondition.Target : ErpCondition.Standard;
            }
            else if (Eq(v, StimulusStart))
            {
                bool paired = HasNeighbor(markers, i, tol,
                    x => x.Value.Trim().StartsWith(PrefixColorOnset, StringComparison.OrdinalIgnoreCase));
                m.Category = paired ? MarkerCategory.StimulusDuplicate : MarkerCategory.TargetStimulus;
                m.Condition = paired ? ErpCondition.None : ErpCondition.Target;
            }
        }
        return lastTarget;
    }

    private static bool HasNeighbor(List<MarkerEvent> markers, int i, double tol, Func<MarkerEvent, bool> pred)
    {
        double t = markers[i].Timestamp;
        for (int j = i - 1; j >= 0 && t - markers[j].Timestamp <= tol; j--)
            if (pred(markers[j])) return true;
        for (int j = i + 1; j < markers.Count && markers[j].Timestamp - t <= tol; j++)
            if (pred(markers[j])) return true;
        return false;
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Renk adlarını büyük/küçük harf ve Türkçe karakter farklarını yok sayarak karşılaştırır.</summary>
    public static bool SameColor(string a, string b) => NormalizeKey(a) == NormalizeKey(b);

    public static string NormalizeKey(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim().Replace('ı', 'i').Replace('İ', 'i');
        var sb = new StringBuilder();
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }
}
