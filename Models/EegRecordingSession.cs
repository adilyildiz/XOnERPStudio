namespace XOnERPStudio.Models;

/// <summary>
/// Bir kayıt oturumunun tamamı: sürekli EEG (kanal × örnek), zaman damgaları, markerlar ve üst veri.
/// Tüm zamanlar aynı saat tabanındadır (LSL local_clock veya dosya saati).
/// </summary>
public class EegRecordingSession
{
    public string ParticipantId { get; set; } = "";
    public string ParticipantName { get; set; } = "";
    public string ParticipantAge { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime RecordedAt { get; set; } = DateTime.Now;
    public string DeviceName { get; set; } = "BrainProducts X.on";
    public string SourceDescription { get; set; } = "";

    public double SampleRate { get; set; } = EegConstants.DefaultSampleRate;

    /// <summary>Kanal adları; Data dizisiyle aynı sırada.</summary>
    public List<string> ChannelNames { get; set; } = new();

    /// <summary>Örnek zaman damgaları (s), monoton artan.</summary>
    public double[] Timestamps { get; set; } = Array.Empty<double>();

    /// <summary>Data[kanal][örnek] — µV.</summary>
    public double[][] Data { get; set; } = Array.Empty<double[]>();

    public List<MarkerEvent> Markers { get; set; } = new();

    /// <summary>İçe aktarma sırasında oluşan uyarılar / bilgiler.</summary>
    public List<string> ImportNotes { get; set; } = new();

    public int SampleCount => Timestamps.Length;
    public double StartTime => Timestamps.Length > 0 ? Timestamps[0] : 0;
    public double EndTime => Timestamps.Length > 0 ? Timestamps[^1] : 0;
    public double DurationSeconds => Math.Max(0, EndTime - StartTime);

    public int ChannelIndex(string name)
    {
        for (int i = 0; i < ChannelNames.Count; i++)
            if (string.Equals(ChannelNames[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>Verilen zamana en yakın örnek indeksini ikili arama ile bulur.</summary>
    public int IndexOfTime(double t)
    {
        var ts = Timestamps;
        if (ts.Length == 0) return -1;
        if (t <= ts[0]) return 0;
        if (t >= ts[^1]) return ts.Length - 1;
        int idx = Array.BinarySearch(ts, t);
        if (idx >= 0) return idx;
        idx = ~idx; // ts[idx-1] < t < ts[idx]
        return (t - ts[idx - 1]) <= (ts[idx] - t) ? idx - 1 : idx;
    }

    /// <summary>Zaman damgalarından ölçülen gerçek örnekleme hızı.</summary>
    public double EffectiveSampleRate()
    {
        if (Timestamps.Length < 2) return SampleRate;
        double span = Timestamps[^1] - Timestamps[0];
        return span > 0 ? (Timestamps.Length - 1) / span : SampleRate;
    }

    /// <summary>
    /// Kanalları standart 7 kanal sırasına (F3..P4) göre yeniden düzenler; bulunamayan kanallar
    /// için ilk kalan kanallar kullanılır. AUX gibi ek kanallar sona eklenir.
    /// </summary>
    public void NormalizeChannelOrder()
    {
        var used = new HashSet<int>();
        var newNames = new List<string>();
        var newData = new List<double[]>();
        var missing = new List<string>();

        foreach (var std in EegConstants.ChannelNames)
        {
            int i = ChannelIndex(std);
            if (i >= 0 && used.Add(i))
            {
                newNames.Add(std);
                newData.Add(Data[i]);
            }
            else missing.Add(std);
        }

        if (missing.Count > 0)
        {
            // Etiketler eşleşmiyorsa kalan kanalları sırayla ata (X.on sırası: F3,F4,C3,Cz,C4,P3,P4,AUX)
            if (newNames.Count == 0)
            {
                newNames.Clear(); newData.Clear(); used.Clear();
                for (int k = 0; k < EegConstants.ChannelNames.Length && k < Data.Length; k++)
                {
                    newNames.Add(EegConstants.ChannelNames[k]);
                    newData.Add(Data[k]);
                    used.Add(k);
                }
                ImportNotes.Add("Kanal etiketleri tanınmadı; ilk kanallar X.on sırasına (F3,F4,C3,Cz,C4,P3,P4) göre atandı.");
            }
            else
            {
                ImportNotes.Add("Eksik kanallar: " + string.Join(", ", missing));
            }
        }

        for (int i = 0; i < Data.Length; i++)
        {
            if (used.Contains(i)) continue;
            newNames.Add(i < ChannelNames.Count ? ChannelNames[i] : $"Ch{i + 1}");
            newData.Add(Data[i]);
        }

        ChannelNames = newNames;
        Data = newData.ToArray();
    }

    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(ParticipantId) ? SourceDescription : $"{ParticipantId} — {SourceDescription}";
}
