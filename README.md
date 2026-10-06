# XOnERPStudio

X.On EEG (7 kanal, 250 Hz) verilerini **LSLMarkerSender** markerları ve dikkat testi (görsel oddball) olaylarıyla milisaniye hassasiyetinde eşleştirip ERP (olay ilişkili potansiyel) analizi ve raporlaması yapan .NET 8 Windows Forms uygulaması.

## Çalıştırma

```bash
dotnet run
```

- Hedef çatı `net8.0-windows` (x64). `RollForward=Major` ayarlı olduğu için x64 .NET 8 Desktop Runtime yoksa yüklü .NET 9/10 ile de çalışır.
- `lsl.dll` (liblsl 1.17.7, x64) derlemeye gömülüdür. İlk açılışta `%LOCALAPPDATA%\XOnERPStudio\native\` altına çıkarılır.

## Tek dosya exe paketleme

```bash
dotnet publish -p:PublishProfile=SingleExe
```

Çıktı `publish\XOnERPStudio.exe` dosyasıdır (~69 MB, win-x64). .NET çalışma zamanı ve `lsl.dll` içine gömülüdür. Hedef bilgisayarda kurulum gerekmez, exe tek başına kopyalanıp çalıştırılabilir. Profil ayarları: [Properties/PublishProfiles/SingleExe.pubxml](Properties/PublishProfiles/SingleExe.pubxml).

## Veri kaynakları

| Kaynak | Açıklama |
|---|---|
| **Canlı LSL** | `XonStream` (EEG) + `MarkerStream` (LSLMarkerSender). Her iki inlet `proc_clocksync` ile alınır, yani tüm zamanlar yerel LSL saatindedir. Kayıt durdurulunca `Belgeler\XOnERPStudio\Kayitlar` altına otomatik CSV yedeği yazılır. |
| **XDF** | LabRecorder dosyaları. ClockOffset ile saat düzeltmesi ve düzenli akışlarda jitter giderme yapılır. Akışlar otomatik seçilir. |
| **CSV** | EEG CSV (zaman + F3…P4 [+AUX] [+marker]) ve ayrı marker CSV. Ayraç, ondalık ayırıcı, ms/s birimi ve göreli marker zamanı otomatik algılanır. |
| **Simülasyon** | Cihazsız test için sentetik veri: 1/f arka plan, alfa, göz kırpma, N100/P200/N200/P300. İsteğe bağlı olarak gerçek LSL akışları (`XonStream_Sim`, `MarkerStream_Sim`) şeklinde ağa yayınlanabilir. |

## Marker eşleştirme

LSLMarkerSender dikkat testi her denemede `color_onset_<Renk>` gönderir. Hedef denemelerde aynı anda `stimulus_start` da gelir. Bu yüzden:

- Epoch tetikleyicisi `color_onset_*` markerıdır. Hedef/standart ayrımı `target_color_<Renk>` ya da eşlik eden `stimulus_start` ile yapılır.
- Eşlik eden `stimulus_start` "eş marker" sayılır, böylece hedefler **iki kez sayılmaz**. `color_onset` yoksa (genel marker gönderimi) `stimulus_start` tek başına hedef tetikleyicisi olur.
- Tepkiler (`response_correct / response_incorrect / response_miss`) en yakın önceki uyarana atanır. Bunlardan Hit/Miss/FA/CR, RT ve d′ (log-doğrusal düzeltme) hesaplanır.
- Ayarlarda özel hedef/standart marker listeleri (ör. `S1`, `S2`) tanımlanabilir.

## Analiz hattı

1. Butterworth bant geçiren filtre (0,5–30 Hz) ve 50 Hz çentik, ileri-geri (sıfır faz)
2. Epoch −200…+800 ms, baseline −200…0 ms
3. Artefakt reddi: |x| > 75 µV veya tepe-tepe > 120 µV
4. Koşul ortalaması ± SEM, fark dalgası (Hedef − Standart)
5. N100 / N200 / P300 tepe tespiti, SNR
6. Welch PSD, Delta/Theta/Alpha/Beta göreli güç (tüm kayıt ve `rest_start…rest_end`)
7. Kanal kalitesi: 1–45 Hz RMS, şebeke gürültüsü oranı, kanal başına red oranı

X.on montajında Pz bulunmadığından **Pz\* = (P3 + P4) / 2** sanal parietal kanal olarak raporlanır.

## Çıktılar

- **HTML rapor:** tek dosya, gömülü SVG grafikler. Tarayıcıda *Yazdır → PDF olarak kaydet* ile PDF'e dönüştürülebilir. Örnek: [docs/ornek_rapor.html](docs/ornek_rapor.html)
- **CSV dökümü (SPSS/R/Excel):** bileşenler (uzun format), deneme sonuçları, davranış özeti, ortalama dalgalar, epoch günlüğü, bant gücü
- Grafiklerin PNG'leri, oturumun birleşik CSV'si ve JSON

## Doğrulama komutları

```bash
bin/Debug/net8.0-windows/XOnERPStudio.exe --selftest C:\temp\xon_test
```

Simülasyon → analiz → rapor/CSV → CSV'den geri okuma uçtan uca test edilir: epoch sayıları, P300 gecikme/genlik, topografi, filtrenin sıfır faz özelliği. Sonuç `selftest.txt` dosyasına yazılır.

```bash
bin/Debug/net8.0-windows/XOnERPStudio.exe --lsltest C:\temp\xon_test
```

Simülasyon gerçek LSL akışı olarak yayınlanır, ardından bulunur, 30 s kaydedilir ve marker–EEG zaman hizası ölçülür (< 2 ms).

```bash
bin/Debug/net8.0-windows/XOnERPStudio.exe --screens C:\temp\xon_screens
```

Simülasyon yüklenir ve her sekmenin ekran görüntüsü kaydedilir.

## Klasör yapısı

```text
LSL/               liblsl P/Invoke sarmalayıcısı, gömülü DLL yükleyici, canlı çift akış yöneticisi
Models/            oturum, marker (+ bağlamsal sınıflandırıcı), epoch, dalga, ayar ve sonuç modelleri
SignalProcessing/  biquad/FiltFilt filtreler, ERP motoru, FFT/Welch PSD, istatistik
IO/                XDF ve CSV okuyucular, dikkat testi deneme kurucusu, oturum dışa aktarıcı
Simulation/        sentetik X.on verisi ve LSL yayıncısı
Controls/          GDI+ grafikler: osiloskop, ERP, topografi, spektrum, kalite, tema
Reporting/         HTML+SVG rapor ve istatistik CSV dökümü
Forms/             ana ekran, canlı LSL ekranı, ayarlar
```

> Bu yazılım araştırma ve eğitim amaçlıdır; klinik tanı aracı değildir.
