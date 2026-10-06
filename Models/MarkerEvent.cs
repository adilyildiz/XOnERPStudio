using System.Drawing;

namespace XOnERPStudio.Models;

public enum MarkerCategory
{
    Custom,
    TargetStimulus,
    DistractorStimulus,
    /// <summary>Aynı uyaran için color_onset ile birlikte gelen ikinci marker (ör. stimulus_start). Epoch üretmez.</summary>
    StimulusDuplicate,
    StimulusEnd,
    ResponseHit,
    ResponseFalseAlarm,
    ResponseMiss,
    RestState,
    TaskControl,
}

public enum ErpCondition
{
    None,
    Target,
    Standard,
}

/// <summary>Zaman damgalı tek bir marker olayı ve sınıflandırması.</summary>
public class MarkerEvent
{
    public MarkerEvent() { }

    public MarkerEvent(double timestamp, string value)
    {
        Timestamp = timestamp;
        Value = value ?? "";
    }

    /// <summary>LSL ortak saatine göre zaman (s).</summary>
    public double Timestamp { get; set; }

    public string Value { get; set; } = "";

    public MarkerCategory Category { get; set; } = MarkerCategory.Custom;

    /// <summary>Bu marker bir epoch tetikleyicisiyse ERP koşulu.</summary>
    public ErpCondition Condition { get; set; } = ErpCondition.None;

    /// <summary>color_onset_X / target_color_X markerlarındaki renk adı.</summary>
    public string ColorName { get; set; }

    public bool IsEpochTrigger => Condition != ErpCondition.None;

    public override string ToString() => $"{Timestamp:F3}  {Value}  [{Category}]";

    public static Color DisplayColor(MarkerCategory c) => c switch
    {
        MarkerCategory.TargetStimulus => Color.FromArgb(241, 196, 15),
        MarkerCategory.DistractorStimulus => Color.FromArgb(52, 152, 219),
        MarkerCategory.ResponseHit => Color.FromArgb(46, 204, 113),
        MarkerCategory.ResponseFalseAlarm => Color.FromArgb(231, 76, 60),
        MarkerCategory.ResponseMiss => Color.FromArgb(230, 126, 34),
        MarkerCategory.RestState => Color.FromArgb(155, 89, 182),
        MarkerCategory.TaskControl => Color.FromArgb(149, 165, 166),
        MarkerCategory.StimulusDuplicate or MarkerCategory.StimulusEnd => Color.FromArgb(110, 110, 125),
        _ => Color.FromArgb(26, 188, 156),
    };

    public static string DisplayName(MarkerCategory c) => c switch
    {
        MarkerCategory.TargetStimulus => "Hedef Uyaran",
        MarkerCategory.DistractorStimulus => "Çeldirici",
        MarkerCategory.StimulusDuplicate => "Hedef (eş marker)",
        MarkerCategory.StimulusEnd => "Uyaran Sonu",
        MarkerCategory.ResponseHit => "Doğru Tepki",
        MarkerCategory.ResponseFalseAlarm => "Hatalı Tepki",
        MarkerCategory.ResponseMiss => "Kaçırma",
        MarkerCategory.RestState => "Dinlenme",
        MarkerCategory.TaskControl => "Görev Kontrol",
        _ => "Özel",
    };
}
