# N225BrokerBridge Webhook テスト一括スクリプト (Stage 1)
#
# 前提:
#   1. 新ブリッジ (N225BrokerBridge.UI.exe) が起動済み
#   2. 設定ダイアログで Webhook パスフレーズ = "abcdefg" (TV 実テンプレと同じ) 保存済み (再起動必須)
#   3. StrategyRegistry に TestStrategy + interval=5 + IsEnabled=true 登録済み
#   4. kabu Station 起動済み (実弾発注テストのケース 3〜5)
#
# 使い方:
#   pwsh -File test_all.ps1                    # ケース 1, 2, 6, 7 (発注なし) を実行
#   pwsh -File test_all.ps1 -IncludeOrder      # ケース 3, 4, 5 (発注あり) も実行
#
# 期待レスポンス (200 + 本文):
#   1. Authenticated_Failed
#   2. (400 Bad Request)
#   3. NewOrderDispatched_     ← kabu に新規買い 1 枚 (実弾)
#   4. ExitOrderDispatched_    ← 建玉なしならエラー、建玉あれば返済
#   5. DotenDispatched_        ← 建玉なしならエラー、short あれば反転
#   6. Ignored_
#   7. Ignored_  (戦略未登録)

param(
    [switch]$IncludeOrder
)

$ErrorActionPreference = 'Continue'
$base = "http://localhost:8000/webhook/"
$payloadsDir = Join-Path $PSScriptRoot "payloads"

function Send-WebhookCase {
    param(
        [string]$Name,
        [string]$File,
        [string]$ExpectStatus = "200",
        [string]$ExpectBody
    )

    $path = Join-Path $payloadsDir $File
    if (-not (Test-Path $path)) {
        Write-Host "  [SKIP] $File not found" -ForegroundColor Yellow
        return
    }
    $body = Get-Content $path -Raw

    Write-Host ""
    Write-Host "─── $Name ───────────────────────────" -ForegroundColor Cyan
    Write-Host "  Payload: $File"
    Write-Host "  Expect : HTTP $ExpectStatus / body contains '$ExpectBody'"

    try {
        $resp = Invoke-WebRequest -Uri $base -Method POST `
            -ContentType "application/json; charset=utf-8" `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) `
            -UseBasicParsing -ErrorAction Stop
        $status = [int]$resp.StatusCode
        $text = $resp.Content
    }
    catch {
        if ($_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
            $stream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($stream)
            $text = $reader.ReadToEnd()
        }
        else {
            Write-Host "  [FAIL] Connection error: $_" -ForegroundColor Red
            return
        }
    }

    $ok = ($status -eq [int]$ExpectStatus) -and ($text -match [Regex]::Escape($ExpectBody))
    $color = if ($ok) { 'Green' } else { 'Red' }
    $mark = if ($ok) { 'PASS' } else { 'FAIL' }
    Write-Host "  [$mark] HTTP $status  body=$text" -ForegroundColor $color
}

Write-Host "==========================================" -ForegroundColor White
Write-Host " N225BrokerBridge Webhook Stage 1 Test"   -ForegroundColor White
Write-Host " URL: $base"                              -ForegroundColor White
Write-Host "==========================================" -ForegroundColor White

# 1. 認証失敗
Send-WebhookCase -Name "1. 認証失敗 (passphrase 不一致)" `
    -File "01_auth_failed.json" -ExpectStatus 200 -ExpectBody "Authenticated_Failed"

# 2. 不正な JSON
Send-WebhookCase -Name "2. JSON パース失敗" `
    -File "02_bad_json.txt" -ExpectStatus 400 -ExpectBody "Bad Request"

# 6. flat→flat (発注なし、未定義遷移)
Send-WebhookCase -Name "6. 未定義遷移 (flat→flat、Ignored)" `
    -File "06_ignored_flat_to_flat.json" -ExpectStatus 200 -ExpectBody "Ignored_"

# 7. 戦略未登録 (発注なし)
Send-WebhookCase -Name "7. 戦略未登録 (Ignored)" `
    -File "07_not_registered.json" -ExpectStatus 200 -ExpectBody "Ignored_"

if ($IncludeOrder) {
    Write-Host ""
    Write-Host "===== 実弾発注ケース (kabu に発注されます) =====" -ForegroundColor Magenta

    # 3. 新規買い
    Send-WebhookCase -Name "3. 新規買い (flat→long)" `
        -File "03_new_buy.json" -ExpectStatus 200 -ExpectBody "NewOrderDispatched_"

    Start-Sleep -Seconds 2

    # 4. 返済 (long→flat)
    Send-WebhookCase -Name "4. 返済 (long→flat)" `
        -File "04_exit_long.json" -ExpectStatus 200 -ExpectBody "ExitOrderDispatched_"

    Start-Sleep -Seconds 2

    # 5. ドテン (short→long)
    Send-WebhookCase -Name "5. ドテン (short→long、建玉 short 1 必要)" `
        -File "05_doten_short_to_long.json" -ExpectStatus 200 -ExpectBody "DotenDispatched_"
}
else {
    Write-Host ""
    Write-Host "実弾発注ケース (3,4,5) はスキップしました。-IncludeOrder で実行できます。" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor White
Write-Host " 完了。ブリッジのログとブリッジ UI の戦略一覧 [最終受信] を確認してください。"
Write-Host "==========================================" -ForegroundColor White
