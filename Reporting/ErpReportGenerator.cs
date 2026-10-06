using System.Globalization;
using System.Text;
using XOnERPStudio.Models;
using XOnERPStudio.SignalProcessing;

namespace XOnERPStudio.Reporting;

/// <summary>
/// Saf HTML5/CSS3 + gömülü SVG grafiklerden oluşan, tek dosyalık ERP raporu üretir.
/// Tarayıcıda açılır; "Yazdır → PDF olarak kaydet" ile PDF'e dönüştürülebilir.
/// </summary>
public static class ErpReportGenerator
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    private static string E(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");
    private static string N(double v, string fmt = "0.0") => double.IsNaN(v) || double.IsInfinity(v) ? "—" : v.ToString(fmt, Tr);
    private static string Pct(double v) => double.IsNaN(v) ? "—" : (v * 100).ToString("0.0", Tr) + "%";

    public static string DefaultReportFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XOnERPStudio", "Raporlar");

    /// <summary>Raporu oluşturup dosyaya yazar ve yolunu döner.</summary>
    public static string Save(ErpAnalysisResult r, string path = null)
    {
        if (path == null)
        {
            Directory.CreateDirectory(DefaultReportFolder);
            string id = string.Join("_", (string.IsNullOrWhiteSpace(r.Session.ParticipantId) ? "oturum" : r.Session.ParticipantId)
                .Split(Path.GetInvalidFileNameChars()));
            path = Path.Combine(DefaultReportFolder, $"ERP_{id}_{DateTime.Now:yyyyMMdd_HHmmss}.html");
        }
        File.WriteAllText(path, Generate(r), new UTF8Encoding(false));
        return path;
    }

    public static string Generate(ErpAnalysisResult r)
    {
        var s = r.Session;
        var b = r.Behavior;
        var set = r.Settings;
        var sb = new StringBuilder();

        sb.Append("<!doctype html><html lang='tr'><head><meta charset='utf-8'>");
        sb.Append("<meta name='viewport' content='width=device-width, initial-scale=1'>");
        sb.Append($"<title>ERP Raporu — {E(s.ParticipantId)}</title><style>{Css}</style></head><body><main>");

        // 1. Başlık
        sb.Append("<header><div><h1>ERP Analiz Raporu</h1><p class='sub'>Görsel Oddball Dikkat Testi · Olay İlişkili Potansiyeller</p></div>");
        sb.Append($"<div class='brand'>XOnERPStudio<br><span>{r.AnalyzedAt:dd.MM.yyyy HH:mm}</span></div></header>");

        sb.Append("<section class='meta'>");
        Meta(sb, "Katılımcı ID", s.ParticipantId);
        Meta(sb, "Ad Soyad", s.ParticipantName);
        Meta(sb, "Yaş", s.ParticipantAge);
        Meta(sb, "Oturum tarihi", s.RecordedAt.ToString("dd.MM.yyyy HH:mm", Tr));
        Meta(sb, "Cihaz", s.DeviceName);
        Meta(sb, "Örnekleme hızı", $"{s.SampleRate:0} Hz");
        var dur = TimeSpan.FromSeconds(s.DurationSeconds);
        Meta(sb, "Kayıt süresi", $"{(int)dur.TotalMinutes}:{dur.Seconds:00} dk");
        Meta(sb, "Kaynak", s.SourceDescription);
        if (!string.IsNullOrWhiteSpace(s.Notes)) Meta(sb, "Notlar", s.Notes);
        sb.Append("</section>");

        // 2. Skor kartı
        sb.Append("<h2>Dikkat Testi Skor Kartı</h2><section class='cards'>");
        Card(sb, "Toplam deneme", b.TotalTrials.ToString(), $"Hedef {b.TargetCount} · Çeldirici {b.DistractorCount}");
        if (b.HasResponseMarkers)
        {
            Card(sb, "Doğru tepki (Hit)", Pct(b.HitRate), $"{b.Hits}/{b.TargetCount}", "good");
            Card(sb, "Hatalı tepki (FA)", Pct(b.FalseAlarmRate), $"{b.FalseAlarms}/{b.DistractorCount}", b.FalseAlarmRate > 0.1 ? "bad" : "");
            Card(sb, "Kaçırma", Pct(b.MissRate), $"{b.Misses}/{b.TargetCount}", b.MissRate > 0.2 ? "bad" : "");
            Card(sb, "Tepki süresi", $"{N(b.MeanRtMs, "0")} ms", $"SS ± {N(b.SdRtMs, "0")} ms · medyan {N(b.MedianRtMs, "0")}");
            Card(sb, "d′ duyarlılık", N(b.DPrime, "0.00"), $"c = {N(b.Criterion, "0.00")}", b.DPrime >= 2 ? "good" : "");
        }
        else
        {
            Card(sb, "Davranış", "—", "Kayıtta tepki markerı yok (pasif oddball)");
        }
        Card(sb, "Hedef epoch", $"{r.TargetEpochsAccepted}/{r.TargetEpochsTotal}", "kabul / toplam");
        Card(sb, "Standart epoch", $"{r.StandardEpochsAccepted}/{r.StandardEpochsTotal}", "kabul / toplam");
        sb.Append("</section>");
        if (!string.IsNullOrEmpty(b.TargetColor)) sb.Append($"<p class='muted'>Hedef renk: <b>{E(b.TargetColor)}</b></p>");

        if (r.Warnings.Count > 0)
        {
            sb.Append("<div class='warn'><b>Uyarılar</b><ul>");
            foreach (var w in r.Warnings) sb.Append($"<li>{E(w)}</li>");
            sb.Append("</ul></div>");
        }

        // 3. ERP tablosu
        sb.Append("<h2>ERP Bileşen İstatistikleri</h2>");
        sb.Append("<table><thead><tr><th>Kanal</th><th>P300 gecikme<br>(ms)</th><th>P300 genlik<br>(µV)</th><th>N200 gecikme<br>(ms)</th><th>N200 genlik<br>(µV)</th>");
        sb.Append("<th>N100<br>(ms / µV)</th><th>Fark P300<br>(µV)</th><th>SNR</th><th>Hedef epoch<br>kabul/red</th></tr></thead><tbody>");
        int tAcc = r.TargetEpochsAccepted, tRej = r.TargetEpochsTotal - tAcc;
        foreach (var name in EegConstants.ChannelNames.Append(EegConstants.VirtualParietalName))
        {
            var c = r.Get(name);
            if (c == null) continue;
            var t = c.Target;
            bool hl = EegConstants.P300Channels.Contains(name) || name == EegConstants.VirtualParietalName;
            sb.Append($"<tr{(hl ? " class='hl'" : "")}><td><b>{E(name)}</b></td>");
            sb.Append($"<td>{Lat(t?.P300)}</td><td>{Amp(t?.P300)}</td><td>{Lat(t?.N200)}</td><td>{Amp(t?.N200)}</td>");
            sb.Append($"<td>{Lat(t?.N100)} / {Amp(t?.N100)}</td><td>{Amp(c.Difference?.P300)}</td><td>{N(t?.Snr ?? double.NaN, "0.0")}</td>");
            sb.Append($"<td>{tAcc} / {tRej}</td></tr>");
        }
        sb.Append("</tbody></table>");
        sb.Append($"<p class='muted'>{E(EegConstants.VirtualParietalName)} = (P3 + P4) / 2 — X.on montajında Pz bulunmadığından sanal parietal orta hat kanalı. " +
                  $"P300 penceresi {set.P300StartMs:0}–{set.P300EndMs:0} ms, N200 {set.N200StartMs:0}–{set.N200EndMs:0} ms, N100 {set.N100StartMs:0}–{set.N100EndMs:0} ms. " +
                  "SNR = |P300 genliği| / baseline RMS (ortalama dalga).</p>");

        // 4. Grafikler
        sb.Append("<h2>ERP Dalga Formları</h2><section class='charts'>");
        sb.Append($"<figure>{SvgCharts.ErpChart(r.Get("Cz"), set, title: "Cz — Hedef vs Standart")}</figure>");
        sb.Append($"<figure>{SvgCharts.ErpChart(r.Get(EegConstants.VirtualParietalName), set, title: "Pz* (P3+P4)/2 — Hedef vs Standart")}</figure>");
        var best = r.BestP300Channel();
        if (best != null && best.ChannelName != "Cz")
            sb.Append($"<figure>{SvgCharts.ErpChart(best, set, title: $"{best.ChannelName} — en büyük P300")}</figure>");
        sb.Append("</section>");

        sb.Append("<h2>7 Kanal Topografisi</h2><section class='topo-wrap'>");
        sb.Append($"<figure>{SvgCharts.Topography(r)}</figure></section>");

        // 5. Spektrum ve kalite
        var spec = r.RestSpectrum ?? r.Spectrum;
        if (spec != null)
        {
            sb.Append($"<h2>Frekans Bantları — {E(spec.Label)}</h2>");
            sb.Append($"<figure>{SvgCharts.BandPowerChart(spec)}</figure>");
            sb.Append("<table><thead><tr><th>Kanal</th>");
            foreach (var band in FrequencyAnalysis.Bands) sb.Append($"<th>{band.Name}<br>({band.Low:0}–{band.High:0} Hz)</th>");
            sb.Append("</tr></thead><tbody>");
            for (int ch = 0; ch < spec.BandRelative.Length; ch++)
            {
                sb.Append($"<tr><td><b>{EegConstants.ChannelNames[ch]}</b></td>");
                for (int k = 0; k < spec.BandRelative[ch].Length; k++)
                    sb.Append($"<td>{Pct(spec.BandRelative[ch][k])} <span class='muted'>({N(spec.BandPower[ch][k], "0.0")} µV²)</span></td>");
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
        }

        sb.Append("<h2>Sinyal Kalitesi</h2><table><thead><tr><th>Kanal</th><th>RMS 1–45 Hz (µV)</th><th>Şebeke gürültüsü</th><th>Reddedilen epoch</th><th>Değerlendirme</th></tr></thead><tbody>");
        foreach (var q in r.Quality)
        {
            string lvl = q.IsFlat ? "Düz sinyal" : q.Level switch { QualityLevel.Good => "İyi", QualityLevel.Fair => "Orta", _ => "Zayıf" };
            string cls = q.Level switch { QualityLevel.Good => "ok", QualityLevel.Fair => "mid", _ => "bad" };
            sb.Append($"<tr><td><b>{q.ChannelName}</b></td><td>{N(q.RmsUv)}</td><td>%{N(q.LineNoisePercent, "0")}</td><td>%{N(q.RejectedEpochPercent, "0")}</td><td><span class='pill {cls}'>{lvl}</span></td></tr>");
        }
        sb.Append("</tbody></table>");

        // 6. Yorum
        sb.Append("<h2>Nörobilişsel Değerlendirme</h2><section class='interp'>");
        foreach (var p in Interpret(r)) sb.Append($"<p>{p}</p>");
        sb.Append("</section>");

        // 7. Yöntem
        sb.Append("<h2>Yöntem</h2><section class='method'><ul>");
        sb.Append($"<li>Filtreleme: {(set.BandpassEnabled ? $"{set.FilterOrder}. derece Butterworth bant geçiren {N(set.HighPassHz, "0.0#")}–{N(set.LowPassHz, "0.#")} Hz" : "bant geçiren yok")}, " +
                  $"{(set.NotchEnabled ? $"{set.NotchHz:0} Hz çentik (Q={set.NotchQ:0})" : "çentik yok")}; ileri-geri (sıfır faz) uygulama.</li>");
        sb.Append($"<li>Epoch: {set.EpochStartMs:0} … {set.EpochEndMs:0} ms; baseline {set.BaselineStartMs:0} … {set.BaselineEndMs:0} ms ortalaması çıkarıldı.</li>");
        sb.Append(set.ArtifactRejectionEnabled
            ? $"<li>Artefakt reddi: herhangi bir kanalda |x| &gt; {set.ArtifactThresholdUv:0} µV veya tepe-tepe &gt; {set.PeakToPeakThresholdUv:0} µV olan epochlar dışlandı.</li>"
            : "<li>Artefakt reddi kapalı.</li>");
        sb.Append("<li>Koşullar: <code>color_onset_&lt;renk&gt;</code> markerları tetikleyici; hedef renk <code>target_color_&lt;renk&gt;</code> veya eşlik eden <code>stimulus_start</code> ile belirlendi. " +
                  "Tepkiler (<code>response_correct / response_incorrect / response_miss</code>) en yakın önceki uyarana atandı.</li>");
        sb.Append("<li>d′ = Z(isabet) − Z(yanlış alarm), log-doğrusal düzeltme ile (Hautus, 1995).</li>");
        sb.Append("<li>Zaman eşleştirmesi: LSL ortak saati (clock sync) — EEG ve marker akışları aynı zaman tabanında.</li>");
        foreach (var note in s.ImportNotes) sb.Append($"<li class='muted'>{E(note)}</li>");
        sb.Append("</ul></section>");

        sb.Append("<footer>Bu rapor araştırma ve eğitim amaçlıdır; tek başına klinik tanı için kullanılamaz. " +
                  "Normatif değerler yaş, montaj, referans ve uyaran parametrelerine göre değişir.</footer>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    private static string Lat(ComponentPeak p) => p is { Found: true } ? N(p.LatencyMs, "0") + (p.AtWindowEdge ? "*" : "") : "—";
    private static string Amp(ComponentPeak p) => p is { Found: true } ? N(p.AmplitudeUv, "0.0") : "—";

    private static void Meta(StringBuilder sb, string k, string v) =>
        sb.Append($"<div><span>{E(k)}</span><b>{E(string.IsNullOrWhiteSpace(v) ? "—" : v)}</b></div>");

    private static void Card(StringBuilder sb, string title, string value, string sub, string cls = "") =>
        sb.Append($"<div class='card {cls}'><span>{E(title)}</span><b>{E(value)}</b><small>{E(sub)}</small></div>");

    /// <summary>Bulgulara dayalı, temkinli dille yazılmış yorum paragrafları.</summary>
    public static List<string> Interpret(ErpAnalysisResult r)
    {
        var list = new List<string>();
        var par = r.Get(EegConstants.VirtualParietalName) ?? r.BestP300Channel();
        var t = par?.Target;
        if (t is not { IsEmpty: false } || !t.P300.Found)
        {
            list.Add("Hedef koşulu için yeterli epoch bulunamadığından P300 değerlendirmesi yapılamadı.");
            return list;
        }

        double amp = t.P300.AmplitudeUv, lat = t.P300.LatencyMs;
        var diff = par.Difference;
        bool reliable = false;
        if (diff is { IsEmpty: false } && diff.P300.Found)
        {
            int i = diff.IndexOfTime(diff.P300.LatencyMs);
            double sem = i >= 0 ? diff.Sem[i] : double.NaN;
            reliable = diff.P300.AmplitudeUv > 2 * sem && diff.P300.AmplitudeUv > 1.5;
            list.Add(reliable
                ? $"<b>Oddball etkisi belirgin.</b> Parietal bölgede hedef uyaranlar, standart uyaranlara göre {N(diff.P300.AmplitudeUv)} µV daha büyük bir pozitif tepe " +
                  $"({N(diff.P300.LatencyMs, "0")} ms) oluşturmuştur; fark, standart hatanın iki katını aşmaktadır. Bu bulgu, katılımcının hedef rengi seçici olarak fark ettiğini ve dikkat kaynaklarını hedefe yönlendirdiğini gösterir."
                : $"<b>Oddball etkisi zayıf.</b> Hedef ve standart dalgalar arasındaki parietal fark ({N(diff.P300.AmplitudeUv)} µV) ölçüm belirsizliğinin belirgin şekilde üstünde değildir. " +
                  "Deneme sayısının artırılması, artefaktların azaltılması veya dikkat düzeyinin kontrol edilmesi önerilir.");
        }

        string ampText = amp < 5 ? "görece düşük" : amp <= 15 ? "tipik aralıkta" : "görece yüksek";
        list.Add($"<b>P300 genliği</b> ({par.ChannelName}: {N(amp)} µV) {ampText}. P300 genliği, göreve ayrılan dikkat kaynağı miktarı (attentional resource allocation) " +
                 "ve uyaranın öznel olasılığı ile ilişkilidir; daha büyük genlik genellikle hedefe daha fazla kaynak ayrıldığını yansıtır. Genlik, referans/montaj ve deneme sayısından da etkilenir.");

        string latText = lat < 300 ? "hızlı" : lat <= 450 ? "sağlıklı yetişkinler için beklenen aralıkta (≈300–450 ms)" : "uzamış";
        list.Add($"<b>P300 gecikmesi</b> ({N(lat, "0")} ms) {latText}. Gecikme, uyaran değerlendirme ve sınıflandırma süresinin, yani bilişsel işlemleme hızının bir göstergesidir; " +
                 "yaşla birlikte uzar ve dikkat/yorgunluk durumuna duyarlıdır.");

        var p3 = r.Get("P3")?.Target?.P300; var p4 = r.Get("P4")?.Target?.P300;
        var f3 = r.Get("F3")?.Target?.P300; var f4 = r.Get("F4")?.Target?.P300;
        if (p3 is { Found: true } && p4 is { Found: true } && f3 is { Found: true } && f4 is { Found: true })
        {
            double parietal = (p3.AmplitudeUv + p4.AmplitudeUv) / 2, frontal = (f3.AmplitudeUv + f4.AmplitudeUv) / 2;
            list.Add(parietal > frontal
                ? $"<b>Topografi:</b> P300 parietal bölgede ({N(parietal)} µV) frontale ({N(frontal)} µV) göre daha büyüktür; bu, hedef tespitiyle ilişkili klasik P3b dağılımıyla uyumludur."
                : $"<b>Topografi:</b> P300 frontal bölgede ({N(frontal)} µV) parietale ({N(parietal)} µV) göre daha büyüktür; bu dağılım yenilik/yönelim yanıtı (P3a) veya frontal artefakt katkısını düşündürebilir.");
            double asym = p3.AmplitudeUv - p4.AmplitudeUv;
            if (Math.Abs(asym) > 3) list.Add($"Parietal hemisferik asimetri dikkat çekicidir (P3 − P4 = {N(asym)} µV).");
        }

        var n2 = t.N200;
        if (n2.Found && n2.AmplitudeUv < -1)
            list.Add($"<b>N200</b> ({N(n2.LatencyMs, "0")} ms, {N(n2.AmplitudeUv)} µV) uyaran uyuşmazlığı tespiti ve bilişsel kontrol süreçlerini yansıtır.");

        var b = r.Behavior;
        if (b.HasResponseMarkers)
        {
            string perf = b.DPrime >= 3 ? "çok iyi" : b.DPrime >= 2 ? "iyi" : b.DPrime >= 1 ? "orta" : "düşük";
            list.Add($"<b>Davranışsal performans</b>: isabet oranı {Pct(b.HitRate)}, yanlış alarm oranı {Pct(b.FalseAlarmRate)}, " +
                     $"ortalama tepki süresi {N(b.MeanRtMs, "0")} ± {N(b.SdRtMs, "0")} ms; d′ = {N(b.DPrime, "0.00")} ile hedef/çeldirici ayrımı {perf} düzeydedir." +
                     (b.SdRtMs > 150 ? " Tepki sürelerindeki yüksek değişkenlik, dikkatte dalgalanmaya işaret edebilir." : ""));
        }

        if (r.TargetEpochsAccepted < 20)
            list.Add($"<i>Not: Hedef koşulunda yalnızca {r.TargetEpochsAccepted} temiz epoch vardır; güvenilir bireysel P300 kestirimi için ≥ 20–30 epoch önerilir.</i>");
        return list;
    }

    private const string Css = @"
:root{--fg:#1d2330;--muted:#6b7385;--line:#e3e6ec;--bg:#ffffff;--soft:#f5f7fa;--accent:#2f6fd6;--good:#1f9d55;--bad:#c53030;--mid:#b7791f}
*{box-sizing:border-box}body{margin:0;background:#eef1f5;color:var(--fg);font:14px/1.55 'Segoe UI',system-ui,-apple-system,Roboto,Arial,sans-serif}
main{max-width:1060px;margin:24px auto;background:var(--bg);padding:36px 44px;border-radius:12px;box-shadow:0 4px 24px rgba(20,30,50,.08)}
header{display:flex;justify-content:space-between;align-items:flex-end;border-bottom:3px solid var(--accent);padding-bottom:14px;margin-bottom:18px}
h1{margin:0;font-size:28px;letter-spacing:-.3px}.sub{margin:2px 0 0;color:var(--muted)}
.brand{text-align:right;font-weight:700;color:var(--accent)}.brand span{font-weight:400;color:var(--muted);font-size:12px}
h2{font-size:18px;margin:30px 0 12px;padding-bottom:6px;border-bottom:1px solid var(--line)}
.meta{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:8px 18px;background:var(--soft);padding:14px 18px;border-radius:8px}
.meta div{display:flex;flex-direction:column}.meta span{font-size:11px;color:var(--muted);text-transform:uppercase;letter-spacing:.4px}
.cards{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:10px}
.card{border:1px solid var(--line);border-radius:8px;padding:10px 12px;display:flex;flex-direction:column;background:#fff}
.card span{font-size:12px;color:var(--muted)}.card b{font-size:22px;margin:2px 0}.card small{color:var(--muted);font-size:11.5px}
.card.good b{color:var(--good)}.card.bad b{color:var(--bad)}
.warn{background:#fff8e6;border:1px solid #f2d48a;border-radius:8px;padding:10px 14px;margin-top:14px}.warn ul{margin:6px 0 0 18px;padding:0}
table{width:100%;border-collapse:collapse;font-size:13px;margin-top:6px;font-variant-numeric:tabular-nums}
th{background:var(--soft);text-align:center;font-weight:600;font-size:12px;color:#3a4152}
th,td{border-bottom:1px solid var(--line);padding:6px 8px;text-align:center}td:first-child,th:first-child{text-align:left}
tr.hl td{background:#f3f7ff}
.charts{display:grid;grid-template-columns:1fr;gap:10px}figure{margin:0}
.chart{width:100%;height:auto;background:#fff;border:1px solid var(--line);border-radius:8px}
.topo-wrap{display:flex;justify-content:center}.topo{max-width:560px}
.chart .ct{font-weight:700;font-size:13px;fill:var(--fg)}.chart .ax{font-size:10px;fill:var(--muted)}.chart .lg{font-size:10.5px;fill:var(--fg)}
.chart .pk{font-size:10.5px;font-weight:700}.chart .grid{stroke:#eceef3;stroke-width:1}.chart .zero{stroke:#b7bdc9;stroke-width:1}
.chart .onset{stroke:#3a4152;stroke-width:1.6}.chart .frame{fill:none;stroke:var(--line)}.chart .muted{fill:var(--muted)}
.chart .head{fill:#fafbfd;stroke:#9aa3b5;stroke-width:2}.chart .box{fill:#fff;stroke:#c9cfdb}.chart .bn{font-size:11px;font-weight:700;fill:var(--fg)}
.interp p{margin:0 0 10px;text-align:justify}.method ul{margin:0;padding-left:18px}.muted{color:var(--muted);font-size:12.5px}
.pill{padding:2px 10px;border-radius:12px;font-size:12px;font-weight:600}.pill.ok{background:#e3f6ec;color:var(--good)}.pill.mid{background:#fdf3dc;color:var(--mid)}.pill.bad{background:#fde8e8;color:var(--bad)}
code{background:var(--soft);padding:1px 5px;border-radius:4px;font-size:12px}
footer{margin-top:30px;padding-top:12px;border-top:1px solid var(--line);color:var(--muted);font-size:12px}
@media print{body{background:#fff}main{box-shadow:none;margin:0;max-width:none;padding:10mm}h2{break-after:avoid}figure,table,.card{break-inside:avoid}}
@media (max-width:640px){main{padding:18px;margin:0;border-radius:0}header{flex-direction:column;align-items:flex-start;gap:6px}.brand{text-align:left}}
";
}
