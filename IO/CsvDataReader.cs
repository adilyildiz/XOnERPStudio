using System.Globalization;
using System.Text;
using XOnERPStudio.Models;

namespace XOnERPStudio.IO;

/// <summary>
/// EEG CSV ve marker CSV okuyucu. Ayraç (, ; tab), ondalık ayırıcı ve sütunlar otomatik algılanır.
///
/// EEG CSV: başlık satırı; zaman sütunu (time / timestamp / lsl_time / zaman ...) ve kanal sütunları
/// (F3, F4, C3, Cz, C4, P3, P4 [, AUX]). İsteğe bağlı bir marker sütunu varsa (marker / event / label)
/// birleşik dosya olarak işlenir. Zaman sütunu yoksa örnekleme hızından üretilir.
///
/// Marker CSV: zaman sütunu + marker sütunu.
/// </summary>
public static class CsvDataReader
{
    private static readonly string[] TimeColumnNames =
        { "timestamp", "time", "lsl_time", "lsl_timestamp", "time_stamp", "zaman", "t", "time_s", "seconds", "lsltime" };
    private static readonly string[] MarkerColumnNames =
        { "marker", "markers", "event", "events", "label", "value", "trigger", "annotation", "description" };

    private class Table
    {
        public string[] Header;
        public List<string[]> Rows = new();
        public CultureInfo Culture = CultureInfo.InvariantCulture;
        public char Delimiter;
    }

    public static EegRecordingSession LoadEeg(string path, double defaultSampleRate = EegConstants.DefaultSampleRate)
    {
        var table = ReadTable(path);
        int timeCol = FindColumn(table.Header, TimeColumnNames);
        int markerCol = FindColumn(table.Header, MarkerColumnNames);

        // Kanal sütunları: önce bilinen adlar, yoksa sayısal tüm sütunlar
        var chCols = new List<int>();
        var chNames = new List<string>();
        var known = EegConstants.ChannelNames.Append(EegConstants.AuxChannelName).ToArray();
        for (int c = 0; c < table.Header.Length; c++)
        {
            string h = CleanHeader(table.Header[c]);
            if (known.Any(k => k.Equals(h, StringComparison.OrdinalIgnoreCase)))
            {
                chCols.Add(c);
                chNames.Add(known.First(k => k.Equals(h, StringComparison.OrdinalIgnoreCase)));
            }
        }
        if (chCols.Count < 7)
        {
            chCols.Clear(); chNames.Clear();
            var first = table.Rows.FirstOrDefault() ?? Array.Empty<string>();
            for (int c = 0; c < table.Header.Length; c++)
            {
                if (c == timeCol || c == markerCol || c >= first.Length) continue;
                if (TryParse(first[c], table.Culture, out _))
                {
                    chCols.Add(c);
                    chNames.Add(CleanHeader(table.Header[c]));
                }
            }
        }
        if (chCols.Count == 0) throw new InvalidDataException("CSV içinde sayısal EEG sütunu bulunamadı.");

        var session = new EegRecordingSession
        {
            SourceDescription = Path.GetFileName(path),
            SampleRate = defaultSampleRate,
            ChannelNames = chNames,
            RecordedAt = File.GetLastWriteTime(path),
        };

        int n = table.Rows.Count;
        var ts = new List<double>(n);
        var data = chCols.Select(_ => new List<double>(n)).ToArray();
        int skipped = 0;
        for (int r = 0; r < n; r++)
        {
            var row = table.Rows[r];
            double t = r / defaultSampleRate;
            if (timeCol >= 0 && (timeCol >= row.Length || !TryParse(row[timeCol], table.Culture, out t))) { skipped++; continue; }

            var vals = new double[chCols.Count];
            bool ok = true;
            for (int k = 0; k < chCols.Count; k++)
            {
                int c = chCols[k];
                if (c >= row.Length || !TryParse(row[c], table.Culture, out vals[k])) { ok = false; break; }
            }
            if (!ok) { skipped++; continue; }

            ts.Add(t);
            for (int k = 0; k < vals.Length; k++) data[k].Add(vals[k]);

            if (markerCol >= 0 && markerCol < row.Length)
            {
                string m = row[markerCol].Trim().Trim('"');
                if (m.Length > 0 && m != "0" && !m.Equals("nan", StringComparison.OrdinalIgnoreCase))
                    foreach (var part in m.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        session.Markers.Add(new MarkerEvent(t, part)); // aynı örnekteki birden çok marker "a|b" olarak yazılır
            }
        }
        if (skipped > 0) session.ImportNotes.Add($"{skipped} satır okunamadı ve atlandı.");

        // Zaman milisaniye cinsindeyse saniyeye çevir
        var tsArr = ts.ToArray();
        if (timeCol >= 0 && tsArr.Length > 10)
        {
            double medDt = MedianDiff(tsArr);
            if (medDt > 0.5 && medDt < 50) // ~4 ms aralık → ms birimi
            {
                for (int i = 0; i < tsArr.Length; i++) tsArr[i] /= 1000.0;
                foreach (var m in session.Markers) m.Timestamp /= 1000.0;
                medDt /= 1000.0;
                session.ImportNotes.Add("Zaman sütunu milisaniye olarak algılandı ve saniyeye çevrildi.");
            }
            if (medDt > 0) session.SampleRate = Math.Round(1.0 / medDt);
            if (!IsMonotonic(tsArr)) session.ImportNotes.Add("Uyarı: zaman damgaları monoton artan değil.");
        }
        else if (timeCol < 0)
        {
            session.ImportNotes.Add($"Zaman sütunu yok; örnekleme hızı {defaultSampleRate} Hz varsayıldı.");
        }

        session.Timestamps = tsArr;
        session.Data = data.Select(d => d.ToArray()).ToArray();
        session.NormalizeChannelOrder();
        return session;
    }

    /// <summary>Marker CSV okur. EEG oturumu verilirse zaman tabanı uyumsuzluğunu sezgisel olarak düzeltir.</summary>
    public static List<MarkerEvent> LoadMarkers(string path, EegRecordingSession alignTo = null)
    {
        var table = ReadTable(path);
        int timeCol = FindColumn(table.Header, TimeColumnNames);
        int markerCol = FindColumn(table.Header, MarkerColumnNames);

        if (timeCol < 0 || markerCol < 0)
        {
            // Başlıksız iki sütun: zaman, marker
            if (table.Header.Length >= 2 && TryParse(table.Header[0], table.Culture, out _))
            {
                table.Rows.Insert(0, table.Header);
                timeCol = 0; markerCol = 1;
            }
            else if (timeCol < 0 && table.Header.Length >= 2) { timeCol = 0; markerCol = markerCol < 0 ? 1 : markerCol; }
            else throw new InvalidDataException("Marker CSV'de zaman ve marker sütunları bulunamadı.");
        }

        var list = new List<MarkerEvent>();
        foreach (var row in table.Rows)
        {
            if (timeCol >= row.Length || markerCol >= row.Length) continue;
            if (!TryParse(row[timeCol], table.Culture, out double t)) continue;
            string v = row[markerCol].Trim().Trim('"');
            if (v.Length == 0) continue;
            list.Add(new MarkerEvent(t, v));
        }

        if (alignTo != null && alignTo.SampleCount > 0 && list.Count > 0)
        {
            double first = list.Min(m => m.Timestamp), last = list.Max(m => m.Timestamp);
            bool inside = first >= alignTo.StartTime - 5 && last <= alignTo.EndTime + 5;
            if (!inside)
            {
                // Göreli zaman (0'dan başlayan) olduğu varsayılır → EEG başlangıcına kaydır
                if (last - first <= alignTo.DurationSeconds + 5 && first < alignTo.StartTime)
                {
                    double shift = alignTo.StartTime;
                    foreach (var m in list) m.Timestamp += shift;
                    alignTo.ImportNotes.Add($"Marker zamanları EEG ile aynı saat tabanında değildi; kayıt başlangıcına göre göreli kabul edilip {shift:F3} s kaydırıldı.");
                }
                else
                {
                    alignTo.ImportNotes.Add("Uyarı: marker zamanları EEG kaydının dışında; eşleştirme başarısız olabilir.");
                }
            }
        }
        return list;
    }

    // ------------------------------------------------------------------

    private static Table ReadTable(string path)
    {
        var lines = ReadAllLinesSmart(path).Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#")).ToList();
        if (lines.Count == 0) throw new InvalidDataException("Dosya boş.");

        char delim = DetectDelimiter(lines[0]);
        var t = new Table { Delimiter = delim, Header = Split(lines[0], delim) };
        for (int i = 1; i < lines.Count; i++) t.Rows.Add(Split(lines[i], delim));

        // ';' ayraçlı ve ondalık virgüllü (Türkçe Excel) dosyalar
        if (delim != ',' && t.Rows.Take(20).SelectMany(r => r).Any(c => c.Contains(',') && !c.Contains('.')))
            t.Culture = CultureInfo.GetCultureInfo("tr-TR");
        return t;
    }

    private static IEnumerable<string> ReadAllLinesSmart(string path)
    {
        var bytes = File.ReadAllBytes(path);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.GetEncoding(1254).GetString(bytes); }
        return text.TrimStart('﻿').Split('\n').Select(l => l.TrimEnd('\r'));
    }

    private static char DetectDelimiter(string header)
    {
        var cands = new[] { '\t', ';', ',' };
        return cands.OrderByDescending(c => header.Count(ch => ch == c)).First();
    }

    private static string[] Split(string line, char delim)
    {
        var res = new List<string>();
        var sb = new StringBuilder();
        bool q = false;
        foreach (char ch in line)
        {
            if (ch == '"') { q = !q; continue; }
            if (ch == delim && !q) { res.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        res.Add(sb.ToString());
        return res.ToArray();
    }

    private static string CleanHeader(string h) => h.Trim().Trim('"').Split(' ', '(', '[')[0].Trim();

    private static int FindColumn(string[] header, string[] names)
    {
        for (int c = 0; c < header.Length; c++)
        {
            string h = CleanHeader(header[c]).ToLowerInvariant();
            if (names.Contains(h)) return c;
        }
        return -1;
    }

    private static bool TryParse(string s, CultureInfo culture, out double v) =>
        double.TryParse(s.Trim().Trim('"'), NumberStyles.Float, culture, out v);

    private static double MedianDiff(double[] t)
    {
        var d = new double[Math.Min(t.Length - 1, 2000)];
        for (int i = 0; i < d.Length; i++) d[i] = t[i + 1] - t[i];
        Array.Sort(d);
        return d[d.Length / 2];
    }

    private static bool IsMonotonic(double[] t)
    {
        for (int i = 1; i < t.Length; i++) if (t[i] < t[i - 1]) return false;
        return true;
    }
}
