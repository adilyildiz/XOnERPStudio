using System.Drawing.Drawing2D;

namespace XOnERPStudio.Controls;

/// <summary>Uygulama genelinde kullanılan koyu tema renkleri ve çizim yardımcıları.</summary>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(24, 25, 31);
    public static readonly Color Surface = Color.FromArgb(32, 33, 41);
    public static readonly Color SurfaceAlt = Color.FromArgb(40, 42, 52);
    public static readonly Color Border = Color.FromArgb(56, 58, 70);
    public static readonly Color Grid = Color.FromArgb(44, 46, 56);
    public static readonly Color GridStrong = Color.FromArgb(70, 72, 86);
    public static readonly Color Text = Color.FromArgb(226, 228, 235);
    public static readonly Color TextMuted = Color.FromArgb(150, 154, 168);
    public static readonly Color Accent = Color.FromArgb(74, 144, 226);

    public static readonly Color TargetColor = Color.FromArgb(235, 87, 87);
    public static readonly Color StandardColor = Color.FromArgb(86, 156, 245);
    public static readonly Color DifferenceColor = Color.FromArgb(76, 201, 120);
    public static readonly Color PeakColor = Color.FromArgb(241, 196, 15);
    public static readonly Color PeakNegColor = Color.FromArgb(64, 214, 222);

    public static readonly Color Good = Color.FromArgb(46, 204, 113);
    public static readonly Color Fair = Color.FromArgb(241, 196, 15);
    public static readonly Color Poor = Color.FromArgb(231, 76, 60);

    private static Icon _appIcon;

    /// <summary>Gömülü app.ico'dan yüklenen uygulama simgesi (pencere başlıkları için).</summary>
    public static Icon AppIcon
    {
        get
        {
            if (_appIcon != null) return _appIcon;
            using var s = typeof(Theme).Assembly.GetManifestResourceStream("app.ico");
            if (s != null) _appIcon = new Icon(s);
            return _appIcon;
        }
    }

    public static Font UiFont(float size = 9f, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        if (d <= 0 || r.Width < d || r.Height < d) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Color WithAlpha(Color c, int a) => Color.FromArgb(a, c);

    /// <summary>Okunaklı eksen adımı (1-2-5 dizisi).</summary>
    public static double NiceStep(double range, int targetTicks)
    {
        if (range <= 0) return 1;
        double raw = range / Math.Max(1, targetTicks);
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double n = raw / mag;
        double step = n < 1.5 ? 1 : n < 3 ? 2 : n < 7 ? 5 : 10;
        return step * mag;
    }

    /// <summary>Kontrolü PNG olarak kaydeder.</summary>
    public static void SaveControlPng(Control c, string path)
    {
        using var bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height));
        c.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>Standart WinForms kontrollerine koyu tema uygular (özyinelemeli).</summary>
    public static void Apply(Control root)
    {
        foreach (Control c in root.Controls)
        {
            switch (c)
            {
                case Button b:
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = Border;
                    if (b.BackColor == SystemColors.Control || b.BackColor == Color.Transparent) b.BackColor = SurfaceAlt;
                    b.ForeColor = Text;
                    b.Cursor = Cursors.Hand;
                    break;
                case TextBox or NumericUpDown or ComboBox or ListBox:
                    c.BackColor = SurfaceAlt;
                    c.ForeColor = Text;
                    if (c is ComboBox cb) cb.FlatStyle = FlatStyle.Flat;
                    break;
                case DataGridView g:
                    StyleGrid(g);
                    break;
                case PropertyGrid pg:
                    pg.BackColor = Surface; pg.ViewBackColor = SurfaceAlt; pg.ViewForeColor = Text;
                    pg.LineColor = Border; pg.CategoryForeColor = Text; pg.HelpBackColor = Surface;
                    pg.HelpForeColor = TextMuted; pg.CategorySplitterColor = Border; pg.ViewBorderColor = Border;
                    pg.HelpBorderColor = Border;
                    break;
                case Label or CheckBox or RadioButton or GroupBox:
                    c.ForeColor = c.ForeColor == SystemColors.ControlText ? Text : c.ForeColor;
                    break;
            }
            if (c.HasChildren) Apply(c);
        }
    }

    public static void StyleGrid(DataGridView g)
    {
        g.BackgroundColor = Surface;
        g.BorderStyle = BorderStyle.None;
        g.GridColor = Border;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
        g.ColumnHeadersDefaultCellStyle.ForeColor = Text;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
        g.ColumnHeadersDefaultCellStyle.Font = UiFont(9f, FontStyle.Bold);
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        g.DefaultCellStyle.BackColor = Surface;
        g.DefaultCellStyle.ForeColor = Text;
        g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(52, 73, 110);
        g.DefaultCellStyle.SelectionForeColor = Text;
        g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(36, 37, 46);
        g.RowHeadersVisible = false;
        g.AllowUserToAddRows = false;
        g.AllowUserToDeleteRows = false;
        g.AllowUserToResizeRows = false;
        g.ReadOnly = true;
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
    }
}

/// <summary>Titreşimsiz çizim için çift tamponlu temel kontrol.</summary>
public abstract class PlotControlBase : Control
{
    protected PlotControlBase()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont(8.5f);
    }

    /// <summary>Kontrolü belirtilen boyutta bitmap'e çizer (rapor/PNG için).</summary>
    public Bitmap RenderToBitmap(int width, int height)
    {
        var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        g.Clear(BackColor);
        var old = Size;
        try
        {
            Size = new Size(width, height);
            OnPaint(new PaintEventArgs(g, new Rectangle(0, 0, width, height)));
        }
        finally { Size = old; }
        return bmp;
    }

    public void SavePng(string path, int width = 0, int height = 0)
    {
        using var bmp = RenderToBitmap(width > 0 ? width : Math.Max(Width, 400), height > 0 ? height : Math.Max(Height, 300));
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
