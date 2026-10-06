using System.Diagnostics;
using LSL;
using XOnERPStudio.Models;

namespace XOnERPStudio.Simulation;

/// <summary>
/// Simülasyon verisini gerçek LSL akışları olarak ağa yayınlar: "XonStream" (EEG, 8 kanal, 250 Hz)
/// ve "MarkerStream" (Markers). Cihaz ve LSLMarkerSender olmadan LSL alım hattını, LabRecorder'ı
/// veya başka bir bilgisayarı uçtan uca test etmek için kullanılır.
/// </summary>
public sealed class SimulationLslPublisher : IDisposable
{
    private Thread _thread;
    private volatile bool _running;
    private StreamOutlet _eegOutlet, _markerOutlet;

    public bool IsRunning => _running;
    public event Action<string> StatusChanged;

    public void Start(SimulationOptions options)
    {
        Stop();
        var session = XonSimulationGenerator.Generate(options);

        var eegInfo = new StreamInfo(EegConstants.DefaultEegStreamName + "_Sim", "EEG", session.Data.Length,
            session.SampleRate, channel_format_t.cf_float32, "XOnERPStudioSimEEG");
        var chans = eegInfo.desc().append_child("channels");
        foreach (var name in session.ChannelNames)
        {
            var ch = chans.append_child("channel");
            ch.append_child_value("label", name);
            ch.append_child_value("unit", "microvolts");
            ch.append_child_value("type", "EEG");
        }
        _eegOutlet = new StreamOutlet(eegInfo);

        var mkInfo = new StreamInfo(EegConstants.DefaultMarkerStreamName + "_Sim", "Markers", 1,
            LSL.LSL.IRREGULAR_RATE, channel_format_t.cf_string, "XOnERPStudioSimMarkers");
        _markerOutlet = new StreamOutlet(mkInfo);

        _running = true;
        _thread = new Thread(() => Loop(session)) { IsBackground = true, Name = "SimLslPublisher" };
        _thread.Start();
        StatusChanged?.Invoke($"LSL'e yayınlanıyor: {eegInfo.name()} + {mkInfo.name()}");
    }

    private void Loop(EegRecordingSession s)
    {
        var sw = Stopwatch.StartNew();
        double simStart = s.Timestamps[0];
        double lslStart = LSL.LSL.local_clock();
        int i = 0, mi = 0, nch = s.Data.Length;
        var markers = s.Markers.OrderBy(m => m.Timestamp).ToList();
        var sample = new float[nch];

        while (_running && i < s.SampleCount)
        {
            double simNow = simStart + sw.Elapsed.TotalSeconds;
            while (i < s.SampleCount && s.Timestamps[i] <= simNow)
            {
                for (int c = 0; c < nch; c++) sample[c] = (float)s.Data[c][i];
                i++;
                // Bu döngüdeki son örnekte tamponu ağa gönder (pushthrough)
                bool last = i >= s.SampleCount || s.Timestamps[i] > simNow;
                _eegOutlet.push_sample(sample, lslStart + (s.Timestamps[i - 1] - simStart), last);
            }
            while (mi < markers.Count && markers[mi].Timestamp <= simNow)
            {
                _markerOutlet.push_sample(new[] { markers[mi].Value }, lslStart + (markers[mi].Timestamp - simStart));
                mi++;
            }
            Thread.Sleep(5);
        }
        if (_running) StatusChanged?.Invoke("Simülasyon yayını tamamlandı.");
        _running = false;
    }

    public void Stop()
    {
        _running = false;
        if (_thread != null && _thread.IsAlive) _thread.Join(2000);
        _thread = null;
        _eegOutlet?.Dispose(); _eegOutlet = null;
        _markerOutlet?.Dispose(); _markerOutlet = null;
    }

    public void Dispose() => Stop();
}
