# @file make-app-icon.ps1
# @author: rudals252
# @brief 원본 패키지 이미지(src/NotificationsPro.Package/Images/LargeTile.png)로 다중 크기(16~256px, PNG 압축) app.ico를 만들어
#        src/NotificationsPro/app.ico에 저장한다. csproj의 ApplicationIcon이 이 파일을 참조해 exe 아이콘이 된다. 추가 도구 불필요.
[CmdletBinding()]
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\..\src\NotificationsPro.Package\Images\LargeTile.png'),
    [string]$Destination = (Join-Path $PSScriptRoot '..\..\src\NotificationsPro\app.ico'),
    [int[]]$Sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Source = [IO.Path]::GetFullPath($Source)
$Destination = [IO.Path]::GetFullPath($Destination)
$src = [System.Drawing.Image]::FromFile($Source)
try {
    $entries = foreach ($size in $Sizes) {
        $bmp = New-Object System.Drawing.Bitmap $size, $size
        try {
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $g.Clear([System.Drawing.Color]::Transparent)
                $g.DrawImage($src, 0, 0, $size, $size)
            } finally { $g.Dispose() }
            $ms = New-Object IO.MemoryStream
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            [pscustomobject]@{ Size = $size; Bytes = $ms.ToArray() }
            $ms.Dispose()
        } finally { $bmp.Dispose() }
    }
} finally { $src.Dispose() }

# ICO container: ICONDIR + ICONDIRENTRY[] + image data (PNG entries are valid since Windows Vista).
$fs = [IO.File]::Create($Destination)
try {
    $w = New-Object IO.BinaryWriter $fs
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$entries.Count)
    $offset = 6 + 16 * $entries.Count
    foreach ($e in $entries) {
        $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$e.Bytes.Length); $w.Write([uint32]$offset)
        $offset += $e.Bytes.Length
    }
    foreach ($e in $entries) { $w.Write($e.Bytes) }
    $w.Flush()
} finally { $fs.Dispose() }

"wrote $Destination ($((Get-Item $Destination).Length) bytes, sizes: $($Sizes -join ','))"
