# Run:  powershell -NoProfile -Command "Invoke-Pester <repo>\tests\tools"
# Pester 3.4 / Windows PowerShell 5.1. Runs tools\Check-CommandLog.ps1 as a child process on small
# synthetic logs and checks the exit code plus the -ResultPath JSON.
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:checker = (Resolve-Path (Join-Path $here '..\..\tools\Check-CommandLog.ps1')).Path

function New-TempDir {
    $d = Join-Path ([IO.Path]::GetTempPath()) ('cmdlog-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $d | Out-Null
    $d
}

# A three-day WarSails log that satisfies every log-checkable criterion.
function Get-WarSailsLogLines([string]$ChainOfCommand = 'True') {
    @(
        "[Init] CoherentWarAI active. Target de-greed: True, marshal doctrine: True, chain of command: $ChainOfCommand"
        '[Command] Command chain: TargetScore->NavalTargetScoreCalculatingModel, Garrison->NavalDLCSettlementGarrisonModel, Army->NavalDLCArmyManagementCalculationModel'
        '=== Year 1084, Summer 1 - war posture ==='
        '[Contention] 0 parties written by more than one subsystem today (tracked 0 parties)'
        '[Posture] Vlandia: 10 parties, 2 war(s), 0 % of realm threatened -> 3 may attack, 7 defend'
        '[Posture]   MARSHAL: Cedd (strength 200, valor 0)'
        '[Command] Vlandia delegates Naval to Aldric'
        '[Command] Vlandia delegates Defense to Bern'
        '[Command] Vlandia delegates Offense to Cedd'
        '[Command] Aldric orders Dorn to patrol (10, 20) near Ostican'
        '=== Year 1084, Summer 2 - war posture ==='
        '[Contention] 0 parties written by more than one subsystem today (tracked 18 parties)'
        '=== Year 1084, Summer 3 - war posture ==='
        '[Contention] 0 parties written by more than one subsystem today (tracked 20 parties)'
    )
}

function Invoke-Checker([string]$LogPath, [string]$Mode, [string]$ResultPath) {
    $null = & powershell -NoProfile -File $script:checker -LogPath $LogPath -Mode $Mode -MinDays 3 -ResultPath $ResultPath
    $LASTEXITCODE
}

Describe 'Check-CommandLog -ResultPath' {
    $dir = New-TempDir
    $log = Join-Path $dir 'CoherentWarAI.log'
    Set-Content -LiteralPath $log -Value (Get-WarSailsLogLines) -Encoding UTF8

    It 'passes a complete WarSails log with exit 0 and writes all 8 rows' {
        $json = Join-Path $dir 'ws.json'
        Invoke-Checker $log 'WarSails' $json | Should Be 0
        $doc = Get-Content -LiteralPath $json -Raw | ConvertFrom-Json
        $doc.schema | Should Be 1
        $doc.mode | Should Be 'WarSails'
        $doc.days | Should Be 3
        @($doc.rows).Count | Should Be 8
        (@($doc.rows | ForEach-Object { $_.ac }) -join ',') | Should Be 'AC1,AC10,AC9,AC20,AC21,AC13/AC23,AC26,AC25a'
        @($doc.rows | Where-Object { $_.verdict -eq 'FAIL' }).Count | Should Be 0
        @($doc.rows | Where-Object { $_.ac -eq 'AC13/AC23' })[0].verdict | Should Be 'PASS'
    }

    It 'points the AC25a row at CW-1' {
        $json = Join-Path $dir 'ws-ac25a.json'
        $null = Invoke-Checker $log 'WarSails' $json
        $row = @((Get-Content -LiteralPath $json -Raw | ConvertFrom-Json).rows | Where-Object { $_.ac -eq 'AC25a' })[0]
        $row.verdict | Should Be 'MANUAL'
        $row.evidence | Should Match 'CW-1'
        $row.evidence | Should Match 'Compare-Ac25a\.ps1'
    }

    It 'fails AC10 with exit 1 when a WarSails log is checked as FeatureOff (counter-check)' {
        $json = Join-Path $dir 'fo.json'
        Invoke-Checker $log 'FeatureOff' $json | Should Be 1
        $doc = Get-Content -LiteralPath $json -Raw | ConvertFrom-Json
        $doc.mode | Should Be 'FeatureOff'
        @($doc.rows).Count | Should Be 8
        @($doc.rows | Where-Object { $_.ac -eq 'AC10' })[0].verdict | Should Be 'FAIL'
    }

    It 'passes AC10 for a silent FeatureOff log' {
        $foLog = Join-Path $dir 'fo.log'
        Set-Content -LiteralPath $foLog -Encoding UTF8 -Value @(
            '[Init] CoherentWarAI active. Target de-greed: True, chain of command: False'
            '[Command] Command chain: TargetScore->NavalTargetScoreCalculatingModel, Garrison->NavalDLCSettlementGarrisonModel, Army->NavalDLCArmyManagementCalculationModel'
            '=== Year 1084, Summer 1 - war posture ==='
            '[Contention] 0 parties written by more than one subsystem today (tracked 0 parties)'
        )
        $json = Join-Path $dir 'fo-silent.json'
        Invoke-Checker $foLog 'FeatureOff' $json | Should Be 0
        @((Get-Content -LiteralPath $json -Raw | ConvertFrom-Json).rows | Where-Object { $_.ac -eq 'AC10' })[0].verdict | Should Be 'PASS'
    }

    It 'still exits 0 and writes no file when -ResultPath is omitted' {
        $null = & powershell -NoProfile -File $script:checker -LogPath $log -Mode WarSails -MinDays 3
        $LASTEXITCODE | Should Be 0
        @(Get-ChildItem -LiteralPath $dir -Filter '*.json' | Where-Object { $_.Name -notmatch '^(ws|ws-ac25a|fo|fo-silent)\.json$' }).Count | Should Be 0
    }

    It 'exits 1 without writing JSON when the log is missing' {
        $json = Join-Path $dir 'missing.json'
        Invoke-Checker (Join-Path $dir 'nope.log') 'WarSails' $json | Should Be 1
        Test-Path -LiteralPath $json | Should Be $false
    }

    try { [IO.Directory]::Delete($dir, $true) } catch { }
}
