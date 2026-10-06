using XOnERPStudio.IO;
using XOnERPStudio.Models;

namespace XOnERPStudio.SignalProcessing;

/// <summary>
/// ERP analiz hattı: filtreleme → marker sınıflandırma → epochlama → baseline → artefakt reddi →
/// koşul ortalaması + SEM → fark dalgası → N100/N200/P300 tepe tespiti → spektrum ve kalite.
/// </summary>
public static class ErpEngine
{
    public static ErpAnalysisResult Analyze(EegRecordingSession session, ErpFilterSettings settings, IProgress<string> progress = null)
    {
        if (session == null || session.SampleCount == 0) throw new InvalidOperationException("Analiz için EEG verisi yok.");
        double fs = session.SampleRate;
        string err = settings.Validate(fs);
        if (err != null) throw new InvalidOperationException(err);

        var result = new ErpAnalysisResult { Session = session, Settings = settings.Clone() };

        double eff = session.EffectiveSampleRate();
        if (Math.Abs(eff - fs) / fs > 0.02)
            result.Warnings.Add($"Ölçülen örnekleme hızı ({eff:0.0} Hz) nominal değerden ({fs:0} Hz) farklı; kayıtta boşluk olabilir.");

        progress?.Report("Filtreleniyor (FiltFilt)...");
        result.FilteredData = DigitalFilter.FilterChannels(session.Data, settings, fs);

        progress?.Report("Markerlar sınıflandırılıyor...");
        string targetColor = MarkerClassifier.Classify(session.Markers, settings);
        result.Behavior = AttentionTaskLogReader.Build(session.Markers, settings, targetColor);

        progress?.Report("Epochlar kesiliyor...");
        int eegCount = Math.Min(EegConstants.ChannelNames.Length, session.Data.Length);
        BuildEpochs(result, eegCount);

        // Davranış denemelerine epoch red bilgisini işle
        var rejectedTriggers = new HashSet<MarkerEvent>(result.Epochs.Where(e => e.Rejected).Select(e => e.Trigger));
        foreach (var t in result.Behavior.Trials) t.EpochRejected = rejectedTriggers.Contains(t.Stimulus);

        progress?.Report("Ortalama ERP dalgaları ve tepeler hesaplanıyor...");
        for (int ch = 0; ch < eegCount; ch++)
        {
            int c = ch;
            result.Channels[EegConstants.ChannelNames[ch]] = BuildChannel(result, EegConstants.ChannelNames[ch], e => e.Data[c]);
        }
        int p3 = EegConstants.IndexOf("P3"), p4 = EegConstants.IndexOf("P4");
        if (p3 < eegCount && p4 < eegCount)
        {
            result.Channels[EegConstants.VirtualParietalName] = BuildChannel(result, EegConstants.VirtualParietalName,
                e => e.Data[p3].Zip(e.Data[p4], (a, b) => (a + b) / 2).ToArray());
        }

        progress?.Report("Spektrum ve sinyal kalitesi hesaplanıyor...");
        ComputeSpectra(result, eegCount);
        ComputeQuality(result, eegCount);

        if (result.Epochs.Count == 0)
            result.Warnings.Add("Hiç epoch oluşturulamadı: uyaran markerı bulunamadı veya markerlar EEG kaydının dışında.");
        else
        {
            if (result.TargetEpochsAccepted < 10)
                result.Warnings.Add($"Kabul edilen hedef epoch sayısı düşük ({result.TargetEpochsAccepted}); P300 kestirimi güvenilir olmayabilir (önerilen ≥ 20).");
            if (result.StandardEpochsAccepted == 0)
                result.Warnings.Add("Standart (çeldirici) epoch yok; fark dalgası hesaplanamadı.");
        }
        return result;
    }

    private static int MsToSamples(double ms, double fs) => (int)Math.Round(ms * fs / 1000.0);

    private static void BuildEpochs(ErpAnalysisResult result, int eegCount)
    {
        var s = result.Settings;
        var session = result.Session;
        double fs = session.SampleRate;
        int pre = MsToSamples(-s.EpochStartMs, fs);
        int len = pre + MsToSamples(s.EpochEndMs, fs);
        int bs = MsToSamples(s.BaselineStartMs - s.EpochStartMs, fs);
        int be = MsToSamples(s.BaselineEndMs - s.EpochStartMs, fs);
        double maxGap = 2.0 / fs;
        int outside = 0;

        foreach (var m in session.Markers.Where(m => m.IsEpochTrigger))
        {
            int idx = session.IndexOfTime(m.Timestamp);
            if (idx < 0 || Math.Abs(session.Timestamps[idx] - m.Timestamp) > maxGap) { outside++; continue; }
            int start = idx - pre;
            if (start < 0 || start + len > session.SampleCount) { outside++; continue; }

            var ep = new ErpEpoch
            {
                Trigger = m,
                Condition = m.Condition,
                OnsetSampleIndex = idx,
                Data = new double[eegCount][],
                MaxAbs = new double[eegCount],
                PeakToPeak = new double[eegCount],
            };
            var reasons = new List<string>();
            for (int ch = 0; ch < eegCount; ch++)
            {
                var x = new double[len];
                Array.Copy(result.FilteredData[ch], start, x, 0, len);
                DigitalFilter.BaselineCorrect(x, bs, be);
                ep.Data[ch] = x;

                double mx = double.MinValue, mn = double.MaxValue, abs = 0;
                foreach (var v in x)
                {
                    if (v > mx) mx = v;
                    if (v < mn) mn = v;
                    if (Math.Abs(v) > abs) abs = Math.Abs(v);
                }
                ep.MaxAbs[ch] = abs;
                ep.PeakToPeak[ch] = mx - mn;

                if (s.ArtifactRejectionEnabled)
                {
                    if (abs > s.ArtifactThresholdUv) reasons.Add($"{EegConstants.ChannelNames[ch]} |x|={abs:0} µV");
                    else if (mx - mn > s.PeakToPeakThresholdUv) reasons.Add($"{EegConstants.ChannelNames[ch]} p-p={mx - mn:0} µV");
                }
            }
            ep.Rejected = reasons.Count > 0;
            ep.RejectReason = string.Join("; ", reasons);
            result.Epochs.Add(ep);
        }

        if (outside > 0)
            result.Warnings.Add($"{outside} uyaran markerı EEG kaydının dışında veya kenarında kaldığı için epochlanamadı.");
    }

    private static ChannelErp BuildChannel(ErpAnalysisResult result, string name, Func<ErpEpoch, double[]> getData)
    {
        var s = result.Settings;
        double fs = result.Session.SampleRate;
        var accepted = result.Epochs.Where(e => !e.Rejected).ToList();
        var target = Average(accepted.Where(e => e.Condition == ErpCondition.Target).Select(getData).ToList(), name, "Hedef", s, fs);
        var standard = Average(accepted.Where(e => e.Condition == ErpCondition.Standard).Select(getData).ToList(), name, "Standart", s, fs);

        ErpWaveform diff = null;
        if (!target.IsEmpty && !standard.IsEmpty)
        {
            int n = target.Mean.Length;
            diff = new ErpWaveform
            {
                ChannelName = name,
                ConditionName = "Fark (H−S)",
                TimesMs = target.TimesMs,
                Mean = new double[n],
                Sem = new double[n],
                EpochCount = Math.Min(target.EpochCount, standard.EpochCount),
            };
            for (int i = 0; i < n; i++)
            {
                diff.Mean[i] = target.Mean[i] - standard.Mean[i];
                diff.Sem[i] = Math.Sqrt(target.Sem[i] * target.Sem[i] + standard.Sem[i] * standard.Sem[i]);
            }
            DetectPeaks(diff, s);
        }
        return new ChannelErp { ChannelName = name, Target = target, Standard = standard, Difference = diff };
    }

    private static ErpWaveform Average(List<double[]> epochs, string channel, string condition, ErpFilterSettings s, double fs)
    {
        int pre = MsToSamples(-s.EpochStartMs, fs);
        int len = pre + MsToSamples(s.EpochEndMs, fs);
        var w = new ErpWaveform
        {
            ChannelName = channel,
            ConditionName = condition,
            TimesMs = Enumerable.Range(0, len).Select(i => (i - pre) * 1000.0 / fs).ToArray(),
            Mean = new double[len],
            Sem = new double[len],
            EpochCount = epochs.Count,
        };
        if (epochs.Count == 0) return w;

        int n = epochs.Count;
        for (int i = 0; i < len; i++)
        {
            double sum = 0;
            foreach (var e in epochs) sum += e[i];
            double mean = sum / n;
            double ss = 0;
            foreach (var e in epochs) ss += (e[i] - mean) * (e[i] - mean);
            w.Mean[i] = mean;
            w.Sem[i] = n > 1 ? Math.Sqrt(ss / (n - 1)) / Math.Sqrt(n) : 0;
        }
        DetectPeaks(w, s);
        return w;
    }

    /// <summary>N100 / N200 (negatif) ve P300 (pozitif) tepelerini pencereler içinde bulur, SNR hesaplar.</summary>
    public static void DetectPeaks(ErpWaveform w, ErpFilterSettings s)
    {
        if (w.IsEmpty) return;
        w.P300 = FindPeak(w, "P300", s.P300StartMs, s.P300EndMs, positive: true);
        w.N200 = FindPeak(w, "N200", s.N200StartMs, s.N200EndMs, positive: false);
        w.N100 = FindPeak(w, "N100", s.N100StartMs, s.N100EndMs, positive: false);

        var baseIdx = Enumerable.Range(0, w.TimesMs.Length)
            .Where(i => w.TimesMs[i] >= s.BaselineStartMs && w.TimesMs[i] < s.BaselineEndMs).ToList();
        w.BaselineRms = baseIdx.Count > 0 ? Math.Sqrt(baseIdx.Average(i => w.Mean[i] * w.Mean[i])) : 0;
        w.Snr = w.P300.Found && w.BaselineRms > 1e-9 ? Math.Abs(w.P300.AmplitudeUv) / w.BaselineRms : double.NaN;
    }

    private static ComponentPeak FindPeak(ErpWaveform w, string name, double fromMs, double toMs, bool positive)
    {
        var p = new ComponentPeak { Name = name };
        int best = -1, first = -1, last = -1;
        for (int i = 0; i < w.TimesMs.Length; i++)
        {
            if (w.TimesMs[i] < fromMs || w.TimesMs[i] > toMs) continue;
            if (first < 0) first = i;
            last = i;
            if (best < 0 || (positive ? w.Mean[i] > w.Mean[best] : w.Mean[i] < w.Mean[best])) best = i;
        }
        if (best < 0) return p;
        p.Found = true;
        p.LatencyMs = w.TimesMs[best];
        p.AmplitudeUv = w.Mean[best];
        p.AtWindowEdge = best == first || best == last;
        return p;
    }

    private static void ComputeSpectra(ErpAnalysisResult result, int eegCount)
    {
        var session = result.Session;
        double fs = session.SampleRate;
        var eeg = session.Data.Take(eegCount).ToArray();
        var names = EegConstants.ChannelNames.Take(eegCount).ToList();
        result.Spectrum = FrequencyAnalysis.Compute("Tüm kayıt", eeg, names, fs, new[] { (0, session.SampleCount) });

        // Dinlenme bölümleri: rest_start → rest_end
        var ranges = new List<(int, int)>();
        MarkerEvent open = null;
        foreach (var m in session.Markers.Where(m => m.Category == MarkerCategory.RestState))
        {
            if (m.Value.Trim().Equals("rest_start", StringComparison.OrdinalIgnoreCase)) open = m;
            else if (open != null)
            {
                int a = session.IndexOfTime(open.Timestamp), b = session.IndexOfTime(m.Timestamp);
                if (b > a) ranges.Add((a, b - a));
                open = null;
            }
        }
        if (ranges.Count > 0)
            result.RestSpectrum = FrequencyAnalysis.Compute("Dinlenme", eeg, names, fs, ranges);
    }

    private static void ComputeQuality(ErpAnalysisResult result, int eegCount)
    {
        var spec = result.Spectrum;
        double lineHz = result.Settings.NotchHz;
        int totalEpochs = result.Epochs.Count;
        var s = result.Settings;

        for (int ch = 0; ch < eegCount; ch++)
        {
            var q = new ChannelQuality { ChannelName = EegConstants.ChannelNames[ch] };
            if (spec != null)
            {
                // 1–45 Hz bant sınırlı RMS: PSD integrali
                double df = spec.Frequencies[1] - spec.Frequencies[0], pow = 0;
                for (int k = 0; k < spec.Frequencies.Length; k++)
                    if (spec.Frequencies[k] >= 1 && spec.Frequencies[k] <= 45) pow += spec.Power[ch][k] * df;
                q.RmsUv = Math.Sqrt(pow);
                q.LineNoisePercent = lineHz < result.Session.SampleRate / 2
                    ? 100 * FrequencyAnalysis.RelativePowerAround(spec.Power[ch], spec.Frequencies, lineHz, 1.5) : 0;
            }
            else
            {
                q.RmsUv = Statistics.Rms(result.FilteredData[ch], 0, result.FilteredData[ch].Length);
            }
            q.IsFlat = q.RmsUv < 0.5;
            if (totalEpochs > 0)
            {
                int bad = result.Epochs.Count(e => e.MaxAbs[ch] > s.ArtifactThresholdUv || e.PeakToPeak[ch] > s.PeakToPeakThresholdUv);
                q.RejectedEpochPercent = 100.0 * bad / totalEpochs;
            }
            q.Level = q.IsFlat || q.RmsUv > 80 || q.LineNoisePercent > 50 || q.RejectedEpochPercent > 40 ? QualityLevel.Poor
                    : q.RmsUv > 40 || q.LineNoisePercent > 20 || q.RejectedEpochPercent > 20 ? QualityLevel.Fair
                    : QualityLevel.Good;
            result.Quality.Add(q);
        }
    }
}
