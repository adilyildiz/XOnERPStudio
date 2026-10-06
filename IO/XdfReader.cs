using System.Globalization;
using System.Text;
using System.Xml.Linq;
using XOnERPStudio.Models;

namespace XOnERPStudio.IO;

/// <summary>
/// LabRecorder .xdf (Extensible Data Format 1.0) ikili dosya ayrıştırıcısı.
/// Tüm akışları okur, ClockOffset kayıtlarıyla saat senkronizasyonu yapar, düzenli akışlarda
/// zaman damgası titreşimini (jitter) giderir; ardından EEG ve marker akışlarını oturuma dönüştürür.
/// </summary>
public static class XdfReader
{
    public class XdfStream
    {
        public uint Id { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public int ChannelCount { get; set; }
        public double NominalSrate { get; set; }
        public string ChannelFormat { get; set; } = "float32";
        public List<string> ChannelLabels { get; set; } = new();
        public string HostName { get; set; } = "";
        public List<double> Timestamps { get; } = new();
        public List<double[]> NumericSamples { get; } = new();
        public List<string[]> StringSamples { get; } = new();
        public List<(double collection, double offset)> ClockOffsets { get; } = new();

        public bool IsString => ChannelFormat == "string";
        public int SampleCount => Timestamps.Count;
        public override string ToString() => $"{Name} ({Type}, {ChannelCount} kn, {NominalSrate:0.##} Hz, {SampleCount} örnek)";
    }

    /// <summary>Dosyadaki tüm akışları ham olarak okur (saat düzeltmesi uygulanmış).</summary>
    public static List<XdfStream> ReadStreams(string path)
    {
        var streams = new Dictionary<uint, XdfStream>();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var br = new BinaryReader(fs);

        var magic = br.ReadBytes(4);
        if (Encoding.ASCII.GetString(magic) != "XDF:") throw new InvalidDataException("Geçerli bir XDF dosyası değil (XDF: imzası yok).");

        while (fs.Position < fs.Length)
        {
            long chunkLen;
            try { chunkLen = ReadVarLen(br); }
            catch (EndOfStreamException) { break; }
            if (chunkLen < 2 || fs.Position + chunkLen > fs.Length) break; // kesik dosya
            long chunkEnd = fs.Position + chunkLen;
            ushort tag = br.ReadUInt16();

            try
            {
                switch (tag)
                {
                    case 2: // StreamHeader
                    {
                        uint id = br.ReadUInt32();
                        var xml = ReadXml(br, chunkEnd);
                        streams[id] = ParseHeader(id, xml);
                        break;
                    }
                    case 3: // Samples
                    {
                        uint id = br.ReadUInt32();
                        if (streams.TryGetValue(id, out var st)) ReadSamples(br, st, chunkEnd);
                        break;
                    }
                    case 4: // ClockOffset
                    {
                        uint id = br.ReadUInt32();
                        double collection = br.ReadDouble();
                        double offset = br.ReadDouble();
                        if (streams.TryGetValue(id, out var st)) st.ClockOffsets.Add((collection, offset));
                        break;
                    }
                    // 1 FileHeader, 5 Boundary, 6 StreamFooter: içerik gerekmiyor
                }
            }
            catch (EndOfStreamException) { break; }
            fs.Position = chunkEnd;
        }

        foreach (var st in streams.Values)
        {
            ApplyClockOffsets(st);
            if (st.NominalSrate > 0 && !st.IsString) Dejitter(st);
        }
        return streams.Values.ToList();
    }

    /// <summary>XDF dosyasını EEG oturumuna dönüştürür. Akış seçimi otomatik yapılır.</summary>
    public static EegRecordingSession Load(string path, string eegStreamName = null, string markerStreamName = null)
    {
        var streams = ReadStreams(path);
        var eeg = (eegStreamName != null ? streams.FirstOrDefault(s => s.Name == eegStreamName) : null)
                  ?? streams.Where(s => !s.IsString && s.NominalSrate > 0 && s.ChannelCount >= 7 && s.SampleCount > 0)
                            .OrderByDescending(s => s.Name.Equals(EegConstants.DefaultEegStreamName, StringComparison.OrdinalIgnoreCase))
                            .ThenByDescending(s => s.Type.Equals("EEG", StringComparison.OrdinalIgnoreCase))
                            .ThenByDescending(s => s.SampleCount)
                            .FirstOrDefault()
                  ?? throw new InvalidDataException("XDF içinde en az 7 kanallı düzenli bir EEG akışı bulunamadı.\nAkışlar: " +
                                                     string.Join(", ", streams));

        var markerStreams = markerStreamName != null
            ? streams.Where(s => s.Name == markerStreamName).ToList()
            : streams.Where(s => s.IsString || s.Type.Equals("Markers", StringComparison.OrdinalIgnoreCase)).ToList();

        var session = new EegRecordingSession
        {
            SourceDescription = Path.GetFileName(path),
            SampleRate = eeg.NominalSrate,
            ChannelNames = Enumerable.Range(0, eeg.ChannelCount)
                .Select(i => i < eeg.ChannelLabels.Count && !string.IsNullOrWhiteSpace(eeg.ChannelLabels[i]) ? eeg.ChannelLabels[i] : $"Ch{i + 1}")
                .ToList(),
            Timestamps = eeg.Timestamps.ToArray(),
            RecordedAt = File.GetCreationTime(path),
        };
        session.Data = new double[eeg.ChannelCount][];
        for (int ch = 0; ch < eeg.ChannelCount; ch++)
        {
            var x = new double[eeg.SampleCount];
            for (int i = 0; i < x.Length; i++) x[i] = eeg.NumericSamples[i][ch];
            session.Data[ch] = x;
        }
        session.ImportNotes.Add($"EEG akışı: {eeg}");

        foreach (var ms in markerStreams)
        {
            session.ImportNotes.Add($"Marker akışı: {ms}");
            for (int i = 0; i < ms.SampleCount; i++)
            {
                string v = ms.IsString ? ms.StringSamples[i].FirstOrDefault() : ms.NumericSamples[i][0].ToString(CultureInfo.InvariantCulture);
                session.Markers.Add(new MarkerEvent(ms.Timestamps[i], v));
            }
        }
        if (markerStreams.Count == 0) session.ImportNotes.Add("Uyarı: XDF içinde marker akışı bulunamadı.");

        session.NormalizeChannelOrder();
        return session;
    }

    // ------------------------------------------------------------------

    private static long ReadVarLen(BinaryReader br)
    {
        byte nb = br.ReadByte();
        return nb switch
        {
            1 => br.ReadByte(),
            4 => br.ReadUInt32(),
            8 => (long)br.ReadUInt64(),
            _ => throw new InvalidDataException($"Geçersiz uzunluk baytı: {nb}"),
        };
    }

    private static XElement ReadXml(BinaryReader br, long chunkEnd)
    {
        var bytes = br.ReadBytes((int)(chunkEnd - br.BaseStream.Position));
        var text = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        return XElement.Parse(text);
    }

    private static XdfStream ParseHeader(uint id, XElement info)
    {
        string V(string n) => info.Element(n)?.Value ?? "";
        var st = new XdfStream
        {
            Id = id,
            Name = V("name"),
            Type = V("type"),
            ChannelFormat = V("channel_format").Trim().ToLowerInvariant(),
            HostName = V("hostname"),
        };
        st.ChannelCount = int.TryParse(V("channel_count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cc) ? cc : 1;
        st.NominalSrate = double.TryParse(V("nominal_srate"), NumberStyles.Float, CultureInfo.InvariantCulture, out var sr) ? sr : 0;
        var chans = info.Element("desc")?.Element("channels")?.Elements("channel");
        if (chans != null)
            st.ChannelLabels = chans.Select(c => c.Element("label")?.Value ?? c.Element("name")?.Value ?? "").ToList();
        return st;
    }

    private static void ReadSamples(BinaryReader br, XdfStream st, long chunkEnd)
    {
        long count = ReadVarLen(br);
        double lastTs = st.Timestamps.Count > 0 ? st.Timestamps[^1] : 0;
        double dt = st.NominalSrate > 0 ? 1.0 / st.NominalSrate : 0;

        for (long k = 0; k < count && br.BaseStream.Position < chunkEnd; k++)
        {
            byte tsBytes = br.ReadByte();
            double ts = tsBytes == 8 ? br.ReadDouble() : lastTs + dt;
            lastTs = ts;
            st.Timestamps.Add(ts);

            if (st.IsString)
            {
                var vals = new string[st.ChannelCount];
                for (int c = 0; c < st.ChannelCount; c++)
                {
                    long len = ReadVarLen(br);
                    vals[c] = DecodeString(br.ReadBytes((int)len));
                }
                st.StringSamples.Add(vals);
            }
            else
            {
                var vals = new double[st.ChannelCount];
                for (int c = 0; c < st.ChannelCount; c++)
                {
                    vals[c] = st.ChannelFormat switch
                    {
                        "float32" => br.ReadSingle(),
                        "double64" => br.ReadDouble(),
                        "int8" => br.ReadSByte(),
                        "int16" => br.ReadInt16(),
                        "int32" => br.ReadInt32(),
                        "int64" => br.ReadInt64(),
                        _ => throw new InvalidDataException($"Desteklenmeyen kanal formatı: {st.ChannelFormat}"),
                    };
                }
                st.NumericSamples.Add(vals);
            }
        }
    }

    /// <summary>Önce UTF-8 dener; geçersizse Windows-1254 (Türkçe ANSI) ile çözer.</summary>
    public static string DecodeString(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(1254).GetString(bytes); }
    }

    /// <summary>
    /// Kayıt bilgisayarının saatine dönüştürme: ölçülen ofsetlere doğrusal model (a + b·t) uydurulur
    /// ve her zaman damgasına eklenir (pyxdf'in temel davranışı).
    /// </summary>
    private static void ApplyClockOffsets(XdfStream st)
    {
        if (st.ClockOffsets.Count == 0 || st.Timestamps.Count == 0) return;
        double a, b;
        if (st.ClockOffsets.Count == 1) { a = st.ClockOffsets[0].offset; b = 0; }
        else
        {
            double mx = st.ClockOffsets.Average(o => o.collection), my = st.ClockOffsets.Average(o => o.offset);
            double sxx = st.ClockOffsets.Sum(o => (o.collection - mx) * (o.collection - mx));
            double sxy = st.ClockOffsets.Sum(o => (o.collection - mx) * (o.offset - my));
            b = sxx > 0 ? sxy / sxx : 0;
            a = my - b * mx;
        }
        for (int i = 0; i < st.Timestamps.Count; i++)
            st.Timestamps[i] += a + b * st.Timestamps[i];
    }

    /// <summary>Düzenli akışlarda, büyük boşluklarla ayrılan her bölüm için zaman = a + b·indeks doğrusal uydurması.</summary>
    private static void Dejitter(XdfStream st)
    {
        var ts = st.Timestamps;
        if (ts.Count < 3) return;
        double maxGap = Math.Max(1.0, 10.0 / st.NominalSrate);
        int segStart = 0;
        for (int i = 1; i <= ts.Count; i++)
        {
            if (i == ts.Count || ts[i] - ts[i - 1] > maxGap || ts[i] < ts[i - 1])
            {
                FitSegment(ts, segStart, i);
                segStart = i;
            }
        }
    }

    private static void FitSegment(List<double> ts, int from, int to)
    {
        int n = to - from;
        if (n < 3) return;
        double mx = (n - 1) / 2.0, my = 0;
        for (int i = from; i < to; i++) my += ts[i];
        my /= n;
        double sxx = 0, sxy = 0;
        for (int i = 0; i < n; i++)
        {
            sxx += (i - mx) * (i - mx);
            sxy += (i - mx) * (ts[from + i] - my);
        }
        double b = sxy / sxx, a = my - b * mx;
        for (int i = 0; i < n; i++) ts[from + i] = a + b * i;
    }
}
