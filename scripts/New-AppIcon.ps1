param([string]$Output = "$PSScriptRoot/../src/Templator/Assets/Templator.ico")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$frames = @()
foreach ($size in @(16, 32, 48, 256)) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $background = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#B8EBCF'))
    $ink = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#15291F'))
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $radius = [single]($size * 0.4)
    $edge = [single]($size - 1)
    $path.AddArc(0, 0, $radius, $radius, 180, 90)
    $path.AddArc(($edge - $radius), 0, $radius, $radius, 270, 90)
    $path.AddArc(($edge - $radius), ($edge - $radius), $radius, $radius, 0, 90)
    $path.AddArc(0, ($edge - $radius), $radius, $radius, 90, 90)
    $path.CloseFigure()
    $graphics.FillPath($background, $path)
    $font = [System.Drawing.Font]::new('Segoe UI', [single]($size * 0.75), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $graphics.DrawString('t.', $font, $ink, [single]($size * 0.07), [single](-$size * 0.06))
    $memory = [System.IO.MemoryStream]::new()
    $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@{ Size = $size; Bytes = $memory.ToArray() }
    $memory.Dispose(); $font.Dispose(); $path.Dispose(); $background.Dispose(); $ink.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$stream = [System.IO.File]::Create([System.IO.Path]::GetFullPath($Output))
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $stream.Dispose() }
