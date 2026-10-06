using System.Diagnostics;
using XOnERPStudio.Controls;
using XOnERPStudio.IO;
using XOnERPStudio.Lsl;
using XOnERPStudio.Models;
using XOnERPStudio.Reporting;
using XOnERPStudio.Simulation;

namespace XOnERPStudio.Forms;

/// <summary>Ana ekran: veri yükleme, analiz, sekmeli görselleştirme ve raporlama.</summary>
public class MainForm : Form
{
    private EegRecordingSession _session;
    private ErpAnalysisResult _result;
    private ErpFilterSettings _settings = ErpFilterSettings.Load();
    private bool _analyzing;
    private bool _fillingPeakGrid;

    // Kabuk
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly FlowLayoutPanel _nav = new();
    private readonly List<(Button btn, Control page)> _pages = new();
    private ToolStripStatusLabel _status, _lslStatus;
    private SummaryCard _cardSession, _cardEpochs, _cardP300, _cardBehavior, _cardRt, _cardQuality;

    // Sinyal
    private ContinuousEegViewer _eegViewer;
    private HScrollBar _eegScroll;
    private CheckBox _chkFiltered;
    private DataGridView _markerGrid;

    // ERP
    private ComboBox _cmbChannel;
    private ErpWaveformViewer _erpViewer;
    private DataGridView _peakGrid;

    // Topografi / spektrum
    private Topographic7ChannelView _topo;
    private FrequencySpectrumViewer _spectrum;
    private SignalQualityGauge _qualityGauge;
    private ComboBox _cmbSpectrum;

    // Davranış / rapor
    private DataGridView _trialGrid, _epochGrid;
    private Label _behaviorSummary;
    private TextBox _txtId, _txtName, _txtAge, _txtNotes, _txtLog;

    public MainForm()
    {
        Text = "XOnERPStudio — X.On EEG & LSL Marker ERP Analiz ve Raporlama";
        Size = new Size(1440, 900);
        MinimumSize = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont(9f);
        Icon = Theme.AppIcon;
        KeyPreview = true;
        AllowDrop = true;

        BuildShell();
        Theme.Apply(this);
        UpdateAll();

        Shown += (_, _) =>
        {
            bool ok = NativeLibraryLoader.IsAvailable(out var ver);
            _lslStatus.Text = ok ? $"liblsl {ver}" : "LSL yok";
            _lslStatus.ForeColor = ok ? Theme.Good : Theme.Poor;
        };
        DragEnter += (_, e) => e.Effect = e.Data!.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) =>
        {
            if (e.Data!.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) _ = OpenFileAsync(files[0]);
        };
    }

    // ================================================================== kabuk

    private void BuildShell()
    {
        var menu = new MenuStrip { Renderer = new DarkToolStripRenderer(), BackColor = Theme.Surface, ForeColor = Theme.Text };
        var mFile = new ToolStripMenuItem("&Dosya");
        ((ToolStripMenuItem)mFile.DropDownItems.Add("XDF / CSV Aç...", null, async (_, _) => await OpenFileDialogAsync())).ShortcutKeys = Keys.Control | Keys.O;
        mFile.DropDownItems.Add("Marker CSV Ekle...", null, (_, _) => AddMarkerCsv());
        mFile.DropDownItems.Add("Simülasyon Oturumu Oluştur", null, async (_, _) => await CreateSimulationAsync());
        mFile.DropDownItems.Add(new ToolStripSeparator());
        mFile.DropDownItems.Add("Oturumu Birleşik CSV Olarak Kaydet...", null, (_, _) => ExportCombinedCsv());
        mFile.DropDownItems.Add("Oturum + Analiz JSON Kaydet...", null, (_, _) => ExportJson());
        mFile.DropDownItems.Add(new ToolStripSeparator());
        mFile.DropDownItems.Add("Çıkış", null, (_, _) => Close());
        var mAnalysis = new ToolStripMenuItem("&Analiz");
        ((ToolStripMenuItem)mAnalysis.DropDownItems.Add("Analizi Çalıştır", null, async (_, _) => await RunAnalysisAsync())).ShortcutKeys = Keys.F5;
        mAnalysis.DropDownItems.Add("Filtre ve Epoch Ayarları...", null, async (_, _) => await EditSettingsAsync());
        var mLive = new ToolStripMenuItem("&Canlı");
        ((ToolStripMenuItem)mLive.DropDownItems.Add("LSL Canlı İzleme ve Kayıt...", null, async (_, _) => await OpenLiveAsync())).ShortcutKeys = Keys.Control | Keys.L;
        var mReport = new ToolStripMenuItem("&Rapor");
        ((ToolStripMenuItem)mReport.DropDownItems.Add("HTML Rapor Oluştur ve Aç", null, (_, _) => GenerateReport())).ShortcutKeys = Keys.Control | Keys.R;
        mReport.DropDownItems.Add("İstatistik CSV Dökümü...", null, (_, _) => ExportStatsCsv());
        mReport.DropDownItems.Add("Grafikleri PNG Olarak Kaydet...", null, (_, _) => ExportPngs());
        mReport.DropDownItems.Add("Rapor Klasörünü Aç", null, (_, _) => OpenFolder(ErpReportGenerator.DefaultReportFolder));
        menu.Items.AddRange(new ToolStripItem[] { mFile, mAnalysis, mLive, mReport });

        // Araç çubuğu
        var tools = Ui.Bar(DockStyle.Top, 44);
        tools.Padding = new Padding(10, 7, 10, 0);
        tools.Controls.Add(Ui.Button("📂 Dosya Aç", async (_, _) => await OpenFileDialogAsync()));
        tools.Controls.Add(Ui.Button("➕ Marker CSV", (_, _) => AddMarkerCsv()));
        tools.Controls.Add(Ui.Button("🧪 Simülasyon", async (_, _) => await CreateSimulationAsync()));
        tools.Controls.Add(Ui.Button("📡 Canlı LSL", async (_, _) => await OpenLiveAsync(), Color.FromArgb(38, 80, 70)));
        tools.Controls.Add(Ui.Button("⚙ Ayarlar", async (_, _) => await EditSettingsAsync()));
        tools.Controls.Add(Ui.Button("▶ Analiz Et", async (_, _) => await RunAnalysisAsync(), Theme.Accent));
        tools.Controls.Add(Ui.Button("📝 HTML Rapor", (_, _) => GenerateReport(), Color.FromArgb(90, 60, 130)));
        tools.Controls.Add(Ui.Button("📊 CSV Dökümü", (_, _) => ExportStatsCsv()));

        // Özet kartları
        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 82, Padding = new Padding(10, 8, 10, 6), BackColor = Theme.Background, WrapContents = false };
        _cardSession = new SummaryCard("Oturum") { Width = 260 };
        _cardEpochs = new SummaryCard("Epoch (kabul / toplam)") { Width = 190 };
        _cardP300 = new SummaryCard("P300 (Pz* hedef)") { Width = 190, AccentColor = Theme.PeakColor };
        _cardBehavior = new SummaryCard("Doğruluk · d′") { Width = 190, AccentColor = Theme.Good };
        _cardRt = new SummaryCard("Tepki süresi") { Width = 170, AccentColor = Theme.StandardColor };
        _cardQuality = new SummaryCard("Sinyal kalitesi") { Width = 190, AccentColor = Theme.Good };
        cards.Controls.AddRange(new Control[] { _cardSession, _cardEpochs, _cardP300, _cardBehavior, _cardRt, _cardQuality });

        // Sekme gezinme
        _nav.Dock = DockStyle.Top;
        _nav.Height = 38;
        _nav.Padding = new Padding(10, 4, 10, 0);
        _nav.BackColor = Theme.Background;
        _nav.WrapContents = false;

        AddPage("〰 Sinyal", BuildSignalPage());
        AddPage("📈 ERP", BuildErpPage());
        AddPage("🧠 Topografi", BuildTopoPage());
        AddPage("🌈 Spektrum & Kalite", BuildSpectrumPage());
        AddPage("🎯 Davranış", BuildBehaviorPage());
        AddPage("📄 Rapor", BuildReportPage());
        SelectPage(0);

        var status = new StatusStrip { Renderer = new DarkToolStripRenderer(), BackColor = Theme.Surface };
        _status = new ToolStripStatusLabel("Hazır. Bir XDF/CSV dosyası açın (sürükle-bırak da olur), simülasyon oluşturun veya canlı LSL'e bağlanın.")
            { Spring = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Text };
        _lslStatus = new ToolStripStatusLabel("LSL…") { ForeColor = Theme.TextMuted };
        status.Items.AddRange(new ToolStripItem[] { _status, _lslStatus });

        Controls.Add(_content);
        Controls.Add(_nav);
        Controls.Add(cards);
        Controls.Add(tools);
        Controls.Add(menu);
        Controls.Add(status);
        MainMenuStrip = menu;
    }

    private void AddPage(string title, Control page)
    {
        int index = _pages.Count;
        var btn = Ui.Button(title, (_, _) => SelectPage(index));
        btn.Height = 32;
        btn.Margin = new Padding(0, 0, 4, 0);
        btn.FlatAppearance.BorderSize = 0;
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _content.Controls.Add(page);
        _nav.Controls.Add(btn);
        _pages.Add((btn, page));
    }

    private void SelectPage(int index)
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            bool sel = i == index;
            _pages[i].page.Visible = sel;
            _pages[i].btn.BackColor = sel ? Theme.Accent : Theme.Surface;
            _pages[i].btn.Font = Theme.UiFont(9.5f, sel ? FontStyle.Bold : FontStyle.Regular);
        }
    }

    // ================================================================== sayfalar

    private Control BuildSignalPage()
    {
        var page = new Panel();
        var bar = Ui.Bar(DockStyle.Top, 38);
        var cmbWin = Ui.Combo(80, "2 s", "5 s", "10 s", "30 s", "60 s");
        cmbWin.SelectedIndex = 2;
        cmbWin.SelectedIndexChanged += (_, _) => _eegViewer.WindowSeconds = ContinuousEegViewer.WindowOptions[cmbWin.SelectedIndex];
        var cmbScale = Ui.Combo(90, "±10 µV", "±20 µV", "±50 µV", "±100 µV", "±200 µV", "±500 µV");
        cmbScale.SelectedIndex = 2;
        cmbScale.SelectedIndexChanged += (_, _) => _eegViewer.ScaleUv = ContinuousEegViewer.ScaleOptions[cmbScale.SelectedIndex];
        _chkFiltered = Ui.Check("Filtrelenmiş sinyal", true, (_, _) => RefreshSignal(false));
        var chkLabels = Ui.Check("Marker etiketleri", true, (s, _) => { _eegViewer.ShowMarkerLabels = ((CheckBox)s!).Checked; _eegViewer.Invalidate(); });
        bar.Controls.AddRange(new Control[] { Ui.Label("Pencere:"), cmbWin, Ui.Label("Ölçek:"), cmbScale, _chkFiltered, chkLabels,
            Ui.Button("PNG", (_, _) => SavePng(_eegViewer, "sinyal")) });

        _eegViewer = new ContinuousEegViewer { Dock = DockStyle.Fill };
        _eegScroll = new HScrollBar { Dock = DockStyle.Bottom, Height = 16 };
        _eegScroll.Scroll += (_, _) => _eegViewer.ViewStart = _eegViewer.DataStart + _eegScroll.Value / 10.0;
        _eegViewer.ViewChanged += (_, _) => SyncScroll();

        _markerGrid = Ui.Grid();
        _markerGrid.Columns.Add("t", "Zaman (s)");
        _markerGrid.Columns.Add("v", "Marker");
        _markerGrid.Columns.Add("c", "Kategori");
        _markerGrid.Columns[0].FillWeight = 50;
        _markerGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _markerGrid.Rows[e.RowIndex].Tag is MarkerEvent m)
                _eegViewer.ViewStart = m.Timestamp - _eegViewer.WindowSeconds / 3;
        };
        _markerGrid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex == 2 && _markerGrid.Rows[e.RowIndex].Tag is MarkerEvent m)
                e.CellStyle!.ForeColor = MarkerEvent.DisplayColor(m.Category);
        };

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_eegViewer);
        left.Controls.Add(_eegScroll);
        var side = new Panel { Width = 380 };
        side.Controls.Add(_markerGrid);
        side.Controls.Add(Header("Markerlar (çift tık: zamana git)"));
        page.Controls.Add(Split(left, side, DockStyle.Right));
        page.Controls.Add(bar);
        return page;
    }

    private Control BuildErpPage()
    {
        var page = new Panel();
        var bar = Ui.Bar(DockStyle.Top, 38);
        _cmbChannel = Ui.Combo(90, EegConstants.ChannelNames.Append(EegConstants.VirtualParietalName).Cast<object>().ToArray());
        _cmbChannel.SelectedItem = EegConstants.VirtualParietalName;
        _cmbChannel.SelectedIndexChanged += (_, _) => RefreshErp();
        _erpViewer = new ErpWaveformViewer { Dock = DockStyle.Fill };
        CheckBox Toggle(string text, Func<bool> get, Action<bool> set) =>
            Ui.Check(text, get(), (s, _) => { set(((CheckBox)s!).Checked); _erpViewer.Invalidate(); });
        bar.Controls.AddRange(new Control[]
        {
            Ui.Label("Kanal:"), _cmbChannel,
            Toggle("Hedef", () => _erpViewer.ShowTarget, v => _erpViewer.ShowTarget = v),
            Toggle("Standart", () => _erpViewer.ShowStandard, v => _erpViewer.ShowStandard = v),
            Toggle("Fark", () => _erpViewer.ShowDifference, v => _erpViewer.ShowDifference = v),
            Toggle("±SEM", () => _erpViewer.ShowSem, v => _erpViewer.ShowSem = v),
            Toggle("Tepeler", () => _erpViewer.ShowPeaks, v => _erpViewer.ShowPeaks = v),
            Ui.Check("Negatif yukarı", _settings.NegativeUp, (s, _) => { _settings.NegativeUp = ((CheckBox)s!).Checked; _erpViewer.NegativeUp = _settings.NegativeUp; _erpViewer.Invalidate(); _topo.Invalidate(); }),
            Ui.Button("PNG", (_, _) => SavePng(_erpViewer, $"erp_{_cmbChannel.SelectedItem}")),
        });

        _peakGrid = Ui.Grid();
        foreach (var (n, h) in new[] { ("ch", "Kanal"), ("p3l", "P300 ms"), ("p3a", "P300 µV"), ("n2l", "N200 ms"), ("n2a", "N200 µV"),
                     ("n1l", "N100 ms"), ("n1a", "N100 µV"), ("sp3", "Std P300 µV"), ("dp3", "Fark P300 µV"), ("dl", "Fark P300 ms"), ("snr", "SNR") })
            _peakGrid.Columns.Add(n, h);
        _peakGrid.CellClick += (_, e) =>
        {
            if (_fillingPeakGrid || e.RowIndex < 0) return;
            if (_peakGrid.Rows[e.RowIndex].Cells[0].Value is string ch && (string)_cmbChannel.SelectedItem != ch)
                _cmbChannel.SelectedItem = ch;
        };

        _peakGrid.Dock = DockStyle.Fill;
        var bottomPanel = new Panel { Height = 250 };
        bottomPanel.Controls.Add(_peakGrid);
        page.Controls.Add(Split(_erpViewer, bottomPanel, DockStyle.Bottom));
        page.Controls.Add(bar);
        return page;
    }

    private Control BuildTopoPage()
    {
        var page = new Panel();
        _topo = new Topographic7ChannelView { Dock = DockStyle.Fill };
        _topo.ChannelSelected += ch => { _cmbChannel.SelectedItem = ch; SelectPage(1); };
        var bar = Ui.Bar(DockStyle.Top, 38);
        bar.Controls.Add(Ui.Check("Yalnızca fark dalgası", false, (s, _) => { _topo.DifferenceOnly = ((CheckBox)s!).Checked; _topo.Invalidate(); }));
        bar.Controls.Add(Ui.Button("PNG", (_, _) => SavePng(_topo, "topografi")));
        page.Controls.Add(_topo);
        page.Controls.Add(bar);
        return page;
    }

    private Control BuildSpectrumPage()
    {
        var page = new Panel();
        _spectrum = new FrequencySpectrumViewer { Dock = DockStyle.Fill };
        _qualityGauge = new SignalQualityGauge { Dock = DockStyle.Fill };
        var bar = Ui.Bar(DockStyle.Top, 38);
        _cmbSpectrum = Ui.Combo(140, "Tüm kayıt", "Dinlenme");
        _cmbSpectrum.SelectedIndex = 0;
        _cmbSpectrum.SelectedIndexChanged += (_, _) => RefreshSpectrum();
        var cmbCh = Ui.Combo(90, new object[] { "Tümü" }.Concat(EegConstants.ChannelNames).ToArray());
        cmbCh.SelectedIndex = 0;
        cmbCh.SelectedIndexChanged += (_, _) => { _spectrum.HighlightChannel = cmbCh.SelectedIndex == 0 ? null : (string)cmbCh.SelectedItem; _spectrum.Invalidate(); };
        bar.Controls.AddRange(new Control[] { Ui.Label("Bölüm:"), _cmbSpectrum, Ui.Label("Vurgula:"), cmbCh,
            Ui.Button("PNG", (_, _) => SavePng(_spectrum, "spektrum")) });
        var side = new Panel { Width = 500 };
        side.Controls.Add(_qualityGauge);
        page.Controls.Add(Split(_spectrum, side, DockStyle.Right));
        page.Controls.Add(bar);
        return page;
    }

    private Control BuildBehaviorPage()
    {
        var page = new Panel();
        _behaviorSummary = new Label
        {
            Dock = DockStyle.Top, Height = 70, Padding = new Padding(12, 10, 12, 0),
            ForeColor = Theme.Text, Font = Theme.UiFont(10f), BackColor = Theme.Surface,
        };
        _trialGrid = Ui.Grid();
        foreach (var (n, h) in new[] { ("i", "#"), ("t", "Zaman (s)"), ("m", "Marker"), ("c", "Renk"), ("tg", "Hedef?"), ("o", "Sonuç"), ("rt", "RT (ms)"), ("rj", "Epoch") })
            _trialGrid.Columns.Add(n, h);
        _trialGrid.Columns["i"]!.FillWeight = 35; _trialGrid.Columns["m"]!.FillWeight = 150;
        _trialGrid.Columns["o"]!.FillWeight = 130; _trialGrid.Columns["tg"]!.FillWeight = 60;
        _trialGrid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex == 5 && _trialGrid.Rows[e.RowIndex].Tag is TrialResult tr)
                e.CellStyle!.ForeColor = tr.Outcome switch
                {
                    TrialOutcome.Hit => Theme.Good, TrialOutcome.CorrectRejection => Theme.TextMuted,
                    TrialOutcome.Miss => Theme.Fair, _ => Theme.Poor,
                };
        };
        _epochGrid = Ui.Grid();
        foreach (var (n, h) in new[] { ("i", "#"), ("t", "Zaman (s)"), ("m", "Marker"), ("c", "Koşul"), ("r", "Durum"), ("why", "Red nedeni") })
            _epochGrid.Columns.Add(n, h);
        _epochGrid.Columns["why"]!.FillWeight = 220;
        _epochGrid.Columns["i"]!.FillWeight = 35; _epochGrid.Columns["m"]!.FillWeight = 150;

        var left = new Panel();
        left.Controls.Add(_trialGrid);
        left.Controls.Add(Header("Denemeler (dikkat testi)"));
        var right = new Panel { Width = 680 };
        right.Controls.Add(_epochGrid);
        right.Controls.Add(Header("Epochlar ve artefakt reddi"));
        page.Controls.Add(Split(left, right, DockStyle.Right));
        page.Controls.Add(_behaviorSummary);
        return page;
    }

    /// <summary>Doldurulan ana kontrol + sabit boyutlu yan panel, aralarında sürüklenebilir ayırıcı.</summary>
    private static Panel Split(Control fill, Control side, DockStyle sideDock)
    {
        var host = new Panel { Dock = DockStyle.Fill };
        fill.Dock = DockStyle.Fill;
        side.Dock = sideDock;
        var splitter = new Splitter { Dock = sideDock, BackColor = Theme.Border, Width = 5, Height = 5, MinSize = 120 };
        host.Controls.Add(fill);
        host.Controls.Add(splitter);
        host.Controls.Add(side);
        return host;
    }

    private static Label Header(string text)
    {
        var l = Ui.Label("  " + text, false, 9.5f, FontStyle.Bold);
        l.Dock = DockStyle.Top; l.AutoSize = false; l.Height = 26; l.BackColor = Theme.Surface;
        return l;
    }

    private Control BuildReportPage()
    {
        var page = new Panel { Padding = new Padding(16) };
        var form = new TableLayoutPanel { Dock = DockStyle.Top, Height = 190, ColumnCount = 2, BackColor = Theme.Background };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        TextBox Field(string label, int row, bool multi = false)
        {
            var tb = new TextBox { Dock = DockStyle.Fill, Multiline = multi, Height = multi ? 60 : 24, BackColor = Theme.SurfaceAlt, ForeColor = Theme.Text };
            tb.TextChanged += (_, _) => PushParticipantInfo();
            form.Controls.Add(Ui.Label(label), 0, row);
            form.Controls.Add(tb, 1, row);
            return tb;
        }
        _txtId = Field("Katılımcı ID", 0);
        _txtName = Field("Ad Soyad", 1);
        _txtAge = Field("Yaş", 2);
        _txtNotes = Field("Notlar", 3, true);
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(0, 10, 0, 0), BackColor = Theme.Background };
        buttons.Controls.Add(Ui.Button("📝 HTML Rapor Oluştur ve Aç", (_, _) => GenerateReport(), Color.FromArgb(90, 60, 130)));
        buttons.Controls.Add(Ui.Button("📊 İstatistik CSV Dökümü", (_, _) => ExportStatsCsv()));
        buttons.Controls.Add(Ui.Button("🖼 Grafikleri PNG Kaydet", (_, _) => ExportPngs()));
        buttons.Controls.Add(Ui.Button("💾 Oturum CSV", (_, _) => ExportCombinedCsv()));
        buttons.Controls.Add(Ui.Button("{ } JSON", (_, _) => ExportJson()));
        buttons.Controls.Add(Ui.Button("📁 Rapor Klasörü", (_, _) => OpenFolder(ErpReportGenerator.DefaultReportFolder)));

        var hint = Ui.Label("Rapor tarayıcıda açılır; PDF için tarayıcıda Yazdır → \"PDF olarak kaydet\" seçin.", true);
        hint.Dock = DockStyle.Top;
        hint.Padding = new Padding(0, 4, 0, 8);

        _txtLog = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = Theme.Surface, ForeColor = Theme.TextMuted, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9f),
        };
        page.Controls.Add(_txtLog);
        page.Controls.Add(Header("Günlük: içe aktarma notları, analiz uyarıları ve yorum"));
        page.Controls.Add(hint);
        page.Controls.Add(buttons);
        page.Controls.Add(form);
        return page;
    }

    // ================================================================== veri yükleme

    private async Task OpenFileDialogAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "EEG kaydı aç",
            Filter = "EEG kayıtları (*.xdf;*.csv;*.tsv;*.txt)|*.xdf;*.csv;*.tsv;*.txt|LabRecorder XDF (*.xdf)|*.xdf|CSV (*.csv)|*.csv|Tüm dosyalar|*.*",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) await OpenFileAsync(dlg.FileName);
    }

    private async Task OpenFileAsync(string path)
    {
        try
        {
            SetStatus($"Yükleniyor: {Path.GetFileName(path)}...");
            UseWaitCursor = true;
            var session = await Task.Run(() => Path.GetExtension(path).Equals(".xdf", StringComparison.OrdinalIgnoreCase)
                ? XdfReader.Load(path)
                : CsvDataReader.LoadEeg(path));
            await LoadSessionAsync(session);
            if (session.Markers.Count == 0)
                SetStatus("EEG yüklendi ancak marker yok. 'Marker CSV Ekle' ile marker dosyasını ekleyin.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Dosya açılamadı", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Dosya açılamadı.");
        }
        finally { UseWaitCursor = false; }
    }

    private async void AddMarkerCsv()
    {
        if (_session == null)
        {
            MessageBox.Show(this, "Önce EEG verisini açın.", "Marker CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dlg = new OpenFileDialog { Title = "Marker CSV ekle", Filter = "CSV (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|Tüm dosyalar|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var markers = CsvDataReader.LoadMarkers(dlg.FileName, _session);
            _session.Markers.AddRange(markers);
            _session.ImportNotes.Add($"{markers.Count} marker eklendi: {Path.GetFileName(dlg.FileName)}");
            await RunAnalysisAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Marker CSV okunamadı", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task CreateSimulationAsync()
    {
        SetStatus("Simülasyon verisi üretiliyor...");
        var s = await Task.Run(() => XonSimulationGenerator.Generate(new SimulationOptions()));
        await LoadSessionAsync(s);
    }

    private async Task OpenLiveAsync()
    {
        using var f = new LiveLslForm(_settings);
        if (f.ShowDialog(this) == DialogResult.OK && f.RecordedSession != null)
            await LoadSessionAsync(f.RecordedSession);
    }

    private async Task LoadSessionAsync(EegRecordingSession s)
    {
        _session = s;
        _result = null;
        _txtId.Text = s.ParticipantId;
        _txtName.Text = s.ParticipantName;
        _txtAge.Text = s.ParticipantAge;
        _txtNotes.Text = s.Notes;
        UpdateAll(resetView: true);
        await RunAnalysisAsync();
    }

    private void PushParticipantInfo()
    {
        if (_session == null) return;
        _session.ParticipantId = _txtId.Text.Trim();
        _session.ParticipantName = _txtName.Text.Trim();
        _session.ParticipantAge = _txtAge.Text.Trim();
        _session.Notes = _txtNotes.Text.Trim();
    }

    // ================================================================== analiz

    private async Task RunAnalysisAsync()
    {
        if (_session == null) { SetStatus("Analiz için önce veri yükleyin."); return; }
        if (_analyzing) return;
        _analyzing = true;
        UseWaitCursor = true;
        var sw = Stopwatch.StartNew();
        try
        {
            var progress = new Progress<string>(SetStatus);
            var session = _session;
            var settings = _settings;
            _result = await Task.Run(() => SignalProcessing.ErpEngine.Analyze(session, settings, progress));
            UpdateAll();
            SetStatus($"Analiz tamamlandı ({sw.ElapsedMilliseconds} ms): {_result.TargetEpochsAccepted} hedef, {_result.StandardEpochsAccepted} standart epoch." +
                      (_result.Warnings.Count > 0 ? $"  ⚠ {_result.Warnings.Count} uyarı (Rapor sekmesi)" : ""));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Analiz hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Analiz başarısız: " + ex.Message);
        }
        finally
        {
            _analyzing = false;
            UseWaitCursor = false;
        }
    }

    private async Task EditSettingsAsync()
    {
        using var f = new FilterSettingsForm(_settings, _session?.SampleRate ?? EegConstants.DefaultSampleRate);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        _settings = f.Settings;
        if (_session != null) await RunAnalysisAsync();
    }

    // ================================================================== görünüm güncelleme

    private void UpdateAll(bool resetView = false)
    {
        RefreshCards();
        RefreshSignal(resetView);
        RefreshErp();
        _topo.SetData(_result);
        RefreshSpectrum();
        _qualityGauge.SetData(_result?.Quality);
        RefreshBehavior();
        RefreshLog();
        Text = _session == null ? "XOnERPStudio — X.On EEG & LSL Marker ERP Analiz ve Raporlama" : $"XOnERPStudio — {_session.DisplayTitle}";
    }

    private void RefreshCards()
    {
        if (_session == null)
        {
            foreach (var c in new[] { _cardSession, _cardEpochs, _cardP300, _cardBehavior, _cardRt, _cardQuality }) c.SetValue("—");
            return;
        }
        var d = TimeSpan.FromSeconds(_session.DurationSeconds);
        _cardSession.SetValue($"{(int)d.TotalMinutes}:{d.Seconds:00} dk · {_session.SampleRate:0} Hz",
            $"{_session.ChannelNames.Count} kanal · {_session.Markers.Count} marker · {_session.SourceDescription}");
        if (_result == null) return;

        _cardEpochs.SetValue($"{_result.TargetEpochsAccepted}/{_result.TargetEpochsTotal} · {_result.StandardEpochsAccepted}/{_result.StandardEpochsTotal}",
            "hedef · standart", _result.TargetEpochsAccepted < 10 ? Theme.Poor : Theme.Accent);
        var p = _result.Get(EegConstants.VirtualParietalName)?.Target?.P300;
        _cardP300.SetValue(p is { Found: true } ? $"{p.AmplitudeUv:0.0} µV" : "—", p is { Found: true } ? $"{p.LatencyMs:0} ms gecikme" : "");
        var b = _result.Behavior;
        if (b.HasResponseMarkers)
        {
            _cardBehavior.SetValue($"%{b.Accuracy * 100:0} · {b.DPrime:0.00}", $"Hit {b.Hits}/{b.TargetCount} · FA {b.FalseAlarms}/{b.DistractorCount}");
            _cardRt.SetValue(double.IsNaN(b.MeanRtMs) ? "—" : $"{b.MeanRtMs:0} ms", double.IsNaN(b.SdRtMs) ? "" : $"SS ± {b.SdRtMs:0} ms");
        }
        else
        {
            _cardBehavior.SetValue("—", "tepki markerı yok");
            _cardRt.SetValue("—");
        }
        int good = _result.Quality.Count(q => q.Level == QualityLevel.Good), poor = _result.Quality.Count(q => q.Level == QualityLevel.Poor);
        _cardQuality.SetValue($"{good}/{_result.Quality.Count} iyi", poor > 0 ? $"{poor} kanal zayıf" : "tüm kanallar kabul edilebilir",
            poor > 0 ? Theme.Poor : good == _result.Quality.Count ? Theme.Good : Theme.Fair);
    }

    private void RefreshSignal(bool resetView)
    {
        if (_session == null) { _eegViewer.Clear(); _markerGrid.Rows.Clear(); return; }
        double[][] data;
        if (_chkFiltered.Checked && _result?.FilteredData != null) data = _result.FilteredData;
        else data = _session.Data.Select(d => { var c = (double[])d.Clone(); SignalProcessing.DigitalFilter.RemoveMean(c); return c; }).ToArray();

        _eegViewer.TimeOrigin = _session.StartTime;
        _eegViewer.SetData(_session.Timestamps, data, _session.ChannelNames, _session.Markers, resetView);

        if (resetView || _markerGrid.Rows.Count != _session.Markers.Count || _result != null)
        {
            _markerGrid.SuspendLayout();
            _markerGrid.Rows.Clear();
            foreach (var m in _session.Markers)
            {
                int i = _markerGrid.Rows.Add($"{m.Timestamp - _session.StartTime:0.000}", m.Value, MarkerEvent.DisplayName(m.Category));
                _markerGrid.Rows[i].Tag = m;
            }
            _markerGrid.ResumeLayout();
        }
        SyncScroll();
    }

    private void SyncScroll()
    {
        double span = Math.Max(0, _eegViewer.DataEnd - _eegViewer.DataStart - _eegViewer.WindowSeconds);
        _eegScroll.Minimum = 0;
        _eegScroll.Maximum = (int)(span * 10) + Math.Max(1, (int)(_eegViewer.WindowSeconds));
        _eegScroll.LargeChange = Math.Max(1, (int)(_eegViewer.WindowSeconds));
        _eegScroll.SmallChange = 5;
        int v = (int)((_eegViewer.ViewStart - _eegViewer.DataStart) * 10);
        _eegScroll.Value = Math.Clamp(v, _eegScroll.Minimum, Math.Max(_eegScroll.Minimum, _eegScroll.Maximum - _eegScroll.LargeChange + 1));
    }

    private void RefreshErp()
    {
        string ch = _cmbChannel.SelectedItem as string ?? EegConstants.VirtualParietalName;
        _erpViewer.SetData(_result?.Get(ch), _result?.Settings ?? _settings);
        _erpViewer.NegativeUp = _settings.NegativeUp;

        if (_peakGrid.Rows.Count > 0 && _result != null && _peakGrid.Tag == _result) return;
        _fillingPeakGrid = true;
        _peakGrid.Rows.Clear();
        _peakGrid.Tag = _result;
        if (_result == null) { _fillingPeakGrid = false; return; }
        static string L(ComponentPeak p) => p is { Found: true } ? p.LatencyMs.ToString("0") + (p.AtWindowEdge ? "*" : "") : "—";
        static string A(ComponentPeak p) => p is { Found: true } ? p.AmplitudeUv.ToString("0.0") : "—";
        foreach (var c in _result.Channels.Values)
            _peakGrid.Rows.Add(c.ChannelName, L(c.Target?.P300), A(c.Target?.P300), L(c.Target?.N200), A(c.Target?.N200),
                L(c.Target?.N100), A(c.Target?.N100), A(c.Standard?.P300), A(c.Difference?.P300), L(c.Difference?.P300),
                c.Target == null || double.IsNaN(c.Target.Snr) ? "—" : c.Target.Snr.ToString("0.0"));
        _peakGrid.ClearSelection();
        _fillingPeakGrid = false;
    }

    private void RefreshSpectrum()
    {
        var spec = _cmbSpectrum.SelectedIndex == 1 ? _result?.RestSpectrum : _result?.Spectrum;
        if (_cmbSpectrum.SelectedIndex == 1 && _result != null && spec == null)
            SetStatus("Bu kayıtta rest_start / rest_end ile işaretli dinlenme bölümü yok.");
        _spectrum.SetData(spec);
    }

    private void RefreshBehavior()
    {
        _trialGrid.Rows.Clear();
        _epochGrid.Rows.Clear();
        if (_result == null) { _behaviorSummary.Text = "Analiz sonucu yok."; return; }

        var b = _result.Behavior;
        _behaviorSummary.Text = b.HasResponseMarkers
            ? $"Hedef renk: {b.TargetColor ?? "?"}   ·   Deneme {b.TotalTrials} (hedef {b.TargetCount}, çeldirici {b.DistractorCount})   ·   " +
              $"Hit %{b.HitRate * 100:0.0}   Miss %{b.MissRate * 100:0.0}   FA %{b.FalseAlarmRate * 100:0.0}   ·   Doğruluk %{b.Accuracy * 100:0.0}\n" +
              $"Tepki süresi {b.MeanRtMs:0} ± {b.SdRtMs:0} ms (medyan {b.MedianRtMs:0})   ·   d′ = {b.DPrime:0.00}   ·   c = {b.Criterion:0.00}"
            : $"Deneme {b.TotalTrials} (hedef {b.TargetCount}, standart {b.DistractorCount}). Kayıtta tepki markerı (response_*) yok — pasif oddball olarak değerlendirildi.";

        _trialGrid.SuspendLayout();
        foreach (var t in b.Trials)
        {
            int i = _trialGrid.Rows.Add(t.Index, $"{t.Stimulus.Timestamp - _session.StartTime:0.000}", t.Stimulus.Value, t.ColorName,
                t.IsTarget ? "Evet" : "", b.HasResponseMarkers ? TrialResult.OutcomeName(t.Outcome) : "—",
                t.ReactionTimeMs?.ToString("0") ?? "", t.EpochRejected ? "reddedildi" : "");
            _trialGrid.Rows[i].Tag = t;
        }
        _trialGrid.ResumeLayout();

        _epochGrid.SuspendLayout();
        int k = 1;
        foreach (var e in _result.Epochs)
        {
            int i = _epochGrid.Rows.Add(k++, $"{e.Trigger.Timestamp - _session.StartTime:0.000}", e.Trigger.Value,
                e.Condition == ErpCondition.Target ? "Hedef" : "Standart", e.Rejected ? "Reddedildi" : "Kabul", e.RejectReason);
            if (e.Rejected) _epochGrid.Rows[i].DefaultCellStyle.ForeColor = Theme.Poor;
        }
        _epochGrid.ResumeLayout();
    }

    private void RefreshLog()
    {
        var lines = new List<string>();
        if (_session != null)
        {
            lines.Add($"Kaynak: {_session.SourceDescription}");
            lines.Add($"Kanallar: {string.Join(", ", _session.ChannelNames)}  ·  {_session.SampleRate:0} Hz (ölçülen {_session.EffectiveSampleRate():0.00} Hz)");
            lines.AddRange(_session.ImportNotes.Select(n => "• " + n));
        }
        if (_result != null)
        {
            lines.Add("");
            lines.Add("Analiz uyarıları:");
            lines.AddRange(_result.Warnings.Count > 0 ? _result.Warnings.Select(w => "⚠ " + w) : new[] { "  (yok)" });
            lines.Add("");
            lines.Add("Otomatik yorum:");
            lines.AddRange(ErpReportGenerator.Interpret(_result).Select(p => "  " + System.Text.RegularExpressions.Regex.Replace(p, "<.*?>", "")));
        }
        _txtLog.Text = string.Join(Environment.NewLine, lines);
    }

    // ================================================================== dışa aktarma

    private bool RequireResult()
    {
        if (_result != null) return true;
        MessageBox.Show(this, "Önce veri yükleyip analizi çalıştırın.", "XOnERPStudio", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private void GenerateReport()
    {
        if (!RequireResult()) return;
        try
        {
            PushParticipantInfo();
            string path = ErpReportGenerator.Save(_result);
            SetStatus("Rapor oluşturuldu: " + path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Rapor hatası", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ExportStatsCsv()
    {
        if (!RequireResult()) return;
        using var dlg = new FolderBrowserDialog { Description = "CSV dosyalarının kaydedileceği klasör", UseDescriptionForTitle = true, SelectedPath = ErpReportGenerator.DefaultReportFolder };
        Directory.CreateDirectory(ErpReportGenerator.DefaultReportFolder);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var files = CsvReportExporter.ExportAll(_result, dlg.SelectedPath);
            SetStatus($"{files.Count} CSV dosyası yazıldı: {dlg.SelectedPath}");
            OpenFolder(dlg.SelectedPath);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "CSV hatası", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ExportPngs()
    {
        if (!RequireResult()) return;
        using var dlg = new FolderBrowserDialog { Description = "PNG grafiklerinin kaydedileceği klasör", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string id = string.IsNullOrWhiteSpace(_session.ParticipantId) ? "oturum" : string.Join("_", _session.ParticipantId.Split(Path.GetInvalidFileNameChars()));
        var view = new ErpWaveformViewer();
        foreach (var name in EegConstants.ChannelNames.Append(EegConstants.VirtualParietalName))
        {
            view.SetData(_result.Get(name), _result.Settings);
            view.SavePng(Path.Combine(dlg.SelectedPath, $"{id}_erp_{name.Replace("*", "")}.png"), 1400, 700);
        }
        _topo.SavePng(Path.Combine(dlg.SelectedPath, $"{id}_topografi.png"), 1200, 1100);
        _spectrum.SavePng(Path.Combine(dlg.SelectedPath, $"{id}_spektrum.png"), 1400, 800);
        _qualityGauge.SavePng(Path.Combine(dlg.SelectedPath, $"{id}_kalite.png"), 900, 400);
        SetStatus("PNG grafikleri kaydedildi: " + dlg.SelectedPath);
        OpenFolder(dlg.SelectedPath);
    }

    private void SavePng(PlotControlBase c, string name)
    {
        using var dlg = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = $"{name.Replace("*", "")}.png" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        c.SavePng(dlg.FileName, Math.Max(c.Width, 1200), Math.Max(c.Height, 600));
        SetStatus("Kaydedildi: " + dlg.FileName);
    }

    private void ExportCombinedCsv()
    {
        if (_session == null) { RequireResult(); return; }
        using var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"{(_session.ParticipantId is { Length: > 0 } id ? id : "oturum")}_eeg_marker.csv" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        SessionExporter.ExportCombinedCsv(_session, dlg.FileName);
        SessionExporter.ExportMarkersCsv(_session, Path.ChangeExtension(dlg.FileName, null) + "_markers.csv");
        SetStatus("Oturum CSV kaydedildi: " + dlg.FileName);
    }

    private void ExportJson()
    {
        if (_session == null) { RequireResult(); return; }
        using var dlg = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = $"{(_session.ParticipantId is { Length: > 0 } id ? id : "oturum")}.json" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        SessionExporter.ExportJson(_session, dlg.FileName, _result);
        SetStatus("JSON kaydedildi: " + dlg.FileName);
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void SetStatus(string s) => _status.Text = s;

    // ================================================================== ekran görüntüsü (doğrulama)

    /// <summary>Simülasyon yükler, her sekmeyi PNG olarak kaydeder (XOnERPStudio.exe --screens klasör).</summary>
    internal async Task CaptureScreensAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        await CreateSimulationAsync();
        for (int i = 0; i < _pages.Count; i++)
        {
            SelectPage(i);
            Application.DoEvents();
            await Task.Delay(150);
            using var bmp = new Bitmap(Width, Height);
            DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
            bmp.Save(Path.Combine(folder, $"sekme{i + 1}.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
