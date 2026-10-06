using System.Globalization;
using System.Text;
using XOnERPStudio.IO;
using XOnERPStudio.Models;
using XOnERPStudio.Reporting;
using XOnERPStudio.SignalProcessing;
using XOnERPStudio.Simulation;

namespace XOnERPStudio;

/// <summary>
/// Arayüz olmadan uçtan uca doğrulama: simülasyon → analiz → rapor/CSV → CSV geri okuma → yeniden analiz.
/// Sonuçlar ve geçti/kaldı kontrolleri selftest.txt dosyasına yazılır. Çıkış kodu 0 = başarılı.
/// </summary>
internal static class SelfTest
{
    /// <summary>
    /// LSL geri döngü testi: simülasyonu gerçek LSL akışları olarak yayınlar, LiveLslManager ile bulup
    /// kaydeder ve kaydı analiz eder (XOnERPStudio.exe --lsltest klasör).
    /// </summary>
    public static int RunLsl(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var log = new StringBuilder();
        int failures = 0;
        void Check(bool ok, string what) { log.AppendLine((ok ? "[OK]   " : "[FAIL] ") + what); if (!ok) failures++; }
        try
        {
            Check(Lsl.NativeLibraryLoader.IsAvailable(out var ver), $"liblsl yüklendi ({ver})");
            using var pub = new SimulationLslPublisher();
            var simOpts = new SimulationOptions { Trials = 60, RestSeconds = 2, Seed = 7 };
            pub.Start(simOpts);
            Thread.Sleep(1500);
            var streams = Lsl.LiveLslManager.Discover(3.0);
            foreach (var s in streams) log.AppendLine("  Akış: " + s);
            var eeg = streams.FirstOrDefault(s => s.Name == EegConstants.DefaultEegStreamName + "_Sim");
            var mk = streams.FirstOrDefault(s => s.Name == EegConstants.DefaultMarkerStreamName + "_Sim");
            Check(eeg != null && mk != null, "Simülasyon EEG ve marker akışları ağda bulundu");
            if (eeg == null || mk == null) throw new InvalidOperationException("Akış bulunamadı");

            using var mgr = new Lsl.LiveLslManager();
            mgr.ConnectLsl(eeg, mk);
            Check(mgr.ChannelNames.Take(7).SequenceEqual(EegConstants.ChannelNames), "Kanal etiketleri stream desc'ten okundu");
            mgr.StartRecording();
            Thread.Sleep(30000);
            var session = mgr.StopRecording();
            log.AppendLine($"  Kayıt: {session.DurationSeconds:0.0} s, {session.SampleCount} örnek, {session.Markers.Count} marker, ölçülen {mgr.MeasuredRate:0.0} Hz");
            Check(session.SampleCount > 250 * 20 && Math.Abs(session.EffectiveSampleRate() - 250) < 3, $"Etkin örnekleme hızı ≈250 Hz ({session.EffectiveSampleRate():0.0})");
            Check(session.Markers.Count > 20, "Markerlar alındı");
            var r = ErpEngine.Analyze(session, new ErpFilterSettings());
            log.AppendLine($"  Epoch hedef {r.TargetEpochsTotal}, standart {r.StandardEpochsTotal}; Pz* {r.Get(EegConstants.VirtualParietalName)?.Target?.P300}");
            Check(r.TargetEpochsTotal > 0 && r.StandardEpochsTotal > 0, "Canlı kayıttan epochlar oluşturuldu");
            // Marker ↔ EEG hizası: aynı tohumla üretilen orijinal oturumla karşılaştır
            var orig = XonSimulationGenerator.Generate(simOpts);
            int k0 = Enumerable.Range(0, orig.SampleCount).First(k =>
                Enumerable.Range(0, 8).All(c => Math.Abs((float)orig.Data[c][k] - session.Data[c][0]) < 1e-3));
            double eegOffset = session.Timestamps[0] - orig.Timestamps[k0];
            var m0 = session.Markers[0];
            var om = orig.Markers.Where(m => m.Value == m0.Value)
                .First(m => m.Timestamp - orig.Timestamps[0] >= m0.Timestamp - eegOffset - orig.Timestamps[0] - 0.5);
            double markerOffset = m0.Timestamp - om.Timestamp;
            double errMs = (markerOffset - eegOffset) * 1000;
            log.AppendLine($"  Hiza: EEG ofset {eegOffset:F4} s, marker ofset {markerOffset:F4} s, fark {errMs:0.00} ms");
            Check(Math.Abs(errMs) < 2, $"Marker–EEG zaman hizası < 2 ms ({errMs:0.00} ms)");
            pub.Stop();
        }
        catch (Exception ex) { log.AppendLine("[FAIL] İstisna: " + ex); failures++; }
        log.AppendLine(failures == 0 ? "SONUÇ: TÜM TESTLER GEÇTİ" : $"SONUÇ: {failures} TEST BAŞARISIZ");
        File.WriteAllText(Path.Combine(outDir, "lsltest.txt"), log.ToString(), new UTF8Encoding(true));
        return failures == 0 ? 0 : 1;
    }

    public static int Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var log = new StringBuilder();
        int failures = 0;
        void Check(bool ok, string what)
        {
            log.AppendLine((ok ? "[OK]   " : "[FAIL] ") + what);
            if (!ok) failures++;
        }

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var opts = new SimulationOptions { Trials = 120, Seed = 42, P300AmplitudeUv = 10, P300LatencyMs = 350 };
            var session = XonSimulationGenerator.Generate(opts);
            var settings = new ErpFilterSettings();
            var r = ErpEngine.Analyze(session, settings);

            log.AppendLine($"Süre {session.DurationSeconds:0.0} s, {session.SampleCount} örnek, {session.Markers.Count} marker");
            log.AppendLine($"Epoch hedef {r.TargetEpochsAccepted}/{r.TargetEpochsTotal}, standart {r.StandardEpochsAccepted}/{r.StandardEpochsTotal}");
            var b = r.Behavior;
            log.AppendLine($"Davranış: hit {b.Hits}/{b.TargetCount}, FA {b.FalseAlarms}/{b.DistractorCount}, RT {b.MeanRtMs:0}±{b.SdRtMs:0} ms, d' {b.DPrime:0.00}");
            foreach (var c in r.Channels.Values)
                log.AppendLine($"  {c.ChannelName,-4} T: {c.Target.P300} | {c.Target.N200} | S: {c.Standard.P300} | Fark: {c.Difference?.P300} | SNR {c.Target.Snr:0.0}");
            foreach (var w in r.Warnings) log.AppendLine("  Uyarı: " + w);

            int expectedTargets = (int)Math.Round(opts.Trials * opts.TargetRatio);
            Check(r.TargetEpochsTotal == expectedTargets, $"Hedef epoch sayısı = {expectedTargets} (çift sayım yok)");
            Check(r.StandardEpochsTotal == opts.Trials - expectedTargets, "Standart epoch sayısı doğru");
            Check(r.Epochs.Any(e => e.Rejected), "Göz kırpmaları artefakt olarak reddedildi");
            var pz = r.Get(EegConstants.VirtualParietalName);
            Check(pz.Target.P300.Found && Math.Abs(pz.Target.P300.LatencyMs - 350) <= 40, $"Pz* P300 gecikmesi ≈350 ms ({pz.Target.P300.LatencyMs:0})");
            Check(pz.Target.P300.AmplitudeUv is > 6 and < 14, $"Pz* P300 genliği ≈10 µV ({pz.Target.P300.AmplitudeUv:0.0})");
            Check(pz.Difference.P300.AmplitudeUv > 5, "Fark dalgasında belirgin P300");
            Check(pz.Target.N200.Found && pz.Target.N200.AmplitudeUv < 0, $"Hedef N200 negatif ({pz.Target.N200.AmplitudeUv:0.0} µV)");
            Check(r.Get("P3").Target.P300.AmplitudeUv > r.Get("F3").Target.P300.AmplitudeUv, "Parietal > frontal topografi");
            Check(b.HitRate > 0.75 && b.DPrime > 2, "Davranış metrikleri makul");
            Check(r.RestSpectrum != null, "Dinlenme spektrumu hesaplandı");
            int alpha = Array.IndexOf(FrequencyAnalysis.Bands.Select(x => x.Name).ToArray(), "Alpha");
            Check(r.RestSpectrum.BandRelative[5][alpha] > r.RestSpectrum.BandRelative[0][alpha], "Alfa parietalde frontalden güçlü");

            // Filtre faz testi: 10 Hz sinüsün tepesi kaymamalı
            var sine = Enumerable.Range(0, 2500).Select(i => Math.Sin(2 * Math.PI * 10 * i / 250.0)).ToArray();
            var filtered = DigitalFilter.FiltFilt(sine, DigitalFilter.DesignChain(settings, 250), 750);
            double maxErr = Enumerable.Range(500, 1500).Max(i => Math.Abs(filtered[i] - sine[i]));
            Check(maxErr < 0.05, $"FiltFilt sıfır faz / geçiş bandı kazancı (maks hata {maxErr:0.0000})");

            string report = ErpReportGenerator.Save(r, Path.Combine(outDir, "rapor.html"));
            var csvs = CsvReportExporter.ExportAll(r, outDir);
            Check(File.Exists(report) && csvs.All(File.Exists), "HTML rapor ve CSV dosyaları yazıldı");

            // Birleşik CSV gidiş-dönüş
            string combined = Path.Combine(outDir, "oturum.csv");
            SessionExporter.ExportCombinedCsv(session, combined);
            SessionExporter.ExportJson(session, Path.Combine(outDir, "oturum.json"), r);
            var back = CsvDataReader.LoadEeg(combined);
            var r2 = ErpEngine.Analyze(back, settings);
            Check(back.SampleCount == session.SampleCount && Math.Abs(back.SampleRate - 250) < 1, "CSV geri okuma: örnek sayısı ve hız");
            Check(r2.TargetEpochsTotal == r.TargetEpochsTotal, "CSV geri okuma: aynı epoch sayısı");
            Check(Math.Abs(r2.Get("P3").Target.P300.AmplitudeUv - r.Get("P3").Target.P300.AmplitudeUv) < 0.5, "CSV geri okuma: aynı P300");

            // Ayrı marker CSV (göreli zaman) hizalaması
            string mk = Path.Combine(outDir, "markers_relative.csv");
            File.WriteAllLines(mk, new[] { "time,marker" }.Concat(session.Markers.Select(m =>
                (m.Timestamp - session.StartTime).ToString("F4", CultureInfo.InvariantCulture) + "," + m.Value)), new UTF8Encoding(true));
            var markers = CsvDataReader.LoadMarkers(mk, back);
            Check(Math.Abs(markers[0].Timestamp - session.Markers[0].Timestamp) < 0.005, "Göreli marker CSV EEG saatine hizalandı");

            log.AppendLine("Rapor: " + report);
        }
        catch (Exception ex)
        {
            log.AppendLine("[FAIL] İstisna: " + ex);
            failures++;
        }

        log.AppendLine(failures == 0 ? "SONUÇ: TÜM TESTLER GEÇTİ" : $"SONUÇ: {failures} TEST BAŞARISIZ");
        File.WriteAllText(Path.Combine(outDir, "selftest.txt"), log.ToString(), new UTF8Encoding(true));
        return failures == 0 ? 0 : 1;
    }
}
