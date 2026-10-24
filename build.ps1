$ErrorActionPreference = 'Stop'
$rootPath = $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force -Path (Join-Path $rootPath 'assets') | Out-Null
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap 64,64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(109,225,184))
$graphics.FillEllipse($brush, 2, 2, 60, 60)
$font = New-Object System.Drawing.Font 'Segoe UI',36,([System.Drawing.FontStyle]::Bold),([System.Drawing.GraphicsUnit]::Pixel)
$textBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(23,27,34))
$graphics.DrawString('C', $font, $textBrush, 14, 9)
$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$stream = [System.IO.File]::Create((Join-Path $rootPath 'assets\app.ico'))
try { $icon.Save($stream) } finally { $stream.Dispose(); $graphics.Dispose(); $brush.Dispose(); $textBrush.Dispose(); $font.Dispose(); $bitmap.Dispose() }
$sources = Get-ChildItem -LiteralPath (Join-Path $rootPath 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $compilerPath /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/out:$rootPath\CodexQuotaTaskbar.exe" "/win32manifest:$rootPath\app.manifest" "/win32icon:$rootPath\assets\app.ico" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:Microsoft.CSharp.dll $sources
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
Get-Item -LiteralPath (Join-Path $rootPath 'CodexQuotaTaskbar.exe') | Select-Object FullName,Length,LastWriteTime
