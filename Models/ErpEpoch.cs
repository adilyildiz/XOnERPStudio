namespace XOnERPStudio.Models;

/// <summary>Tek bir deneme (trial) penceresi: marker çevresinde kesilmiş, baseline düzeltilmiş veri.</summary>
public class ErpEpoch
{
    public MarkerEvent Trigger { get; set; }
    public ErpCondition Condition { get; set; }

    /// <summary>Tetikleyiciye en yakın örnek indeksi (sürekli veride).</summary>
    public int OnsetSampleIndex { get; set; }

    /// <summary>Data[kanal][örnek] (µV), kanal sırası EegConstants.ChannelNames ile aynıdır.</summary>
    public double[][] Data { get; set; }

    public bool Rejected { get; set; }
    public string RejectReason { get; set; } = "";

    /// <summary>Kanal başına en büyük mutlak değer (µV).</summary>
    public double[] MaxAbs { get; set; }

    /// <summary>Kanal başına tepe-tepe farkı (µV).</summary>
    public double[] PeakToPeak { get; set; }
}
