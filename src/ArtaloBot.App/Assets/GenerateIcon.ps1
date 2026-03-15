# PowerShell script to generate ArtaloBot icon
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 32, 48, 64, 128, 256)

function Create-BotIcon {
    param([int]$size)

    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    # Scale factor
    $scale = $size / 512.0

    # Background gradient (purple to pink)
    $gradientBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0, 0)),
        (New-Object System.Drawing.Point($size, $size)),
        [System.Drawing.Color]::FromArgb(255, 102, 126, 234),
        [System.Drawing.Color]::FromArgb(255, 240, 147, 251)
    )

    # Draw background circle
    $margin = [int](16 * $scale)
    $graphics.FillEllipse($gradientBrush, $margin, $margin, $size - 2*$margin, $size - 2*$margin)

    # Robot head (white)
    $whiteBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(250, 245, 245, 245))
    $headX = [int](126 * $scale)
    $headY = [int](140 * $scale)
    $headW = [int](260 * $scale)
    $headH = [int](220 * $scale)
    $radius = [int](40 * $scale)

    # Draw rounded rectangle for head
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($headX, $headY, $radius*2, $radius*2, 180, 90)
    $path.AddArc($headX + $headW - $radius*2, $headY, $radius*2, $radius*2, 270, 90)
    $path.AddArc($headX + $headW - $radius*2, $headY + $headH - $radius*2, $radius*2, $radius*2, 0, 90)
    $path.AddArc($headX, $headY + $headH - $radius*2, $radius*2, $radius*2, 90, 90)
    $path.CloseFigure()
    $graphics.FillPath($whiteBrush, $path)

    # Antenna
    $antennaX = [int](236 * $scale)
    $antennaY = [int](100 * $scale)
    $antennaW = [int](40 * $scale)
    $antennaH = [int](50 * $scale)
    $graphics.FillRectangle($whiteBrush, $antennaX, $antennaY, $antennaW, $antennaH)

    # Antenna ball (cyan)
    $cyanBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0, 0)),
        (New-Object System.Drawing.Point($size, $size)),
        [System.Drawing.Color]::FromArgb(255, 79, 172, 254),
        [System.Drawing.Color]::FromArgb(255, 0, 242, 254)
    )
    $ballX = [int](234 * $scale)
    $ballY = [int](63 * $scale)
    $ballSize = [int](44 * $scale)
    $graphics.FillEllipse($cyanBrush, $ballX, $ballY, $ballSize, $ballSize)

    # Eyes outer (gradient)
    $leftEyeX = [int](148 * $scale)
    $leftEyeY = [int](192 * $scale)
    $eyeSize = [int](76 * $scale)
    $graphics.FillEllipse($gradientBrush, $leftEyeX, $leftEyeY, $eyeSize, $eyeSize)

    $rightEyeX = [int](288 * $scale)
    $graphics.FillEllipse($gradientBrush, $rightEyeX, $leftEyeY, $eyeSize, $eyeSize)

    # Eyes inner (dark)
    $darkBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 26, 26, 46))
    $innerEyeSize = [int](56 * $scale)
    $innerOffset = [int](10 * $scale)
    $graphics.FillEllipse($darkBrush, $leftEyeX + $innerOffset, $leftEyeY + $innerOffset, $innerEyeSize, $innerEyeSize)
    $graphics.FillEllipse($darkBrush, $rightEyeX + $innerOffset, $leftEyeY + $innerOffset, $innerEyeSize, $innerEyeSize)

    # Eye highlights (cyan)
    $highlightSize = [int](24 * $scale)
    $highlightOffset = [int](19 * $scale)
    $graphics.FillEllipse($cyanBrush, $leftEyeX + $highlightOffset, $leftEyeY + $highlightOffset - [int](5*$scale), $highlightSize, $highlightSize)
    $graphics.FillEllipse($cyanBrush, $rightEyeX + $highlightOffset, $leftEyeY + $highlightOffset - [int](5*$scale), $highlightSize, $highlightSize)

    # Eye sparkles
    $sparkleSize = [int](10 * $scale)
    $sparkleBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $graphics.FillEllipse($sparkleBrush, $leftEyeX + [int](28*$scale), $leftEyeY + [int](12*$scale), $sparkleSize, $sparkleSize)
    $graphics.FillEllipse($sparkleBrush, $rightEyeX + [int](28*$scale), $leftEyeY + [int](12*$scale), $sparkleSize, $sparkleSize)

    # Mouth
    $mouthX = [int](186 * $scale)
    $mouthY = [int](295 * $scale)
    $mouthW = [int](140 * $scale)
    $mouthH = [int](40 * $scale)
    $mouthRadius = [int](20 * $scale)

    $mouthPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $mouthPath.AddArc($mouthX, $mouthY, $mouthRadius*2, $mouthH, 180, 90)
    $mouthPath.AddArc($mouthX + $mouthW - $mouthRadius*2, $mouthY, $mouthRadius*2, $mouthH, 270, 90)
    $mouthPath.AddArc($mouthX + $mouthW - $mouthRadius*2, $mouthY, $mouthRadius*2, $mouthH, 0, 90)
    $mouthPath.AddArc($mouthX, $mouthY, $mouthRadius*2, $mouthH, 90, 90)
    $mouthPath.CloseFigure()
    $graphics.FillPath($gradientBrush, $mouthPath)

    # Ears
    $earW = [int](25 * $scale)
    $earH = [int](80 * $scale)
    $earY = [int](200 * $scale)
    $graphics.FillRectangle($cyanBrush, [int](96 * $scale), $earY, $earW, $earH)
    $graphics.FillRectangle($cyanBrush, [int](391 * $scale), $earY, $earW, $earH)

    $graphics.Dispose()
    $gradientBrush.Dispose()
    $cyanBrush.Dispose()
    $whiteBrush.Dispose()
    $darkBrush.Dispose()
    $sparkleBrush.Dispose()

    return $bitmap
}

# Generate PNG for various sizes
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path

foreach ($size in $sizes) {
    $bitmap = Create-BotIcon -size $size
    $pngPath = Join-Path $scriptPath "logo_$size.png"
    $bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    Write-Host "Created $pngPath"
}

# Create main logo PNG (512x512)
$mainLogo = Create-BotIcon -size 512
$mainLogoPath = Join-Path $scriptPath "logo.png"
$mainLogo.Save($mainLogoPath, [System.Drawing.Imaging.ImageFormat]::Png)
$mainLogo.Dispose()
Write-Host "Created $mainLogoPath"

# Create ICO file with multiple sizes
$icoPath = Join-Path $scriptPath "app.ico"

# ICO file structure
$iconSizes = @(256, 128, 64, 48, 32, 16)
$images = @()

foreach ($size in $iconSizes) {
    $images += Create-BotIcon -size $size
}

# Write ICO file
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICO Header
$bw.Write([Int16]0)          # Reserved
$bw.Write([Int16]1)          # Type (1 = ICO)
$bw.Write([Int16]$images.Count) # Number of images

# Calculate offsets
$headerSize = 6 + (16 * $images.Count)
$offset = $headerSize

$imageData = @()
foreach ($img in $images) {
    $ms = New-Object System.IO.MemoryStream
    $img.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $imageData += ,($ms.ToArray())
    $ms.Dispose()
}

# Write directory entries
for ($i = 0; $i -lt $images.Count; $i++) {
    $size = $iconSizes[$i]
    $data = $imageData[$i]

    $bw.Write([Byte]$(if ($size -eq 256) { 0 } else { $size })) # Width
    $bw.Write([Byte]$(if ($size -eq 256) { 0 } else { $size })) # Height
    $bw.Write([Byte]0)         # Color palette
    $bw.Write([Byte]0)         # Reserved
    $bw.Write([Int16]1)        # Color planes
    $bw.Write([Int16]32)       # Bits per pixel
    $bw.Write([Int32]$data.Length) # Size of image data
    $bw.Write([Int32]$offset)  # Offset to image data

    $offset += $data.Length
}

# Write image data
foreach ($data in $imageData) {
    $bw.Write($data)
}

$bw.Close()
$fs.Close()

# Cleanup
foreach ($img in $images) {
    $img.Dispose()
}

Write-Host "Created $icoPath"
Write-Host "Icon generation complete!"
