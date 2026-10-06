using System.ComponentModel;
using Newtonsoft.Json;

namespace XOnERPStudio.Models;

/// <summary>Filtre, epoch, artefakt ve tepe tespiti parametreleri (PropertyGrid ile düzenlenir).</summary>
public class ErpFilterSettings
{
    private const string CatFilter = "1. Filtreleme";
    private const string CatEpoch = "2. Epoch ve Baseline";
    private const string CatArtifact = "3. Artefakt Reddi";
    private const string CatPeaks = "4. Tepe Tespiti";
    private const string CatMarkers = "5. Marker Eşleştirme";
    private const string CatDisplay = "6. Görünüm";

    // ---------- Filtreleme ----------
    [Category(CatFilter), DisplayName("Bant geçiren filtre"), Description("Butterworth bant geçiren filtre (ileri-geri, sıfır faz).")]
    public bool BandpassEnabled { get; set; } = true;

    [Category(CatFilter), DisplayName("Alt kesim (Hz)"), Description("Yüksek geçiren kesim frekansı. ERP için tipik 0.1–1 Hz.")]
    public double HighPassHz { get; set; } = 0.5;

    [Category(CatFilter), DisplayName("Üst kesim (Hz)"), Description("Alçak geçiren kesim frekansı. ERP için tipik 20–40 Hz.")]
    public double LowPassHz { get; set; } = 30.0;

    [Category(CatFilter), DisplayName("Filtre derecesi"), Description("Her kenar için Butterworth derecesi (2 veya 4). FiltFilt uygulandığı için etkin derece iki katıdır.")]
    public int FilterOrder { get; set; } = 2;

    [Category(CatFilter), DisplayName("Çentik filtresi"), Description("Şebeke gürültüsü için IIR çentik filtresi.")]
    public bool NotchEnabled { get; set; } = true;

    [Category(CatFilter), DisplayName("Çentik frekansı (Hz)"), Description("Türkiye/Avrupa şebekesi 50 Hz.")]
    public double NotchHz { get; set; } = 50.0;

    [Category(CatFilter), DisplayName("Çentik Q faktörü"), Description("Q = f0 / bant genişliği. 25 → 2 Hz genişlik.")]
    public double NotchQ { get; set; } = 25.0;

    // ---------- Epoch ----------
    [Category(CatEpoch), DisplayName("Epoch başlangıcı (ms)")]
    public double EpochStartMs { get; set; } = -200;

    [Category(CatEpoch), DisplayName("Epoch sonu (ms)")]
    public double EpochEndMs { get; set; } = 800;

    [Category(CatEpoch), DisplayName("Baseline başlangıcı (ms)")]
    public double BaselineStartMs { get; set; } = -200;

    [Category(CatEpoch), DisplayName("Baseline sonu (ms)")]
    public double BaselineEndMs { get; set; } = 0;

    // ---------- Artefakt ----------
    [Category(CatArtifact), DisplayName("Artefakt reddi"), Description("Eşiği aşan epochlar ortalamaya katılmaz.")]
    public bool ArtifactRejectionEnabled { get; set; } = true;

    [Category(CatArtifact), DisplayName("Mutlak eşik (µV)"), Description("Herhangi bir kanalda |x| bu değeri aşarsa epoch reddedilir.")]
    public double ArtifactThresholdUv { get; set; } = 75;

    [Category(CatArtifact), DisplayName("Tepe-tepe eşiği (µV)"), Description("Herhangi bir kanalda max−min bu değeri aşarsa epoch reddedilir.")]
    public double PeakToPeakThresholdUv { get; set; } = 120;

    // ---------- Tepe ----------
    [Category(CatPeaks), DisplayName("P300 penceresi başı (ms)")]
    public double P300StartMs { get; set; } = 250;

    [Category(CatPeaks), DisplayName("P300 penceresi sonu (ms)")]
    public double P300EndMs { get; set; } = 550;

    [Category(CatPeaks), DisplayName("N200 penceresi başı (ms)")]
    public double N200StartMs { get; set; } = 180;

    [Category(CatPeaks), DisplayName("N200 penceresi sonu (ms)")]
    public double N200EndMs { get; set; } = 280;

    [Category(CatPeaks), DisplayName("N100 penceresi başı (ms)")]
    public double N100StartMs { get; set; } = 80;

    [Category(CatPeaks), DisplayName("N100 penceresi sonu (ms)")]
    public double N100EndMs { get; set; } = 150;

    // ---------- Marker ----------
    [Category(CatMarkers), DisplayName("Yanıt penceresi (ms)"), Description("Uyarandan sonra tepkinin aranacağı süre (bir sonraki uyarana kadar sınırlıdır).")]
    public double ResponseWindowMs { get; set; } = 1500;

    [Category(CatMarkers), DisplayName("Eş marker toleransı (ms)"), Description("color_onset ve stimulus_start bu süre içinde gelirse tek uyaran sayılır.")]
    public double DuplicateToleranceMs { get; set; } = 100;

    [Category(CatMarkers), DisplayName("Hedef renk (zorla)"), Description("Boş bırakılırsa target_color_<Renk> markerından otomatik okunur.")]
    public string TargetColorOverride { get; set; } = "";

    [Category(CatMarkers), DisplayName("Özel hedef markerlar"), Description("Virgülle ayrılmış; bu değerlere sahip markerlar Hedef koşulu olarak epochlanır (ör. S1,Marker_1).")]
    public string CustomTargetMarkers { get; set; } = "";

    [Category(CatMarkers), DisplayName("Özel standart markerlar"), Description("Virgülle ayrılmış; bu değerlere sahip markerlar Standart koşulu olarak epochlanır (ör. S2,Marker_2).")]
    public string CustomStandardMarkers { get; set; } = "";

    // ---------- Görünüm ----------
    [Category(CatDisplay), DisplayName("Negatif yukarı (EEG geleneği)"), Description("ERP grafiklerinde Y eksenini ters çevirir.")]
    public bool NegativeUp { get; set; } = false;

    // ---------- Yardımcılar ----------
    [Browsable(false), JsonIgnore]
    public IEnumerable<string> CustomTargetList => SplitList(CustomTargetMarkers);

    [Browsable(false), JsonIgnore]
    public IEnumerable<string> CustomStandardList => SplitList(CustomStandardMarkers);

    private static IEnumerable<string> SplitList(string s) =>
        (s ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Ayarları kontrol eder; tutarsızlık varsa açıklama döner.</summary>
    public string Validate(double sampleRate)
    {
        if (BandpassEnabled)
        {
            if (HighPassHz <= 0 || LowPassHz <= HighPassHz) return "Filtre kesim frekansları geçersiz (0 < alt < üst olmalı).";
            if (LowPassHz >= sampleRate / 2) return $"Üst kesim Nyquist frekansından ({sampleRate / 2:0} Hz) küçük olmalı.";
        }
        if (FilterOrder != 2 && FilterOrder != 4) return "Filtre derecesi 2 veya 4 olmalı.";
        if (NotchEnabled && (NotchHz <= 0 || NotchHz >= sampleRate / 2 || NotchQ <= 0)) return "Çentik ayarları geçersiz.";
        if (EpochStartMs >= 0 || EpochEndMs <= 0) return "Epoch uyaran öncesi (<0) ve sonrası (>0) kısımları içermeli.";
        if (BaselineStartMs < EpochStartMs || BaselineEndMs > EpochEndMs || BaselineEndMs <= BaselineStartMs)
            return "Baseline aralığı epoch içinde olmalı.";
        return null;
    }

    public ErpFilterSettings Clone() =>
        JsonConvert.DeserializeObject<ErpFilterSettings>(JsonConvert.SerializeObject(this));

    // ---------- Kalıcılık ----------
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XOnERPStudio", "erp_settings.json");

    public static ErpFilterSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonConvert.DeserializeObject<ErpFilterSettings>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch { /* bozuk dosya → varsayılanlar */ }
        return new ErpFilterSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
    }
}
