using XOnERPStudio.Controls;
using XOnERPStudio.Models;

namespace XOnERPStudio.Forms;

/// <summary>Filtre, epoch, artefakt, tepe pencereleri ve marker eşleştirme ayarları iletişim kutusu.</summary>
public class FilterSettingsForm : Form
{
    private readonly PropertyGrid _grid;
    private readonly double _sampleRate;

    public ErpFilterSettings Settings { get; private set; }

    public FilterSettingsForm(ErpFilterSettings current, double sampleRate)
    {
        _sampleRate = sampleRate;
        Settings = current.Clone();

        Text = "Analiz Ayarları — Filtre, Epoch ve Artefakt";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(560, 680);
        MinimumSize = new Size(460, 500);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont(9f);
        Icon = Theme.AppIcon;
        ShowInTaskbar = false;
        MinimizeBox = false;

        _grid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            SelectedObject = Settings,
            PropertySort = PropertySort.Categorized,
            ToolbarVisible = false,
            HelpVisible = true,
        };

        var bar = Ui.Bar(DockStyle.Bottom, 46);
        bar.FlowDirection = FlowDirection.RightToLeft;
        bar.Padding = new Padding(8, 8, 8, 0);
        bar.Controls.Add(Ui.Button("Uygula", (_, _) => Accept(), Theme.Accent, 100));
        bar.Controls.Add(Ui.Button("İptal", (_, _) => { DialogResult = DialogResult.Cancel; Close(); }, null, 90));
        bar.Controls.Add(Ui.Button("Varsayılanlar", (_, _) => { Settings = new ErpFilterSettings(); _grid.SelectedObject = Settings; }, null, 120));

        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(10, 6, 10, 0),
            ForeColor = Theme.TextMuted,
            Text = $"Örnekleme hızı: {sampleRate:0} Hz (Nyquist {sampleRate / 2:0} Hz). Ayarlar uygulandığında analiz yeniden çalıştırılır ve bir sonraki açılış için saklanır.",
        };

        Controls.Add(_grid);
        Controls.Add(info);
        Controls.Add(bar);
        Theme.Apply(this);
        AcceptButton = null;
    }

    private void Accept()
    {
        string err = Settings.Validate(_sampleRate);
        if (err != null)
        {
            MessageBox.Show(this, err, "Geçersiz ayar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try { Settings.Save(); } catch { /* kalıcılık başarısızsa oturum içinde yine de kullan */ }
        DialogResult = DialogResult.OK;
        Close();
    }
}
