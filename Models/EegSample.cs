namespace XOnERPStudio.Models;

/// <summary>Tek bir EEG örneği: LSL zaman damgası (s) ve kanal voltajları (µV).</summary>
public readonly struct EegSample
{
    public EegSample(double timestamp, float[] values)
    {
        Timestamp = timestamp;
        Values = values;
    }

    public double Timestamp { get; }
    public float[] Values { get; }
}
