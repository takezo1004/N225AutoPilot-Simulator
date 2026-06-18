# =====================================================
#  N225 Broker Bridge - Installer Build Script
#  使い方:
#    PowerShell から: .\installer\build.ps1
#    (N225BrokerBridge ディレクトリで実行すること)
# =====================================================

$ErrorActionPreference = "Stop"

$scriptDir   = $PSScriptRoot
$projectRoot = Split-Path -Parent $scriptDir
$publishDir  = Join-Path $scriptDir "publish"
$outputDir   = Join-Path $scriptDir "output"
$issFile     = Join-Path $scriptDir "N225BrokerBridge.iss"
$srcProject  = Join-Path $projectRoot "src\N225BrokerBridge.UI\N225BrokerBridge.UI.csproj"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  N225 Broker Bridge - Installer Builder"     -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

# --- 1. 旧 publish フォルダを掃除 ---
if (Test-Path $publishDir) {
    Write-Host "[1/3] 旧 publish フォルダを削除中..." -ForegroundColor Yellow
    Remove-Item $publishDir -Recurse -Force
}

# --- 2. dotnet publish (self-contained, win-x64) ---
Write-Host "[2/3] dotnet publish 実行中..." -ForegroundColor Yellow
dotnet publish $srcProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "publish 失敗。終了します。" -ForegroundColor Red
    exit 1
}

# --- 3. Inno Setup コンパイル ---
Write-Host "[3/3] Inno Setup コンパイル中..." -ForegroundColor Yellow

# iscc.exe のパス候補 (環境により異なる)
$isccCandidates = @(
    "C:\Program Files (x86)\Inno Setup 6\iscc.exe",
    "C:\Program Files\Inno Setup 6\iscc.exe",
    "iscc.exe"
)

$iscc = $null
foreach ($candidate in $isccCandidates) {
    if ($candidate -eq "iscc.exe") {
        if (Get-Command iscc.exe -ErrorAction SilentlyContinue) {
            $iscc = "iscc.exe"
            break
        }
    } elseif (Test-Path $candidate) {
        $iscc = $candidate
        break
    }
}

if (-not $iscc) {
    Write-Host "iscc.exe が見つかりません。Inno Setup 6 をインストール、または PATH を通してください。" -ForegroundColor Red
    exit 1
}

& $iscc $issFile

if ($LASTEXITCODE -ne 0) {
    Write-Host "Inno Setup コンパイル失敗。" -ForegroundColor Red
    exit 1
}

# --- 完了 ---
Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  ビルド完了!" -ForegroundColor Green
Write-Host "  出力: $outputDir" -ForegroundColor Green
Get-ChildItem $outputDir -Filter "*.exe" | ForEach-Object {
    Write-Host "  → $($_.Name) ($('{0:N1}' -f ($_.Length / 1MB)) MB)" -ForegroundColor Green
}
Write-Host "============================================" -ForegroundColor Green
