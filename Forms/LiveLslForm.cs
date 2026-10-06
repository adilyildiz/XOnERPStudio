using XOnERPStudio.Controls;
using XOnERPStudio.IO;
using XOnERPStudio.Lsl;
using XOnERPStudio.Models;
using XOnERPStudio.SignalProcessing;
using XOnERPStudio.Simulation;

namespace XOnERPStudio.Forms;

/// <summary>
/// Canlı LSL: ağdaki XonStream (EEG) ve MarkerStream (LSLMarkerSender) akışlarını bulur, eşzamanlı izler
/// ve kaydeder. Kayıt durdurulduğunda güvenlik için otomatik CSV yedeği alınır ve oturum analize gönderilebilir.
/// </summary>
public class LiveLslForm : Form
{
    private readonly LiveLslManager _manager = new();
    private readonly SimulationLslPublisher _publisher = new();
    private readonly ErpFilterSettings _settings;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };

    private ComboBox _cmbEeg, _cmbMarker, _cmbWindow;
    private Button _btnDiscover, _btnConnect, _btnDisconnect, _btnSim, _btnPublish, _btnRecord, _btnSend;
    private CheckBox _chkFilter;
    private ContinuousEegViewer _viewer;
    private SignalQualityGauge _quality;
    private ListBox _markerLog;
    private Label _lblStatus, _lblStats, _lblRec;
    private TextBox _txtParticipant;
    private bool _lslAvailable;
    private int _qualityTick;

    /// <summary>Analize gönderilen kayıt (form DialogResult.OK ile kapanırsa).</summary>
    public EegRecordingSession RecordedSession { get; private set; }

    public LiveLslForm(ErpFilterSettings settings)
    {
        _settings = settings;
        Text = "Canlı LSL — EEG + Marker Eşzamanlı Kayıt";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1280, 820);
        MinimumSize = new Size(960, 600);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Icon = Theme.AppIcon;
        Font = Theme.UiFont(9f);

        BuildUi();
        Theme.Apply(this);

        _lslAvailable = NativeLibraryLoader.IsAvailable(out var ver);
        SetStatus(_lslAvailable
            ? $"liblsl {ver} hazır. 'Akışları Ara' ile XonStream ve MarkerStream akışlarını bulun."
            : $"LSL kütüphanesi yüklenemedi ({NativeLibraryLoader.LastError}). Yalnızca dahili simülasyon kullanılabilir.");
        _btnDiscover.Enabled = _btnPublish.Enabled = _lslAvailable;

        _manager.StatusChanged += s => BeginInvokeSafe(() => SetStatus(s));
        _manager.MarkerReceived += m => BeginInvokeSafe(() => LogMarker(m));
        _publisher.StatusChanged += s => BeginInvokeSafe(() => SetStatus(s));
        _timer.Tick += (_, _) => RefreshView();
        _timer.Start();
        UpdateButtons();
    }

    private void BuildUi()
    {
        // ---- Bağlantı çubuğu ----
        var top = Ui.Bar(DockStyle.Top, 44);
        _btnDiscover = Ui.Button("🔍 Akışları Ara", async (_, _) => await DiscoverAsync());
        _cmbEeg = Ui.Combo(300);
        _cmbMarker = Ui.Combo(260);
        _btnConnect = Ui.Button("Bağlan", (_, _) => Connect(), Theme.Accent);
        _btnDisconnect = Ui.Button("Bağlantıyı Kes", (_, _) => Disconnect());
        _btnSim = Ui.Button("🧪 Dahili Simülasyon", (_, _) => StartSimulation());
        _btnPublish = Ui.Button("📡 Simülasyonu LSL'e Yayınla", (_, _) => TogglePublish());
        top.Controls.AddRange(new Control[] { _btnDiscover, Ui.Label("EEG:"), _cmbEeg, Ui.Label("Marker:"), _cmbMarker, _btnConnect, _btnDisconnect, _btnSim, _btnPublish });

        // ---- Görünüm çubuğu ----
        var viewBar = Ui.Bar(DockStyle.Top, 38);
        viewBar.BackColor = Theme.Background;
        _cmbWindow = Ui.Combo(80, "2 s", "5 s", "10 s", "30 s");
        _cmbWindow.SelectedIndex = 2;
        _cmbWindow.SelectedIndexChanged += (_, _) => _viewer.WindowSeconds = ContinuousEegViewer.WindowOptions[_cmbWindow.SelectedIndex];
        var cmbScale = Ui.Combo(90, "±10 µV", "±20 µV", "±50 µV", "±100 µV", "±200 µV", "±500 µV");
        cmbScale.SelectedIndex = 2;
        cmbScale.SelectedIndexChanged += (_, _) => _viewer.ScaleUv = ContinuousEegViewer.ScaleOptions[cmbScale.SelectedIndex];
        _chkFilter = Ui.Check($"Görüntü filtresi ({_settings.HighPassHz:0.#}–{_settings.LowPassHz:0} Hz + çentik)", true, null);
        _lblStats = Ui.Label("", true);
        viewBar.Controls.AddRange(new Control[] { Ui.Label("Pencere:"), _cmbWindow, Ui.Label("Ölçek:"), cmbScale, _chkFilter, _lblStats });

        // ---- Kayıt çubuğu ----
        var bottom = Ui.Bar(DockStyle.Bottom, 48);
        bottom.Padding = new Padding(8, 8, 8, 0);
        _txtParticipant = new TextBox { Width = 140, PlaceholderText = "Katılımcı ID", Margin = new Padding(0, 3, 10, 0) };
        _btnRecord = Ui.Button("⏺ Kaydı Başlat", (_, _) => ToggleRecord(), Color.FromArgb(150, 40, 40), 150);
        _lblRec = Ui.Label("Kayıt yok", true, 10f, FontStyle.Bold);
        _btnSend = Ui.Button("✔ Kaydı Analize Gönder", (_, _) => SendToAnalysis(), Theme.Accent, 190);
        bottom.Controls.AddRange(new Control[] { Ui.Label("Katılımcı:"), _txtParticipant, _btnRecord, _lblRec, _btnSend });

        // ---- Durum ----
        _lblStatus = new Label { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(10, 4, 0, 0), ForeColor = Theme.TextMuted, BackColor = Theme.Surface };

        // ---- Orta: osiloskop + sağ panel ----
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = Theme.Border,
            FixedPanel = FixedPanel.Panel2,
        };
        _viewer = new ContinuousEegViewer { Dock = DockStyle.Fill, FollowLatest = true, WindowSeconds = 10 };
        split.Panel1.Controls.Add(_viewer);

        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Theme.Border };
        _markerLog = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9f),
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 18,
        };
        _markerLog.DrawItem += DrawMarkerItem;
        var logHeader = Ui.Label("  Gelen Markerlar", false, 9.5f, FontStyle.Bold);
        logHeader.Dock = DockStyle.Top;
        logHeader.AutoSize = false;
        logHeader.Height = 26;
        logHeader.BackColor = Theme.Surface;
        right.Panel1.Controls.Add(_markerLog);
        right.Panel1.Controls.Add(logHeader);
        _quality = new SignalQualityGauge { Dock = DockStyle.Fill };
        right.Panel2.Controls.Add(_quality);
        split.Panel2.Controls.Add(right);

        Controls.Add(split);
        Controls.Add(viewBar);
        Controls.Add(top);
        Controls.Add(_lblStatus);
        Controls.Add(bottom);

        Load += (_, _) =>
        {
            split.SplitterDistance = Math.Max(300, split.Width - 380);
            right.SplitterDistance = (int)(right.Height * 0.55);
        };
    }

    // ------------------------------------------------------------------ bağlantı

    private async Task DiscoverAsync()
    {
        _btnDiscover.Enabled = false;
        SetStatus("Ağda LSL akışları aranıyor...");
        try
        {
            var list = await Task.Run(() => LiveLslManager.Discover(2.0));
            _cmbEeg.Items.Clear();
            _cmbMarker.Items.Clear();
            _cmbMarker.Items.Add("(marker akışı yok)");
            foreach (var s in list)
            {
                if (s.LooksLikeMarkers) _cmbMarker.Items.Add(s);
                else _cmbEeg.Items.Add(s);
            }
            var eeg = _cmbEeg.Items.Cast<LslStreamSummary>().OrderByDescending(s => s.Name.StartsWith(EegConstants.DefaultEegStreamName, StringComparison.OrdinalIgnoreCase))
                                                            .ThenByDescending(s => s.LooksLikeEeg).FirstOrDefault();
            if (eeg != null) _cmbEeg.SelectedItem = eeg;
            var mk = _cmbMarker.Items.OfType<LslStreamSummary>().OrderByDescending(s => s.Name.StartsWith(EegConstants.DefaultMarkerStreamName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            _cmbMarker.SelectedItem = (object)mk ?? _cmbMarker.Items[0];
            SetStatus(list.Count == 0
                ? "Akış bulunamadı. X.On LSL köprüsünün ve LSLMarkerSender'ın çalıştığından, aynı ağda olduğunuzdan emin olun."
                : $"{list.Count} akış bulundu.");
        }
        catch (Exception ex) { SetStatus("Arama hatası: " + ex.Message); }
        finally { _btnDiscover.Enabled = true; UpdateButtons(); }
    }

    private void Connect()
    {
        if (_cmbEeg.SelectedItem is not LslStreamSummary eeg)
        {
            MessageBox.Show(this, "Önce bir EEG akışı seçin.", "Canlı LSL", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            Cursor = Cursors.WaitCursor;
            _manager.ConnectLsl(eeg, _cmbMarker.SelectedItem as LslStreamSummary);
            ResetView();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Bağlantı hatası", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { Cursor = Cursors.Default; UpdateButtons(); }
    }

    private void StartSimulation()
    {
        _manager.ConnectSimulation(new SimulationOptions { Trials = 400 });
        ResetView();
        UpdateButtons();
    }

    private void TogglePublish()
    {
        if (_publisher.IsRunning) { _publisher.Stop(); SetStatus("LSL yayını durduruldu."); }
        else
        {
            try { _publisher.Start(new SimulationOptions { Trials = 400 }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Yayın hatası", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        UpdateButtons();
    }

    private void Disconnect()
    {
        if (_manager.IsRecording && !ConfirmStopRecording()) return;
        _manager.Stop();
        SetStatus("Bağlantı kesildi.");
        UpdateButtons();
    }

    private void ResetView()
    {
        _markerLog.Items.Clear();
        _viewer.Clear();
    }

    // ------------------------------------------------------------------ kayıt

    private void ToggleRecord()
    {
        if (!_manager.IsRecording)
        {
            RecordedSession = null;
            _manager.StartRecording();
        }
        else
        {
            var s = _manager.StopRecording();
            if (s != null)
            {
                s.ParticipantId = _txtParticipant.Text.Trim();
                RecordedSession = s;
                AutoBackup(s);
            }
        }
        UpdateButtons();
    }

    private void AutoBackup(EegRecordingSession s)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XOnERPStudio", "Kayitlar");
            Directory.CreateDirectory(dir);
            string id = string.IsNullOrWhiteSpace(s.ParticipantId) ? "kayit" : string.Join("_", s.ParticipantId.Split(Path.GetInvalidFileNameChars()));
            string path = Path.Combine(dir, $"{id}_{s.RecordedAt:yyyyMMdd_HHmmss}.csv");
            SessionExporter.ExportCombinedCsv(s, path);
            SetStatus($"Kayıt durduruldu ({s.DurationSeconds:0.0} s, {s.Markers.Count} marker). Yedek: {path}");
        }
        catch (Exception ex) { SetStatus("Yedekleme hatası: " + ex.Message); }
    }

    private void SendToAnalysis()
    {
        if (_manager.IsRecording) ToggleRecord();
        if (RecordedSession == null || RecordedSession.SampleCount == 0)
        {
            MessageBox.Show(this, "Analize gönderilecek kayıt yok. Önce kayıt başlatıp durdurun.", "Canlı LSL", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        RecordedSession.ParticipantId = _txtParticipant.Text.Trim();
        DialogResult = DialogResult.OK;
        Close();
    }

    private bool ConfirmStopRecording()
    {
        var r = MessageBox.Show(this, "Kayıt devam ediyor. Kaydı durdurup kaydetmek ister misiniz?", "Kayıt",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (r == DialogResult.Cancel) return false;
        if (r == DialogResult.Yes) ToggleRecord();
        else _manager.StopRecording();
        return true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK && _manager.IsRecording && !ConfirmStopRecording()) { e.Cancel = true; return; }
        if (DialogResult != DialogResult.OK && RecordedSession != null)
        {
            var r = MessageBox.Show(this, "Kaydedilen oturumu analize göndermek ister misiniz?", "Kayıt",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes) { RecordedSession.ParticipantId = _txtParticipant.Text.Trim(); DialogResult = DialogResult.OK; }
        }
        _timer.Stop();
        _manager.Dispose();
        _publisher.Dispose();
        base.OnFormClosing(e);
    }

    // ------------------------------------------------------------------ görüntüleme

    private void RefreshView()
    {
        if (!_manager.IsRunning) return;
        double win = _viewer.WindowSeconds;
        bool filter = _chkFilter.Checked;
        // Filtre geçici rejimini görünür alanın dışında tutmak için fazladan veri al
        var (ts, data, markers) = _manager.Snapshot(filter ? win + 6 : win);
        if (ts.Length < 2) return;

        double fs = _manager.SampleRate;
        double[][] display = data;
        if (filter && ts.Length > fs)
            display = DigitalFilter.FilterChannels(data, _settings, fs);
        else
            display = data.Select(d => { var c = (double[])d.Clone(); DigitalFilter.RemoveMean(c); return c; }).ToArray();

        MarkerClassifier.Classify(markers, _settings);
        _viewer.TimeOrigin = ts[0] - (ts[0] % 1);
        _viewer.SetData(ts, display, _manager.ChannelNames, markers);

        if (++_qualityTick % 10 == 0)
        {
            int n = Math.Min(ts.Length, (int)(fs * 2));
            int eeg = Math.Min(EegConstants.ChannelNames.Length, display.Length);
            var rms = Enumerable.Range(0, eeg).Select(c => Statistics.Rms(display[c], ts.Length - n, n)).ToList();
            _quality.SetLiveRms(_manager.ChannelNames.Take(eeg).ToList(), rms);
        }

        _lblStats.Text = $"{_manager.EegStreamName} · ölçülen {_manager.MeasuredRate:0.0} Hz · {_manager.SamplesReceived:N0} örnek · {_manager.MarkersReceived} marker";
        if (_manager.IsRecording)
            _lblRec.Text = $"● KAYIT  {TimeSpan.FromSeconds(_manager.RecordedSeconds):mm\\:ss}";
    }

    private void LogMarker(MarkerEvent m)
    {
        var list = new List<MarkerEvent> { m };
        MarkerClassifier.Classify(list, _settings);
        _markerLog.Items.Insert(0, m);
        if (_markerLog.Items.Count > 500) _markerLog.Items.RemoveAt(_markerLog.Items.Count - 1);
    }

    private void DrawMarkerItem(object sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0) return;
        var m = (MarkerEvent)_markerLog.Items[e.Index];
        var color = MarkerEvent.DisplayColor(m.Category);
        using var b = new SolidBrush(Theme.WithAlpha(color, 255));
        using var mb = new SolidBrush(Theme.TextMuted);
        e.Graphics.FillRectangle(b, e.Bounds.X + 4, e.Bounds.Y + 5, 8, 8);
        e.Graphics.DrawString($"{m.Timestamp:F3}", e.Font, mb, e.Bounds.X + 18, e.Bounds.Y + 1);
        e.Graphics.DrawString(m.Value, e.Font, b, e.Bounds.X + 110, e.Bounds.Y + 1);
    }

    private void UpdateButtons()
    {
        bool running = _manager.IsRunning;
        _btnConnect.Enabled = _lslAvailable && _cmbEeg.Items.Count > 0;
        _btnDisconnect.Enabled = running;
        _btnRecord.Enabled = running;
        _btnRecord.Text = _manager.IsRecording ? "⏹ Kaydı Durdur" : "⏺ Kaydı Başlat";
        _btnSend.Enabled = RecordedSession != null || _manager.IsRecording;
        _btnPublish.Text = _publisher.IsRunning ? "⏹ LSL Yayınını Durdur" : "📡 Simülasyonu LSL'e Yayınla";
        if (!_manager.IsRecording)
            _lblRec.Text = RecordedSession != null ? $"Kayıt hazır: {RecordedSession.DurationSeconds:0.0} s, {RecordedSession.Markers.Count} marker" : "Kayıt yok";
    }

    private void SetStatus(string s) => _lblStatus.Text = s;

    private void BeginInvokeSafe(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(a); } catch (ObjectDisposedException) { }
    }
}
