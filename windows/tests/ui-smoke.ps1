param([Parameter(Mandatory=$true)][string]$AppPath, [string]$OutputDirectory = "$PSScriptRoot/artifacts")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
$originalData = $env:AGENTWATCH_DATA_DIR
$temporary = Join-Path $env:TEMP ('agentwatch-ui-' + [guid]::NewGuid().ToString('N'))
$env:AGENTWATCH_DATA_DIR = $temporary
$process = $null
function Find-Control([string]$Name) {
    return $script:window.FindFirst([Windows.Automation.TreeScope]::Descendants,
        (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $Name)))
}
function Click-Control([string]$Name) {
    $control = Find-Control $Name
    if (-not $control) { throw "Missing control: $Name" }
    $control.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Wait-Control([string]$Name) {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do { $found = Find-Control $Name; if ($found -and -not $found.Current.IsOffscreen) { return $found }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
    throw "Control did not become visible: $Name"
}
try {
    $process = Start-Process (Resolve-Path $AppPath) -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do { $process.Refresh(); if ($process.MainWindowHandle -ne 0) { break }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowHandle -eq 0) { throw 'App did not open a native window.' }
    $script:window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    Wait-Control 'Bắt đầu với Agent Watch' | Out-Null
    # Wait for the startup status check to release the controls.
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do { $next = Find-Control 'Tiếp tục →'; if ($next.Current.IsEnabled) { break }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
    Click-Control 'Tiếp tục →'
    Wait-Control 'Kết nối đúng Studio, đúng key' | Out-Null
    if ((Find-Control 'Tiếp tục →').Current.IsEnabled) { throw 'Unverified key allowed advancing.' }
    $radio = Find-Control 'Địa chỉ + key riêng'
    $radio.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Wait-Control 'Địa chỉ API Studio' | Out-Null
    Click-Control 'Kiểm tra kết nối & key'
    Start-Sleep -Milliseconds 300
    if ((Find-Control 'Tiếp tục →').Current.IsEnabled) { throw 'Empty key allowed advancing.' }
    if (Test-Path (Join-Path $temporary 'profiles.json')) { throw 'Key verification unexpectedly saved a profile.' }
    New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
    $bounds = $script:window.Current.BoundingRectangle
    $bitmap = New-Object Drawing.Bitmap([int]$bounds.Width, [int]$bounds.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen([int]$bounds.X, [int]$bounds.Y, 0, 0, $bitmap.Size)
        $bitmap.Save((Join-Path (Resolve-Path $OutputDirectory) 'windows-key-step.png'), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
    Click-Control 'Quay lại'
    Wait-Control 'Bắt đầu với Agent Watch' | Out-Null
    # The support check draws its report in another font with ✓ and ✗: the
    # app must still be there once it has ended (no key, maybe no WSL here).
    Click-Control 'Chẩn đoán'
    Wait-Control 'Tình trạng trên máy' | Out-Null
    Click-Control 'Kiểm tra log'
    $report = Wait-Control 'Báo cáo kiểm tra log'
    $deadline = [DateTime]::UtcNow.AddSeconds(300)
    do {
        Start-Sleep -Milliseconds 500
        $process.Refresh(); if ($process.HasExited) { throw 'The app closed while checking the log.' }
        $text = $report.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
    } while (($text -notmatch 'AGENT WATCH' -or $text.StartsWith('Đang kiểm tra')) -and [DateTime]::UtcNow -lt $deadline)
    if ($text -notmatch 'Có lỗi ở: ') { throw "The support check did not report a machine without a connection: $text" }
    Start-Sleep -Seconds 2
    $process.Refresh(); if ($process.HasExited) { throw 'The app closed after drawing the support report.' }
    if (-not (Select-String -Path (Join-Path $temporary 'support-check.txt') -Pattern 'AGENT WATCH' -Quiet)) { throw 'The support report was not saved.' }
    if (Test-Path (Join-Path $temporary 'crash.log')) { throw "The app logged an unhandled error: $(Get-Content (Join-Path $temporary 'crash.log') -Raw)" }
    Write-Host 'PASS: native wizard navigation, key entry options, verification gate, no credential persistence, and a support check that reports and survives.'
} finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    $env:AGENTWATCH_DATA_DIR = $originalData
    if (Test-Path $temporary) { Remove-Item $temporary -Recurse -Force }
}
