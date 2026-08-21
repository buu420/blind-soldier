param(
    [Parameter(Mandatory = $true)]
    [string] $InputPath,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [int] $Scale = 1
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $InputPath))
if ($bytes.Length -lt 0xEC -or [BitConverter]::ToInt32($bytes, 0) -ne 1) {
    throw 'Not an FF7 PC TEX v1 file.'
}

$width = [BitConverter]::ToInt32($bytes, 0x3C)
$height = [BitConverter]::ToInt32($bytes, 0x40)
$palettePresent = [BitConverter]::ToInt32($bytes, 0x4C)
$bitsPerIndex = [BitConverter]::ToInt32($bytes, 0x50)
$paletteEntries = [BitConverter]::ToInt32($bytes, 0x58)

if ($palettePresent -eq 0 -or $bitsPerIndex -ne 8) {
    throw "This focused decoder expects an 8-bit indexed FF7 TEX. palette=$palettePresent bitsPerIndex=$bitsPerIndex"
}

$paletteOffset = 0xEC
$pixelOffset = $paletteOffset + ($paletteEntries * 4)
$requiredLength = $pixelOffset + ($width * $height)
if ($width -le 0 -or $height -le 0 -or $requiredLength -gt $bytes.Length) {
    throw "Invalid TEX dimensions or truncated data: ${width}x${height}, need $requiredLength bytes, have $($bytes.Length)."
}

$bitmap = [System.Drawing.Bitmap]::new($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
try {
    for ($y = 0; $y -lt $height; $y++) {
        for ($x = 0; $x -lt $width; $x++) {
            $paletteIndex = $bytes[$pixelOffset + ($y * $width) + $x]
            if ($paletteIndex -ge $paletteEntries) {
                throw "Pixel palette index $paletteIndex exceeds $paletteEntries entries."
            }

            $colorOffset = $paletteOffset + ($paletteIndex * 4)
            $blue = $bytes[$colorOffset]
            $green = $bytes[$colorOffset + 1]
            $red = $bytes[$colorOffset + 2]
            $alpha = $bytes[$colorOffset + 3]
            if ($alpha -eq 0) {
                $alpha = 255
            }
            $bitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($alpha, $red, $green, $blue))
        }
    }

    $resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
    if ($Scale -le 1) {
        $bitmap.Save($resolvedOutput, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    else {
        $scaled = [System.Drawing.Bitmap]::new($width * $Scale, $height * $Scale)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($scaled)
            try {
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
                $graphics.DrawImage($bitmap, 0, 0, $scaled.Width, $scaled.Height)
            }
            finally {
                $graphics.Dispose()
            }
            $scaled.Save($resolvedOutput, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $scaled.Dispose()
        }
    }
}
finally {
    $bitmap.Dispose()
}

Write-Output "Decoded ${width}x${height}, paletteEntries=$paletteEntries, pixelOffset=0x$($pixelOffset.ToString('X')) -> $OutputPath"
