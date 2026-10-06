using System.Drawing;

namespace XOnERPStudio.Models;

/// <summary>BrainProducts X.on cihazına ait sabitler: kanal adları, renkleri ve 10-20 konumları.</summary>
public static class EegConstants
{
    public const double DefaultSampleRate = 250.0;

    /// <summary>Analiz edilen 7 EEG kanalı (X.on varsayılan montajı).</summary>
    public static readonly string[] ChannelNames = { "F3", "F4", "C3", "Cz", "C4", "P3", "P4" };

    /// <summary>XonStream'in 8. kanalı (opsiyonel AUX / bipolar).</summary>
    public const string AuxChannelName = "AUX";

    /// <summary>X.On LSL köprüsünün varsayılan EEG stream adı.</summary>
    public const string DefaultEegStreamName = "XonStream";

    /// <summary>LSLMarkerSender'ın varsayılan marker stream adı.</summary>
    public const string DefaultMarkerStreamName = "MarkerStream";

    /// <summary>P3 ve P4 ortalamasından türetilen sanal parietal orta hat kanalı (Pz yerine).</summary>
    public const string VirtualParietalName = "Pz*";

    /// <summary>P300'ün en belirgin olduğu kanallar.</summary>
    public static readonly string[] P300Channels = { "P3", "P4", "Cz" };

    public static readonly Color[] ChannelColors =
    {
        Color.FromArgb(0x5D, 0xAD, 0xE2), // F3
        Color.FromArgb(0x48, 0xC9, 0xB0), // F4
        Color.FromArgb(0xAF, 0x7A, 0xC5), // C3
        Color.FromArgb(0xF5, 0xB0, 0x41), // Cz
        Color.FromArgb(0xEC, 0x70, 0x63), // C4
        Color.FromArgb(0x58, 0xD6, 0x8D), // P3
        Color.FromArgb(0xF0, 0x8E, 0xC1), // P4
    };

    /// <summary>
    /// Kafa üzerindeki normalize konumlar (x: sol -1 → sağ +1, y: ön -1 → arka +1).
    /// 10-20 sisteminin üstten görünüşüne göre yaklaşık yerleşim.
    /// </summary>
    public static readonly PointF[] TopoPositions =
    {
        new(-0.40f, -0.50f), // F3
        new( 0.40f, -0.50f), // F4
        new(-0.62f,  0.00f), // C3
        new( 0.00f,  0.00f), // Cz
        new( 0.62f,  0.00f), // C4
        new(-0.40f,  0.50f), // P3
        new( 0.40f,  0.50f), // P4
    };

    public static int IndexOf(string channel)
    {
        for (int i = 0; i < ChannelNames.Length; i++)
            if (string.Equals(ChannelNames[i], channel, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    public static Color ColorOf(string channel)
    {
        int i = IndexOf(channel);
        return i >= 0 ? ChannelColors[i] : Color.Silver;
    }
}
