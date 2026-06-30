# Generiert AdfsTester.ico (Multi-Size: 16/32/48/64/256)
# Design: Blaues abgerundetes Quadrat mit Schild (Federation/Identity) + Schluessel-Badge

Add-Type -AssemblyName System.Drawing

$sizes = @(256, 64, 48, 32, 16)
$pngStreams = @{}

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Abgerundetes blaues Quadrat
    $accent = [System.Drawing.Color]::FromArgb(0, 120, 215)
    $bg     = New-Object System.Drawing.SolidBrush $accent
    $r      = [int]($s * 0.18)
    $rect   = New-Object System.Drawing.Drawing2D.GraphicsPath
    $rect.AddArc(0, 0, $r*2, $r*2, 180, 90)
    $rect.AddArc($s-$r*2-1, 0, $r*2, $r*2, 270, 90)
    $rect.AddArc($s-$r*2-1, $s-$r*2-1, $r*2, $r*2, 0, 90)
    $rect.AddArc(0, $s-$r*2-1, $r*2, $r*2, 90, 90)
    $rect.CloseFigure()
    $g.FillPath($bg, $rect)

    # Schild (weiss)
    $white = [System.Drawing.Color]::White
    $shW = [int]($s * 0.52)
    $shH = [int]($s * 0.58)
    $shX = [int](($s - $shW) / 2)
    $shY = [int]($s * 0.16)
    $shield = New-Object System.Drawing.Drawing2D.GraphicsPath
    $midX = $shX + $shW / 2
    $shield.AddLine($shX, $shY, ($shX+$shW), $shY)
    $shield.AddLine(($shX+$shW), $shY, ($shX+$shW), ($shY + $shH*0.5))
    $shield.AddBezier(($shX+$shW), ($shY + $shH*0.5), ($shX+$shW), ($shY + $shH*0.85), $midX, ($shY+$shH), $midX, ($shY+$shH))
    $shield.AddBezier($midX, ($shY+$shH), $midX, ($shY+$shH), $shX, ($shY + $shH*0.85), $shX, ($shY + $shH*0.5))
    $shield.CloseFigure()
    $brushW = New-Object System.Drawing.SolidBrush $white
    $g.FillPath($brushW, $shield)

    # Schluesselloch (blau) im Schild
    if ($s -ge 24) {
        $kr = [int]($s * 0.09)
        $kx = [int]($midX - $kr)
        $ky = [int]($shY + $shH * 0.32)
        $g.FillEllipse($bg, $kx, $ky, $kr*2, $kr*2)
        $stemW = [int]($s * 0.06)
        $g.FillRectangle($bg, [int]($midX - $stemW/2), ($ky + $kr), $stemW, [int]($s * 0.16))
    }

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngStreams[$s] = $ms.ToArray()
    $bmp.Dispose()
}

$out = [System.IO.Path]::Combine($PSScriptRoot, "AdfsTester.ico")
$fs  = [System.IO.File]::Open($out, [System.IO.FileMode]::Create)
$bw  = New-Object System.IO.BinaryWriter $fs

$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)
$dataOffset = 6 + (16 * $sizes.Count)

foreach ($s in $sizes) {
    $data = $pngStreams[$s]
    $bw.Write([byte]($(if ($s -eq 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -eq 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$dataOffset)
    $dataOffset += $data.Length
}
foreach ($s in $sizes) { $bw.Write($pngStreams[$s]) }
$bw.Flush(); $bw.Close(); $fs.Close()
Write-Output ("Icon erstellt: " + $out)
