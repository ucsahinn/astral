[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot '..\src\Astral.App\Assets'
}

function New-RoundedRectanglePath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Color {
    param(
        [int]$Alpha,
        [int]$Red,
        [int]$Green,
        [int]$Blue
    )

    return [System.Drawing.Color]::FromArgb($Alpha, $Red, $Green, $Blue)
}

function New-Pen {
    param(
        [System.Drawing.Brush]$Brush,
        [float]$Width
    )

    $pen = [System.Drawing.Pen]::new($Brush, $Width)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    return $pen
}

function New-SolidPen {
    param(
        [System.Drawing.Color]$Color,
        [float]$Width
    )

    $brush = [System.Drawing.SolidBrush]::new($Color)
    return New-Pen -Brush $brush -Width $Width
}

function Draw-AstralIcon {
    param(
        [int]$Size,
        [string]$Path
    )

    $scale = [single]($Size / 512.0)
    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    try {
        $x = 50 * $scale
        $y = 50 * $scale
        $w = 412 * $scale
        $h = 412 * $scale
        $r = 88 * $scale

        foreach ($glow in @(28, 20, 12, 6)) {
            $glowPath = New-RoundedRectanglePath `
                -X ($x - ($glow * 0.38 * $scale)) `
                -Y ($y - ($glow * 0.38 * $scale)) `
                -Width ($w + ($glow * 0.76 * $scale)) `
                -Height ($h + ($glow * 0.76 * $scale)) `
                -Radius ($r + ($glow * 0.42 * $scale))
            $alpha = [Math]::Max(16, 76 - ($glow * 2))
            $glowBrush = [System.Drawing.SolidBrush]::new((New-Color $alpha 93 255 146))
            $glowPen = New-Pen -Brush $glowBrush -Width ([single]($glow * $scale))
            $graphics.DrawPath($glowPen, $glowPath)
            $glowPen.Dispose()
            $glowBrush.Dispose()
            $glowPath.Dispose()
        }

        $outer = New-RoundedRectanglePath -X $x -Y $y -Width $w -Height $h -Radius $r
        $fillRect = [System.Drawing.RectangleF]::new($x, $y, $w, $h)
        $fill = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            $fillRect,
            (New-Color 255 15 22 43),
            (New-Color 255 9 5 25),
            135)
        $graphics.FillPath($fill, $outer)

        $innerGlow = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            $fillRect,
            (New-Color 120 125 235 255),
            (New-Color 45 255 63 117),
            35)
        $innerGlowPath = New-RoundedRectanglePath `
            -X (72 * $scale) `
            -Y (72 * $scale) `
            -Width (368 * $scale) `
            -Height (368 * $scale) `
            -Radius (72 * $scale)
        $graphics.FillPath($innerGlow, $innerGlowPath)

        $veil = New-RoundedRectanglePath `
            -X (74 * $scale) `
            -Y (74 * $scale) `
            -Width (364 * $scale) `
            -Height (364 * $scale) `
            -Radius (70 * $scale)
        $veilBrush = [System.Drawing.SolidBrush]::new((New-Color 218 15 12 35))
        $graphics.FillPath($veilBrush, $veil)

        $borderBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            $fillRect,
            (New-Color 255 125 235 255),
            (New-Color 255 93 255 146),
            42)
        $borderPen = New-Pen -Brush $borderBrush -Width ([single](13 * $scale))
        $graphics.DrawPath($borderPen, $outer)

        $innerBorder = New-RoundedRectanglePath `
            -X (82 * $scale) `
            -Y (82 * $scale) `
            -Width (348 * $scale) `
            -Height (348 * $scale) `
            -Radius (68 * $scale)
        $innerBorderPen = New-SolidPen -Color (New-Color 222 245 247 251) -Width ([single](4.5 * $scale))
        $graphics.DrawPath($innerBorderPen, $innerBorder)

        $arcPen = New-SolidPen -Color (New-Color 125 245 247 251) -Width ([single](3.2 * $scale))
        $graphics.DrawArc(
            $arcPen,
            [single](120 * $scale),
            [single](78 * $scale),
            [single](218 * $scale),
            [single](116 * $scale),
            196,
            84)

        $aPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $aPath.StartFigure()
        $aPath.AddLine(
            [System.Drawing.PointF]::new(154 * $scale, 338 * $scale),
            [System.Drawing.PointF]::new(256 * $scale, 146 * $scale))
        $aPath.AddLine(
            [System.Drawing.PointF]::new(256 * $scale, 146 * $scale),
            [System.Drawing.PointF]::new(358 * $scale, 338 * $scale))

        $aShadowPen = New-SolidPen -Color (New-Color 95 255 63 117) -Width ([single](78 * $scale))
        $graphics.DrawPath($aShadowPen, $aPath)
        $aEdgeBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.RectangleF]::new(120 * $scale, 110 * $scale, 280 * $scale, 275 * $scale),
            (New-Color 255 125 235 255),
            (New-Color 255 57 232 255),
            90)
        $aEdgePen = New-Pen -Brush $aEdgeBrush -Width ([single](66 * $scale))
        $graphics.DrawPath($aEdgePen, $aPath)
        $aFillBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.RectangleF]::new(140 * $scale, 120 * $scale, 238 * $scale, 250 * $scale),
            (New-Color 255 255 255 255),
            (New-Color 255 226 244 255),
            90)
        $aFillPen = New-Pen -Brush $aFillBrush -Width ([single](45 * $scale))
        $graphics.DrawPath($aFillPen, $aPath)

        $cutoutPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $cutoutPath.AddPolygon(@(
            [System.Drawing.PointF]::new(256 * $scale, 198 * $scale),
            [System.Drawing.PointF]::new(210 * $scale, 288 * $scale),
            [System.Drawing.PointF]::new(302 * $scale, 288 * $scale)
        ))
        $cutoutBrush = [System.Drawing.SolidBrush]::new((New-Color 255 12 9 30))
        $graphics.FillPath($cutoutBrush, $cutoutPath)

        $barShadowPen = New-SolidPen -Color (New-Color 100 255 63 117) -Width ([single](21 * $scale))
        $graphics.DrawLine(
            $barShadowPen,
            [single](210 * $scale),
            [single](310 * $scale),
            [single](302 * $scale),
            [single](310 * $scale))
        $barPen = New-SolidPen -Color (New-Color 255 255 87 127) -Width ([single](14 * $scale))
        $graphics.DrawLine(
            $barPen,
            [single](211 * $scale),
            [single](309 * $scale),
            [single](301 * $scale),
            [single](309 * $scale))

        $nodeLinePen = New-SolidPen -Color (New-Color 255 93 255 146) -Width ([single](10 * $scale))
        $graphics.DrawLine(
            $nodeLinePen,
            [single](363 * $scale),
            [single](184 * $scale),
            [single](412 * $scale),
            [single](134 * $scale))
        $nodeGlowBrush = [System.Drawing.SolidBrush]::new((New-Color 80 93 255 146))
        $graphics.FillEllipse(
            $nodeGlowBrush,
            [single](340 * $scale),
            [single](160 * $scale),
            [single](50 * $scale),
            [single](50 * $scale))
        $nodeBrush = [System.Drawing.SolidBrush]::new((New-Color 255 93 255 146))
        $graphics.FillEllipse(
            $nodeBrush,
            [single](348 * $scale),
            [single](168 * $scale),
            [single](34 * $scale),
            [single](34 * $scale))

        $sparkBrush = [System.Drawing.SolidBrush]::new((New-Color 190 245 247 251))
        $graphics.FillEllipse(
            $sparkBrush,
            [single](164 * $scale),
            [single](104 * $scale),
            [single](5 * $scale),
            [single](5 * $scale))

        New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function New-IcoFile {
    param(
        [int[]]$Sizes,
        [string]$Path
    )

    $images = @()
    foreach ($size in $Sizes) {
        $temp = Join-Path ([IO.Path]::GetTempPath()) ("astral-icon-$size-" + [guid]::NewGuid().ToString('N') + '.png')
        Draw-AstralIcon -Size $size -Path $temp
        $images += [pscustomobject]@{
            Size = $size
            Bytes = [IO.File]::ReadAllBytes($temp)
        }
        Remove-Item -LiteralPath $temp -Force
    }

    $stream = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$images.Count)
        $offset = 6 + (16 * $images.Count)
        foreach ($image in $images) {
            $sizeByte = if ($image.Size -eq 256) { [byte]0 } else { [byte]$image.Size }
            $writer.Write($sizeByte)
            $writer.Write($sizeByte)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$image.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $image.Bytes.Length
        }

        foreach ($image in $images) {
            $writer.Write($image.Bytes)
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

$assetDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null

Draw-AstralIcon -Size 512 -Path (Join-Path $assetDirectory 'astral-mark.png')
Draw-AstralIcon -Size 128 -Path (Join-Path $assetDirectory 'astral-logo.png')
New-IcoFile -Sizes @(16, 24, 32, 48, 64, 128, 256) -Path (Join-Path $assetDirectory 'astral-v2.ico')
Copy-Item `
    -LiteralPath (Join-Path $assetDirectory 'astral-v2.ico') `
    -Destination (Join-Path $assetDirectory 'astral.ico') `
    -Force

Get-ChildItem -LiteralPath $assetDirectory -Filter 'astral*' |
    Sort-Object Name |
    Select-Object Name, Length
