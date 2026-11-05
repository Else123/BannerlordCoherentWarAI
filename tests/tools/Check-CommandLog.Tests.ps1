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

    # FeatureOff = marshal doctrine off only; chain of command and naval orders stay on.
    function New-FeatureOffLog([string]$Name, [string[]]$Lines) {
        $path = Join-Path $dir $Name
        Set-Content -LiteralPath $path -Encoding UTF8 -Value $Lines
        $path
    }
    function Get-Row([string]$Json, [string]$Ac) {
        @((Get-Content -LiteralPath $Json -Raw | ConvertFrom-Json).rows | Where-Object { $_.ac -eq $Ac })[0]
    }
    $foInit = '[Init] CoherentWarAI active. Target de-greed: True, marshal doctrine: False, chain of command: True'
    $foChain = '[Command] Command chain: TargetScore->NavalTargetScoreCalculatingModel, Garrison->NavalDLCSettlementGarrisonModel, Army->NavalDLCArmyManagementCalculationModel'

    It 'passes AC10 for a FeatureOff log: doctrine off, chain on' {
        $foLog = New-FeatureOffLog 'fo-ok.log' @(
            $foInit, $foChain
            '=== Year 1084, Summer 1 - war posture ==='
            '[Contention] 0 parties written by more than one subsystem today (tracked 0 parties)'
            '[Posture] Vlandia: 10 parties, 2 war(s), 0 % of realm threatened -> 3 may attack, 7 defend'
            '[Posture]   attacks: Cedd (strength 200, valor 0)'
            '[Command] Vlandia delegates Naval to Aldric'
            '[Command] Vlandia delegates Defense to Bern'
            '[Command] Aldric orders Dorn to patrol (10, 20) near Ostican'
        )
        $json = Join-Path $dir 'fo-ok.json'
        Invoke-Checker $foLog 'FeatureOff' $json | Should Be 0
        (Get-Row $json 'AC10').verdict | Should Be 'PASS'
    }

    It 'fails AC10 for FeatureOff when the init line still says marshal doctrine: True' {
        $foLog = New-FeatureOffLog 'fo-doc-on.log' @('[Init] CoherentWarAI active. Target de-greed: True, marshal doctrine: True, chain of command: True', $foChain, '=== Year 1084, Summer 1 - war posture ===')
        $json = Join-Path $dir 'fo-doc-on.json'
        Invoke-Checker $foLog 'FeatureOff' $json | Should Be 1
        (Get-Row $json 'AC10').verdict | Should Be 'FAIL'
    }

    It 'fails AC10 for FeatureOff when the chain of command is off (the old FeatureOff meaning)' {
        $foLog = New-FeatureOffLog 'fo-chain-off.log' @('[Init] CoherentWarAI active. Target de-greed: True, marshal doctrine: False, chain of command: False', '=== Year 1084, Summer 1 - war posture ===')
        $json = Join-Path $dir 'fo-chain-off.json'
        Invoke-Checker $foLog 'FeatureOff' $json | Should Be 1
        (Get-Row $json 'AC10').verdict | Should Be 'FAIL'
    }

    It 'does not assert on MARSHAL lines or Offense delegations in FeatureOff (host leaders stay marshals)' {
        $foLog = New-FeatureOffLog 'fo-hosts.log' @($foInit, $foChain, '=== Year 1084, Summer 1 - war posture ===', '[Posture]   MARSHAL: Cedd (strength 200, valor 0)', '[Command] Vlandia delegates Offense to Cedd', '[Command] Vlandia delegates Naval to Aldric', '[Command] Aldric orders Dorn to patrol (10, 20) near Ostican')
        $json = Join-Path $dir 'fo-hosts.json'
        Invoke-Checker $foLog 'FeatureOff' $json | Should Be 0
        (Get-Row $json 'AC10').verdict | Should Be 'PASS'
    }

    It 'judges AC10 on the "Settings in effect" line when it follows the startup line' {
        $foLog = New-FeatureOffLog 'fo-restated.log' @(
            '[Init] CoherentWarAI active. Target de-greed: True, marshal doctrine: True, chain of command: True'
            '[Init] Settings in effect. Target de-greed: True, marshal doctrine: False, chain of command: True, naval orders: True'
            $foChain, '=== Year 1084, Summer 1 - war posture ==='
            '[Command] Vlandia delegates Naval to Aldric'
            '[Command] Aldric orders Dorn to patrol (10, 20) near Ostican'
        )
        $json = Join-Path $dir 'fo-restated.json'
        Invoke-Checker $foLog 'FeatureOff' $json | Should Be 0
        (Get-Row $json 'AC10').verdict | Should Be 'PASS'
    }

    It 'applies AC1, AC9, AC20 (Defense only) and AC21 to FeatureOff because chain and War Sails are on' {
        $lines = @($foInit, $foChain)
        foreach ($d in 1..5) {
            $lines += "=== Year 1084, Summer $d - war posture ==="
            $lines += "[Contention] 0 parties written by more than one subsystem today (tracked $($d * 3) parties)"
            if ($d -eq 1) {
                $lines += '[Command] Vlandia delegates Naval to Aldric'
                $lines += '[Command] Vlandia delegates Defense to Bern'
                $lines += '[Command] Aldric orders Dorn to patrol (10, 20) near Ostican'
            }
        }
        $foLog = New-FeatureOffLog 'fo-full.log' $lines
        $json = Join-Path $dir 'fo-full.json'
        Invoke-Checker $foLog 'FeatureOff' $json | Should Be 0
        foreach ($ac in 'AC1', 'AC9', 'AC10', 'AC20', 'AC21', 'AC13/AC23') {
            (Get-Row $json $ac).verdict | Should Be 'PASS'
        }
    }

    It 'fails AC20 for FeatureOff when no Defense delegation appears within 5 days' {
        $lines = @($foInit, $foChain)
        foreach ($d in 1..5) { $lines += "=== Year 1084, Summer $d - war posture ===" }
        $foLog = New-FeatureOffLog 'fo-nodef.log' $lines
        $json = Join-Path $dir 'fo-nodef.json'
        Invoke-Checker $foLog 'FeatureOff' $json | Should Be 1
        (Get-Row $json 'AC20').verdict | Should Be 'FAIL'
    }

    # AllOff = vanilla-like baseline: marshal doctrine, chain of command and naval orders off.
    $aoSettings = '[Init] Settings in effect. Target de-greed: True, marshal doctrine: False, chain of command: False, naval orders: False'
    function Get-AllOffLogLines {
        @(
            '[Init] CoherentWarAI active. Target de-greed: True, marshal doctrine: True, chain of command: True'
            $aoSettings
            $foChain
            '=== Year 1084, Summer 1 - war posture ==='
            '[Contention] 0 parties written by more than one subsystem today (tracked 0 parties)'
            '[Posture] Vlandia: 10 parties, 2 war(s), 0 % of realm threatened -> 3 may attack, 7 defend'
            '=== Year 1084, Summer 2 - war posture ==='
            '=== Year 1084, Summer 3 - war posture ==='
        )
    }

    It 'passes AC10 for an AllOff log: doctrine, chain and naval orders off, no daily [Command] lines' {
        $aoLog = New-FeatureOffLog 'ao-ok.log' (Get-AllOffLogLines)
        $json = Join-Path $dir 'ao-ok.json'
        Invoke-Checker $aoLog 'AllOff' $json | Should Be 0
        $doc = Get-Content -LiteralPath $json -Raw | ConvertFrom-Json
        $doc.mode | Should Be 'AllOff'
        @($doc.rows).Count | Should Be 8
        (Get-Row $json 'AC10').verdict | Should Be 'PASS'
    }

    It 'reports the chain-/naval-dependent criteria as N/A for AllOff' {
        $aoLog = New-FeatureOffLog 'ao-na.log' (Get-AllOffLogLines)
        $json = Join-Path $dir 'ao-na.json'
        $null = Invoke-Checker $aoLog 'AllOff' $json
        foreach ($ac in 'AC1/AC2', 'AC9', 'AC20', 'AC21', 'AC13/AC23') {
            (Get-Row $json $ac).verdict | Should Be 'N/A for mode'
        }
    }

    It 'passes AC10 for AllOff on a startup line that does not print naval orders' {
        $aoLog = New-FeatureOffLog 'ao-startup.log' @('[Init] CoherentWarAI active. Target de-greed: True, marshal doctrine: False, chain of command: False', '=== Year 1084, Summer 1 - war posture ===')
        $json = Join-Path $dir 'ao-startup.json'
        Invoke-Checker $aoLog 'AllOff' $json | Should Be 0
        (Get-Row $json 'AC10').verdict | Should Be 'PASS'
    }

    It 'fails AC10 for AllOff when a FeatureOff log (chain of command on) is checked' {
        $aoLog = New-FeatureOffLog 'ao-chain-on.log' @('[Init] Settings in effect. Target de-greed: True, marshal doctrine: False, chain of command: True, naval orders: False', '=== Year 1084, Summer 1 - war posture ===')
        $json = Join-Path $dir 'ao-chain-on.json'
        Invoke-Checker $aoLog 'AllOff' $json | Should Be 1
        (Get-Row $json 'AC10').verdict | Should Be 'FAIL'
    }

    It 'fails AC10 for AllOff when the init line says marshal doctrine: True' {
        $aoLog = New-FeatureOffLog 'ao-doc-on.log' @('[Init] Settings in effect. Target de-greed: True, marshal doctrine: True, chain of command: False, naval orders: False', '=== Year 1084, Summer 1 - war posture ===')
        $json = Join-Path $dir 'ao-doc-on.json'
        Invoke-Checker $aoLog 'AllOff' $json | Should Be 1
        (Get-Row $json 'AC10').verdict | Should Be 'FAIL'
    }

    It 'fails AC10 for AllOff when the "Settings in effect" line says naval orders: True' {
        $aoLog = New-FeatureOffLog 'ao-naval-on.log' @('[Init] Settings in effect. Target de-greed: True, marshal doctrine: False, chain of command: False, naval orders: True', '=== Year 1084, Summer 1 - war posture ===')
        $json = Join-Path $dir 'ao-naval-on.json'
        Invoke-Checker $aoLog 'AllOff' $json | Should Be 1
        (Get-Row $json 'AC10').verdict | Should Be 'FAIL'
    }

    It 'fails AC10 for AllOff when a daily [Command] delegation appears despite the chain being off' {
        $aoLog = New-FeatureOffLog 'ao-daily.log' @($aoSettings, '=== Year 1084, Summer 1 - war posture ===', '[Command] Vlandia delegates Defense to Bern')
        $json = Join-Path $dir 'ao-daily.json'
        Invoke-Checker $aoLog 'AllOff' $json | Should Be 1
        (Get-Row $json 'AC10').verdict | Should Be 'FAIL'
    }

    It 'fails AC10 for AllOff when a daily patrol order with hero ids appears' {
        $aoLog = New-FeatureOffLog 'ao-order-ids.log' @($aoSettings, '=== Year 1084, Summer 1 - war posture ===', '[Command] Aldric [id=lord_1] orders Dorn [id=lord_2] to patrol (10, 20) near Ostican')
        $json = Join-Path $dir 'ao-order-ids.json'
        Invoke-Checker $aoLog 'AllOff' $json | Should Be 1
        (Get-Row $json 'AC10').verdict | Should Be 'FAIL'
    }

    It 'fails AC10 when an AllOff log is checked as FeatureOff (counter-check)' {
        $aoLog = New-FeatureOffLog 'ao-as-fo.log' (Get-AllOffLogLines)
        $json = Join-Path $dir 'ao-as-fo.json'
        Invoke-Checker $aoLog 'FeatureOff' $json | Should Be 1
        (Get-Row $json 'AC10').verdict | Should Be 'FAIL'
    }

    It 'compares hero names in AC26 regardless of a trailing [id=...] on some line types' {
        $idLog = Join-Path $dir 'ws-ids.log'
        Set-Content -LiteralPath $idLog -Encoding UTF8 -Value @(
            '[Init] CoherentWarAI active. Target de-greed: True, marshal doctrine: True, chain of command: True'
            '[Command] Command chain: TargetScore->NavalTargetScoreCalculatingModel, Garrison->NavalDLCSettlementGarrisonModel, Army->NavalDLCArmyManagementCalculationModel'
            '=== Year 1084, Summer 1 - war posture ==='
            '[Posture] Vlandia: 10 parties, 2 war(s), 0 % of realm threatened -> 3 may attack, 7 defend'
            '[Posture]   MARSHAL: Dorn [id=lord_2_5] (strength 200, valor 0)'
            '[Command] Vlandia delegates Naval to Aldric [id=lord_2_1]'
            '[Command] Aldric orders Dorn to patrol (10, 20) near Ostican'
        )
        $json = Join-Path $dir 'ws-ids.json'
        $null = Invoke-Checker $idLog 'WarSails' $json
        (Get-Row $json 'AC26').verdict | Should Be 'WARN - verify fallback'
    }

    It 'passes the complete WarSails log when every hero carries [id=...] as the mod writes it' {
        $idLog = Join-Path $dir 'ws-ids.log'
        $lines = Get-WarSailsLogLines | ForEach-Object {
            $_ -replace '^(\[Posture\]\s+MARSHAL: \w+) \(', '$1 [id=lord_m] (' `
               -replace '^(\[Command\] .+ delegates \w+ to \w+)$', '$1 [id=lord_h]' `
               -replace '^(\[Command\] \w+) orders (\w+) to patrol', '$1 [id=lord_h] orders $2 [id=lord_o] to patrol'
        }
        @($lines | Where-Object { $_ -match '\[id=' }).Count | Should Be 5
        Set-Content -LiteralPath $idLog -Encoding UTF8 -Value $lines
        $json = Join-Path $dir 'ws-ids.json'
        Invoke-Checker $idLog 'WarSails' $json | Should Be 0
        $doc = Get-Content -LiteralPath $json -Raw | ConvertFrom-Json
        @($doc.rows | Where-Object { $_.verdict -eq 'FAIL' }).Count | Should Be 0
        @($doc.rows | Where-Object { $_.ac -eq 'AC13/AC23' })[0].verdict | Should Be 'PASS'
    }

    It 'still exits 0 and writes no file when -ResultPath is omitted' {
        $null = & powershell -NoProfile -File $script:checker -LogPath $log -Mode WarSails -MinDays 3
        $LASTEXITCODE | Should Be 0
        @(Get-ChildItem -LiteralPath $dir -Filter '*.json' | Where-Object { $_.Name -notmatch '^(ws|ws-ac25a|ws-ids|fo|fo-[a-z0-9-]+|ao-[a-z0-9-]+)\.json$' }).Count | Should Be 0
    }

    It 'exits 1 without writing JSON when the log is missing' {
        $json = Join-Path $dir 'missing.json'
        Invoke-Checker (Join-Path $dir 'nope.log') 'WarSails' $json | Should Be 1
        Test-Path -LiteralPath $json | Should Be $false
    }

    try { [IO.Directory]::Delete($dir, $true) } catch { }
}
