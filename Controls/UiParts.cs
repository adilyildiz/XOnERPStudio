namespace XOnERPStudio.Controls;

/// <summary>Özet bilgi kartı: başlık, büyük değer ve alt açıklama.</summary>
public class SummaryCard : Control
{
    private string _value = "—", _sub = "";

    public SummaryCard(string title)
    {
        Title = title;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(170, 66);
        Margin = new Padding(0, 0, 10, 0);
        BackColor = Theme.Background;
        AccentColor = Theme.Accent;
    }

    public string Title { get; set; }
    public Color AccentColor { get; set; }

    public void SetValue(string value, string sub = "", Color? accent = null)
    {
        _value = value;
        _sub = sub;
        if (accent.HasValue) AccentColor = accent.Value;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var p = Theme.RoundedRect(r, 8))
        using (var b = new SolidBrush(Theme.Surface))
        using (var pen = new Pen(Theme.Border))
        {
            g.FillPath(b, p);
            g.DrawPath(pen, p);
        }
        using (var ab = new SolidBrush(AccentColor)) g.FillRectangle(ab, 0, 10, 3, Height - 20);
        using var tf = Theme.UiFont(8f);
        using var vf = Theme.UiFont(14f, FontStyle.Bold);
        using var sf = Theme.UiFont(7.5f);
        using var muted = new SolidBrush(Theme.TextMuted);
        using var text = new SolidBrush(Theme.Text);
        g.DrawString(Title, tf, muted, 10, 6);
        g.DrawString(_value, vf, text, 9, 22);
        g.DrawString(_sub, sf, muted, new RectangleF(10, 48, Width - 14, 16));
    }
}

/// <summary>Koyu tema menü/araç çubuğu çizicisi.</summary>
public class DarkToolStripRenderer : ToolStripProfessionalRenderer
{
    public DarkToolStripRenderer() : base(new DarkColors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextMuted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.Text;
        base.OnRenderArrow(e);
    }

    private class DarkColors : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => Theme.Surface;
        public override Color MenuStripGradientEnd => Theme.Surface;
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color MenuItemSelected => Theme.SurfaceAlt;
        public override Color MenuItemSelectedGradientBegin => Theme.SurfaceAlt;
        public override Color MenuItemSelectedGradientEnd => Theme.SurfaceAlt;
        public override Color MenuItemPressedGradientBegin => Theme.SurfaceAlt;
        public override Color MenuItemPressedGradientEnd => Theme.SurfaceAlt;
        public override Color MenuItemBorder => Theme.Border;
        public override Color MenuBorder => Theme.Border;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
        public override Color StatusStripGradientBegin => Theme.Surface;
        public override Color StatusStripGradientEnd => Theme.Surface;
        public override Color ToolStripBorder => Theme.Border;
    }
}

/// <summary>Yardımcı: koyu temalı düğme oluşturucu.</summary>
public static class Ui
{
    public static Button Button(string text, EventHandler onClick, Color? back = null, int width = 0)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = width == 0,
            Width = width > 0 ? width : 80,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = back ?? Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            Font = Theme.UiFont(9f),
            Margin = new Padding(0, 0, 6, 0),
            Padding = new Padding(6, 0, 6, 0),
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderColor = Theme.Border;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(b.BackColor, 0.15f);
        if (onClick != null) b.Click += onClick;
        return b;
    }

    public static Label Label(string text, bool muted = false, float size = 9f, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = muted ? Theme.TextMuted : Theme.Text,
        Font = Theme.UiFont(size, style),
        Margin = new Padding(0, 7, 6, 0),
    };

    public static CheckBox Check(string text, bool value, EventHandler onChange)
    {
        var c = new CheckBox { Text = text, Checked = value, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 6, 10, 0) };
        if (onChange != null) c.CheckedChanged += onChange;
        return c;
    }

    public static ComboBox Combo(int width, params object[] items)
    {
        var c = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = width,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 3, 8, 0),
        };
        c.Items.AddRange(items);
        return c;
    }

    public static FlowLayoutPanel Bar(DockStyle dock = DockStyle.Top, int height = 40) => new()
    {
        Dock = dock,
        Height = height,
        Padding = new Padding(8, 5, 8, 0),
        BackColor = Theme.Surface,
        WrapContents = false,
        AutoScroll = false,
    };

    public static DataGridView Grid()
    {
        var g = new DataGridView { Dock = DockStyle.Fill };
        Theme.StyleGrid(g);
        return g;
    }
}
