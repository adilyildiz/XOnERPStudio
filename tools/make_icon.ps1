param([string]$OutIco, [string]$OutPng)
Add-Type -AssemblyName System.Drawing

function Draw-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Yuvarlatılmış kare arka plan (koyu lacivert → mavi gradyan)
    $pad = [Math]::Max(0.5, $s * 0.03)
    $r = $s * 0.22
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($s - 2 * $pad), ($s - 2 * $pad)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 24, 40, 82)), ([System.Drawing.Color]::FromArgb(255, 46, 104, 196)), 55.0
    $g.FillPath($bg, $path)

    # Taban çizgisi ve uyaran başlangıcı (yalnızca büyük boyutlarda)
    $x0 = $s * 0.14; $x1 = $s * 0.86; $base = $s * 0.64
    if ($s -ge 32) {
        $gp = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(70, 255, 255, 255)), ([Math]::Max(1, $s * 0.018))
        $g.DrawLine($gp, $x0, $base, $x1, $base)
        $op = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(120, 255, 255, 255)), ([Math]::Max(1, $s * 0.022))
        $g.DrawLine($op, $s * 0.27, $s * 0.22, $s * 0.27, $s * 0.80)
    }

    # ERP dalgası: düz baseline → N100 çukuru → P200 → N200 → büyük P300 → dönüş
    $pts = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
    $n = 120
    $peakX = 0; $peakY = 0
    for ($i = 0; $i -le $n; $i++) {
        $t = -200 + 1000.0 * $i / $n     # ms
        $v = -0.45 * [Math]::Exp(-0.5 * [Math]::Pow(($t - 100) / 22, 2)) `
             + 0.30 * [Math]::Exp(-0.5 * [Math]::Pow(($t - 190) / 25, 2)) `
             - 0.35 * [Math]::Exp(-0.5 * [Math]::Pow(($t - 245) / 22, 2)) `
             + 1.00 * [Math]::Exp(-0.5 * [Math]::Pow(($t - 360) / 70, 2))
        $x = $x0 + ($x1 - $x0) * $i / $n
        $y = $base - $v * $s * 0.30
        $pts.Add((New-Object System.Drawing.PointF $x, $y))
        if ($i -gt 0 -and $y -lt $peakY -or $i -eq 0) { if ($i -ne 0) { $peakX = $x; $peakY = $y } else { $peakY = [single]::MaxValue } }
    }
    $w = [Math]::Max(1.4, $s * 0.058)
    $glow = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(60, 255, 255, 255)), ($w * 1.9)
    $glow.LineJoin = 'Round'; $glow.StartCap = 'Round'; $glow.EndCap = 'Round'
    if ($s -ge 48) { $g.DrawLines($glow, $pts.ToArray()) }
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $w
    $pen.LineJoin = 'Round'; $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawLines($pen, $pts.ToArray())

    # P300 tepe noktası (altın)
    $dr = [Math]::Max(1.8, $s * 0.068)
    $gold = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 245, 196, 40))
    $g.FillEllipse($gold, $peakX - $dr, $peakY - $dr, 2 * $dr, 2 * $dr)
    if ($s -ge 32) {
        $ring = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 24, 40, 82)), ([Math]::Max(1, $s * 0.02))
        $g.DrawEllipse($ring, $peakX - $dr, $peakY - $dr, 2 * $dr, 2 * $dr)
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = @()
foreach ($sz in $sizes) {
    $b = Draw-Icon $sz
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += , @($sz, $ms.ToArray())
    if ($sz -eq 256) { $b.Save($OutPng, [System.Drawing.Imaging.ImageFormat]::Png) }
    $b.Dispose()
}

# ICO dosyası: ICONDIR + ICONDIRENTRY[] + PNG verileri
$fs = [System.IO.File]::Create($OutIco)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($im in $images) {
    $sz = $im[0]; $data = $im[1]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($im in $images) { $bw.Write($im[1]) }
$bw.Close()
Write-Output "OK $OutIco"
