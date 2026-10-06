using System.Diagnostics;
using LSL;
using XOnERPStudio.Models;
using XOnERPStudio.Simulation;

namespace XOnERPStudio.Lsl;

/// <summary>Ağda bulunan bir LSL akışının özeti.</summary>
public class LslStreamSummary
{
    public StreamInfo Info { get; init; }
    public string Name { get; init; }
    public string Type { get; init; }
    public int ChannelCount { get; init; }
    public double NominalSrate { get; init; }
    public string Format { get; init; }
    public string SourceId { get; init; }
    public string Host { get; init; }
    public bool IsString => Format == "cf_string";
    public bool LooksLikeEeg => !IsString && NominalSrate > 0 && ChannelCount >= 7;
    public bool LooksLikeMarkers => IsString || Type.Equals("Markers", StringComparison.OrdinalIgnoreCase);
    public override string ToString() =>
        $"{Name}  [{Type}]  {ChannelCount} kn · {(NominalSrate > 0 ? NominalSrate.ToString("0.##") + " Hz" : "düzensiz")} · {Host}";
}

/// <summary>
/// Canlı çift akış dinleyici: XonStream (EEG) ve LSLMarkerSender (Markers) akışlarını eşzamanlı okur.
/// Her iki inlet de proc_clocksync ile alınır; böylece tüm zaman damgaları bu bilgisayarın
/// LSL yerel saatindedir ve EEG ile markerlar milisaniye hassasiyetinde eşleşir.
/// Aynı arayüz, LSL olmadan çalışan dahili simülasyon kaynağını da destekler.
/// </summary>
public sealed class LiveLslManager : IDisposable
{
    private const int DisplaySeconds = 60;

    private readonly object _lock = new();
    private Thread _thread;
    private volatile bool _running;

    private StreamInlet _eegInlet, _markerInlet;
    private EegRecordingSession _simSource;

    // Görüntüleme halka tamponu
    private double[] _ringTs;
    private double[][] _ringData;
    private int _ringPos, _ringCount;
    private readonly List<MarkerEvent> _recentMarkers = new();

    // Kayıt
    private bool _recording;
    private List<double> _recTs;
    private List<double>[] _recData;
    private List<MarkerEvent> _recMarkers;
    private DateTime _recStartedAt;

    public string[] ChannelNames { get; private set; } = Array.Empty<string>();
    public double SampleRate { get; private set; } = EegConstants.DefaultSampleRate;
    public bool IsRunning => _running;
    public bool IsRecording => _recording;
    public bool IsSimulation => _simSource != null;
    public string EegStreamName { get; private set; } = "";
    public string MarkerStreamName { get; private set; } = "";
    public long SamplesReceived { get; private set; }
    public long MarkersReceived { get; private set; }
    public double MeasuredRate { get; private set; }
    public double RecordedSeconds => _recording && _recTs.Count > 1 ? _recTs[^1] - _recTs[0] : 0;
    public double LatestTimestamp { get; private set; }

    public event Action<MarkerEvent> MarkerReceived;
    public event Action<string> StatusChanged;

    // ------------------------------------------------------------------ keşif

    public static List<LslStreamSummary> Discover(double waitSeconds = 1.5)
    {
        var infos = LSL.LSL.resolve_streams(waitSeconds);
        return infos.Select(i => new LslStreamSummary
        {
            Info = i,
            Name = i.name(),
            Type = i.type(),
            ChannelCount = i.channel_count(),
            NominalSrate = i.nominal_srate(),
            Format = i.channel_format().ToString(),
            SourceId = i.source_id(),
            Host = i.hostname(),
        }).OrderBy(s => s.Name).ToList();
    }

    // ------------------------------------------------------------------ bağlantı

    public void ConnectLsl(LslStreamSummary eeg, LslStreamSummary markers)
    {
        Stop();
        if (eeg == null) throw new ArgumentException("EEG akışı seçilmedi.");

        _eegInlet = new StreamInlet(eeg.Info, 360, 0, true,
            processing_options_t.proc_clocksync | processing_options_t.proc_dejitter | processing_options_t.proc_monotonize);
        var full = _eegInlet.info(5.0);
        SampleRate = full.nominal_srate() > 0 ? full.nominal_srate() : EegConstants.DefaultSampleRate;
        ChannelNames = ReadChannelLabels(full, full.channel_count());
        EegStreamName = eeg.Name;

        if (markers != null)
        {
            _markerInlet = new StreamInlet(markers.Info, 360, 0, true, processing_options_t.proc_clocksync);
            _markerInlet.open_stream(5.0);
            MarkerStreamName = markers.Name;
        }
        else MarkerStreamName = "";

        _eegInlet.open_stream(5.0);
        StartThread(LslLoop);
        StatusChanged?.Invoke($"Bağlandı: {EegStreamName}" + (MarkerStreamName.Length > 0 ? $" + {MarkerStreamName}" : " (marker akışı yok)"));
    }

    /// <summary>LSL olmadan, üretilmiş simülasyon verisini gerçek zamanlı olarak oynatır.</summary>
    public void ConnectSimulation(SimulationOptions options)
    {
        Stop();
        _simSource = XonSimulationGenerator.Generate(options);
        SampleRate = _simSource.SampleRate;
        ChannelNames = _simSource.ChannelNames.ToArray();
        EegStreamName = "Simülasyon (XonStream)";
        MarkerStreamName = "Simülasyon (MarkerStream)";
        StartThread(SimulationLoop);
        StatusChanged?.Invoke("Dahili simülasyon akışı başladı.");
    }

    private void StartThread(ThreadStart loop)
    {
        int cap = (int)(SampleRate * DisplaySeconds);
        lock (_lock)
        {
            _ringTs = new double[cap];
            _ringData = Enumerable.Range(0, ChannelNames.Length).Select(_ => new double[cap]).ToArray();
            _ringPos = _ringCount = 0;
            _recentMarkers.Clear();
        }
        SamplesReceived = MarkersReceived = 0;
        _running = true;
        _thread = new Thread(loop) { IsBackground = true, Name = "LiveLslManager" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        if (_thread != null && _thread.IsAlive) _thread.Join(2000);
        _thread = null;
        _eegInlet?.Dispose(); _eegInlet = null;
        _markerInlet?.Dispose(); _markerInlet = null;
        _simSource = null;
    }

    public void Dispose() => Stop();

    private static string[] ReadChannelLabels(StreamInfo info, int count)
    {
        var labels = new List<string>();
        try
        {
            var ch = info.desc().child("channels").child("channel");
            while (!ch.empty() && labels.Count < count)
            {
                labels.Add(ch.child_value("label"));
                ch = ch.next_sibling();
            }
        }
        catch { /* desc yoksa varsayılanlar */ }

        var defaults = EegConstants.ChannelNames.Append(EegConstants.AuxChannelName).ToArray();
        return Enumerable.Range(0, count)
            .Select(i => i < labels.Count && !string.IsNullOrWhiteSpace(labels[i]) ? labels[i]
                       : i < defaults.Length ? defaults[i] : $"Ch{i + 1}")
            .ToArray();
    }

    // ------------------------------------------------------------------ okuma döngüleri

    private void LslLoop()
    {
        int nch = ChannelNames.Length;
        var buf = new float[512, nch];
        var tsBuf = new double[512];
        var mk = new string[1];
        var rateWatch = Stopwatch.StartNew();
        long rateSamples = 0;

        while (_running)
        {
            try
            {
                int n;
                while ((n = _eegInlet.pull_chunk(buf, tsBuf, 0.0)) > 0)
                {
                    for (int i = 0; i < n; i++)
                    {
                        var vals = new double[nch];
                        for (int c = 0; c < nch; c++) vals[c] = buf[i, c];
                        AddSample(tsBuf[i], vals);
                    }
                    rateSamples += n;
                }

                if (_markerInlet != null)
                {
                    double t;
                    while ((t = _markerInlet.pull_sample(mk, 0.0)) != 0.0)
                        AddMarker(new MarkerEvent(t, mk[0]));
                }

                if (rateWatch.Elapsed.TotalSeconds >= 2)
                {
                    MeasuredRate = rateSamples / rateWatch.Elapsed.TotalSeconds;
                    rateSamples = 0;
                    rateWatch.Restart();
                }
            }
            catch (LostException)
            {
                StatusChanged?.Invoke("Akış bağlantısı koptu; yeniden bağlanılmaya çalışılıyor...");
                Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke("Okuma hatası: " + ex.Message);
                Thread.Sleep(500);
            }
            Thread.Sleep(10);
        }
    }

    private void SimulationLoop()
    {
        var src = _simSource;
        var sw = Stopwatch.StartNew();
        double clock0 = src.Timestamps[0];
        int sampleIdx = 0, markerIdx = 0;
        var markers = src.Markers.OrderBy(m => m.Timestamp).ToList();
        int nch = src.Data.Length;
        long lastCount = 0;
        var rateWatch = Stopwatch.StartNew();

        while (_running && sampleIdx < src.SampleCount && _simSource == src)
        {
            double now = clock0 + sw.Elapsed.TotalSeconds;
            while (sampleIdx < src.SampleCount && src.Timestamps[sampleIdx] <= now)
            {
                var vals = new double[nch];
                for (int c = 0; c < nch; c++) vals[c] = src.Data[c][sampleIdx];
                AddSample(src.Timestamps[sampleIdx], vals);
                sampleIdx++;
            }
            while (markerIdx < markers.Count && markers[markerIdx].Timestamp <= now)
            {
                AddMarker(new MarkerEvent(markers[markerIdx].Timestamp, markers[markerIdx].Value));
                markerIdx++;
            }
            if (rateWatch.Elapsed.TotalSeconds >= 2)
            {
                MeasuredRate = (SamplesReceived - lastCount) / rateWatch.Elapsed.TotalSeconds;
                lastCount = SamplesReceived;
                rateWatch.Restart();
            }
            Thread.Sleep(10);
        }
        if (_running) StatusChanged?.Invoke("Simülasyon verisinin sonuna ulaşıldı.");
    }

    private void AddSample(double ts, double[] vals)
    {
        lock (_lock)
        {
            int cap = _ringTs.Length;
            _ringTs[_ringPos] = ts;
            for (int c = 0; c < vals.Length && c < _ringData.Length; c++) _ringData[c][_ringPos] = vals[c];
            _ringPos = (_ringPos + 1) % cap;
            if (_ringCount < cap) _ringCount++;

            if (_recording)
            {
                _recTs.Add(ts);
                for (int c = 0; c < vals.Length && c < _recData.Length; c++) _recData[c].Add(vals[c]);
            }
        }
        SamplesReceived++;
        LatestTimestamp = ts;
    }

    private void AddMarker(MarkerEvent m)
    {
        lock (_lock)
        {
            _recentMarkers.Add(m);
            if (_recentMarkers.Count > 2000) _recentMarkers.RemoveRange(0, 500);
            if (_recording) _recMarkers.Add(m);
        }
        MarkersReceived++;
        MarkerReceived?.Invoke(m);
    }

    /// <summary>Son <paramref name="seconds"/> saniyenin kopyasını döner (görüntüleme için).</summary>
    public (double[] ts, double[][] data, List<MarkerEvent> markers) Snapshot(double seconds)
    {
        lock (_lock)
        {
            if (_ringTs == null || _ringCount == 0) return (Array.Empty<double>(), Array.Empty<double[]>(), new List<MarkerEvent>());
            int cap = _ringTs.Length;
            int n = Math.Min(_ringCount, (int)(seconds * SampleRate));
            int start = (_ringPos - n + cap) % cap;
            var ts = new double[n];
            var data = new double[_ringData.Length][];
            for (int c = 0; c < data.Length; c++) data[c] = new double[n];
            for (int i = 0; i < n; i++)
            {
                int k = (start + i) % cap;
                ts[i] = _ringTs[k];
                for (int c = 0; c < data.Length; c++) data[c][i] = _ringData[c][k];
            }
            double t0 = n > 0 ? ts[0] : 0;
            var mk = _recentMarkers.Where(m => m.Timestamp >= t0).ToList();
            return (ts, data, mk);
        }
    }

    // ------------------------------------------------------------------ kayıt

    public void StartRecording()
    {
        lock (_lock)
        {
            _recTs = new List<double>((int)(SampleRate * 600));
            _recData = Enumerable.Range(0, ChannelNames.Length).Select(_ => new List<double>((int)(SampleRate * 600))).ToArray();
            _recMarkers = new List<MarkerEvent>();
            _recStartedAt = DateTime.Now;
            _recording = true;
        }
        StatusChanged?.Invoke("Kayıt başladı.");
    }

    /// <summary>Kaydı durdurur ve analiz edilebilir bir oturum döner.</summary>
    public EegRecordingSession StopRecording()
    {
        EegRecordingSession s;
        lock (_lock)
        {
            if (!_recording) return null;
            _recording = false;
            s = new EegRecordingSession
            {
                RecordedAt = _recStartedAt,
                SampleRate = SampleRate,
                SourceDescription = $"Canlı kayıt {_recStartedAt:yyyy-MM-dd HH:mm} ({EegStreamName})",
                DeviceName = IsSimulation ? "X.on Simülatör" : "BrainProducts X.on",
                ChannelNames = ChannelNames.ToList(),
                Timestamps = _recTs.ToArray(),
                Data = _recData.Select(d => d.ToArray()).ToArray(),
                Markers = _recMarkers.Select(m => new MarkerEvent(m.Timestamp, m.Value)).ToList(),
            };
        }
        s.ImportNotes.Add($"EEG: {EegStreamName}, Marker: {(MarkerStreamName.Length > 0 ? MarkerStreamName : "yok")}, ölçülen hız {MeasuredRate:0.0} Hz");
        s.NormalizeChannelOrder();
        StatusChanged?.Invoke($"Kayıt durduruldu: {s.DurationSeconds:0.0} s, {s.Markers.Count} marker.");
        return s;
    }
}
