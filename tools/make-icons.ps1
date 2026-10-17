# Generates the plugin PNG icons (transparent, 144x144) with GDI+.
# Usage: powershell -ExecutionPolicy Bypass -File tools/make-icons.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\com.ulanzi.soundhop.ulanziPlugin\assets\icons'
New-Item -ItemType Directory -Force $out | Out-Null

$accent = [System.Drawing.Color]::FromArgb(255, 0, 255, 230)
$white = [System.Drawing.Color]::White

function New-Canvas {
    $bmp = New-Object System.Drawing.Bitmap 144, 144
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

function New-Pen($color, $width) {
    $p = New-Object System.Drawing.Pen $color, $width
    $p.StartCap = 'Round'; $p.EndCap = 'Round'; $p.LineJoin = 'Round'
    return $p
}

function Save($bmp, $g, $name) {
    $g.Dispose()
    $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Draw-Speaker($g, $dx, $dy, $s) {
    $brush = New-Object System.Drawing.SolidBrush $accent
    $pts = @(@(0,22), @(20,22), @(46,0), @(46,72), @(20,50), @(0,50)) | ForEach-Object {
        New-Object System.Drawing.PointF ($dx + $_[0] * $s), ($dy + $_[1] * $s)
    }
    $g.FillPolygon($brush, [System.Drawing.PointF[]]$pts)
    $pen = New-Pen $accent (8 * $s)
    $cx = $dx + 46 * $s; $cy = $dy + 36 * $s
    foreach ($r in 20, 36) {
        $g.DrawArc($pen, $cx - $r * $s, $cy - $r * $s, 2 * $r * $s, 2 * $r * $s, -50, 100)
    }
}

function Draw-Mic($g, $dx, $dy, $s) {
    $brush = New-Object System.Drawing.SolidBrush $accent
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $w = 32 * $s; $h = 58 * $s; $x = $dx - $w / 2; $y = $dy
    $path.AddArc($x, $y, $w, $w, 180, 180)
    $path.AddArc($x, $y + $h - $w, $w, $w, 0, 180)
    $path.CloseFigure()
    $g.FillPath($brush, $path)
    $pen = New-Pen $accent (8 * $s)
    $r = 28 * $s
    $g.DrawArc($pen, $dx - $r, $dy + 18 * $s, 2 * $r, 2 * $r, 0, 180)
    $g.DrawLine($pen, $dx, $dy + 18 * $s + 2 * $r, $dx, $dy + 92 * $s)
    $g.DrawLine($pen, $dx - 16 * $s, $dy + 92 * $s, $dx + 16 * $s, $dy + 92 * $s)
}

function Draw-Hop($g, $cx, $cy, $s) {
    # Curved "hop" arrow
    $pen = New-Pen $white (7 * $s)
    $g.DrawArc($pen, $cx - 20 * $s, $cy - 12 * $s, 40 * $s, 30 * $s, 200, 140)
    $tipX = $cx + 18 * $s; $tipY = $cy - 2 * $s
    $g.DrawLine($pen, $tipX, $tipY, $tipX - 1 * $s, $tipY - 13 * $s)
    $g.DrawLine($pen, $tipX, $tipY, $tipX - 13 * $s, $tipY + 1 * $s)
}

# Output action
$c = New-Canvas; Draw-Speaker $c[1] 26 36 1; Save $c[0] $c[1] 'output.png'
# Input action
$c = New-Canvas; Draw-Mic $c[1] 72 24 1; Save $c[0] $c[1] 'input.png'
# Plugin / category icon
$c = New-Canvas
Draw-Speaker $c[1] 14 60 0.75
Draw-Mic $c[1] 110 52 0.7
Draw-Hop $c[1] 72 34 1
Save $c[0] $c[1] 'plugin.png'

Get-ChildItem $out | Select-Object Name, Length
