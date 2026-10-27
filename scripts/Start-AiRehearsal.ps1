param([string]$PostgresBin = 'C:\Program Files\PostgreSQL\17\bin', [ValidateRange(9, 100000)][int]$MaxDailyRequests = 300)
$ErrorActionPreference = 'Stop'
& node (Join-Path $PSScriptRoot 'Capture-AiRehearsal.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Catalogo publico indisponivel. Os processos existentes foram preservados.' }
# Stop only a prior rehearsal; the synthetic pilot and operation remain active.
& (Join-Path $PSScriptRoot 'Stop-AiPilot.ps1') -PostgresBin $PostgresBin -Profile Rehearsal
& (Join-Path $PSScriptRoot 'Start-AiPilot.ps1') -PostgresBin $PostgresBin -Profile Rehearsal -MaxDailyRequests $MaxDailyRequests
