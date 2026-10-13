# 下载项目依赖：ffmpeg 和 MailKit DLL
# 运行：powershell -ExecutionPolicy Bypass -File scripts\download-deps.ps1

$ErrorActionPreference = "Stop"
$RootDir = Split-Path $PSScriptRoot -Parent
$ToolsDir = Join-Path $RootDir "Tools"
if (-not (Test-Path $ToolsDir)) { New-Item -ItemType Directory -Path $ToolsDir | Out-Null }

Write-Host "=== 下载 ffmpeg ==="
$ffmpegZip = Join-Path $env:TEMP "ffmpeg.zip"
$ffmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
Invoke-WebRequest -Uri $ffmpegUrl -OutFile $ffmpegZip -UseBasicParsing
Expand-Archive -Path $ffmpegZip -DestinationPath $env:TEMP\ffmpeg_extract -Force
$ffmpegExe = Get-ChildItem -Path $env:TEMP\ffmpeg_extract -Recurse -Filter "ffmpeg.exe" | Select-Object -First 1
Copy-Item $ffmpegExe.FullName -Destination (Join-Path $ToolsDir "ffmpeg.exe") -Force
Remove-Item $ffmpegZip, "$env:TEMP\ffmpeg_extract" -Recurse -Force
Write-Host "ffmpeg 已下载到: $ToolsDir\ffmpeg.exe"

Write-Host "`n=== 下载 MailKit 依赖 ==="
$mailkitDir = Join-Path $ToolsDir "mailkit"
if (-not (Test-Path $mailkitDir)) { New-Item -ItemType Directory -Path $mailkitDir | Out-Null }

$packages = @{
    "MailKit"                    = "https://www.nuget.org/api/v2/package/MailKit"
    "MimeKit"                    = "https://www.nuget.org/api/v2/package/MimeKit"
    "BouncyCastle.Cryptography"  = "https://www.nuget.org/api/v2/package/BouncyCastle.Cryptography"
    "System.Buffers"             = "https://www.nuget.org/api/v2/package/System.Buffers"
    "System.Formats.Asn1"        = "https://www.nuget.org/api/v2/package/System.Formats.Asn1"
    "System.Memory"              = "https://www.nuget.org/api/v2/package/System.Memory"
    "System.Numerics.Vectors"    = "https://www.nuget.org/api/v2/package/System.Numerics.Vectors"
    "System.Runtime.CompilerServices.Unsafe" = "https://www.nuget.org/api/v2/package/System.Runtime.CompilerServices.Unsafe"
    "System.Threading.Tasks.Extensions" = "https://www.nuget.org/api/v2/package/System.Threading.Tasks.Extensions"
    "System.ValueTuple"          = "https://www.nuget.org/api/v2/package/System.ValueTuple"
}

foreach ($pkg in $packages.Keys) {
    Write-Host "下载 $pkg ..."
    $nupkg = Join-Path $env:TEMP "$pkg.nupkg"
    $extractDir = Join-Path $env:TEMP "$pkg`_extract"
    Invoke-WebRequest -Uri $packages[$pkg] -OutFile $nupkg -UseBasicParsing
    Expand-Archive -Path $nupkg -DestinationPath $extractDir -Force
    $dll = Get-ChildItem -Path $extractDir -Recurse -Filter "$pkg.dll" | Where-Object { $_.FullName -match "net4|netstandard|lib\\net" } | Select-Object -First 1
    if ($dll) {
        Copy-Item $dll.FullName -Destination (Join-Path $mailkitDir "$pkg.dll") -Force
        Write-Host "  -> $pkg.dll"
    } else {
        Write-Host "  警告：未找到 $pkg.dll"
    }
    Remove-Item $nupkg, $extractDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`n=== 依赖下载完成 ==="
Write-Host "ffmpeg: $ToolsDir\ffmpeg.exe"
Write-Host "MailKit DLL: $mailkitDir"
