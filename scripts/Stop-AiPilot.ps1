param([string]$PostgresBin = 'C:\Program Files\PostgreSQL\17\bin', [ValidateSet('Synthetic', 'Rehearsal')][string]$Profile = 'Synthetic')
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sharedRoot = Join-Path $workspace '.local\ai-pilot'
$pilotRoot = if ($Profile -eq 'Rehearsal') { Join-Path $workspace '.local\ai-rehearsal' } else { $sharedRoot }
$angularConfiguration = if ($Profile -eq 'Rehearsal') { 'rehearsal' } else { 'pilot' }
$processesPath = Join-Path $pilotRoot 'processes.json'
if (Test-Path -LiteralPath $processesPath) {
    $state = Get-Content -LiteralPath $processesPath -Raw | ConvertFrom-Json
    $apiDll = Join-Path $pilotRoot 'api\MadeInMinas.Api.dll'
    $angularCli = Join-Path $workspace 'frontend\made-in-minas\node_modules\@angular\cli\bin\ng.js'
    foreach ($entry in @(@{ Id = $state.ApiPid; Path = $apiDll; Argument = '' }, @{ Id = $state.FrontendPid; Path = $angularCli; Argument = ('--configuration ' + $angularConfiguration) })) {
        $process = Get-CimInstance Win32_Process -Filter "ProcessId = $($entry.Id)" -ErrorAction SilentlyContinue
        if ($process) {
            if (-not $process.CommandLine.Contains($entry.Path) -or -not $process.CommandLine.Contains($entry.Argument)) {
                throw 'PID reutilizado por outro processo. Nenhum processo desconhecido sera encerrado.'
            }
            Stop-Process -Id $entry.Id
        }
    }
    Remove-Item -LiteralPath $processesPath
}
$otherRoot = if ($Profile -eq 'Rehearsal') { $sharedRoot } else { Join-Path $workspace '.local\ai-rehearsal' }
$otherStatePath = Join-Path $otherRoot 'processes.json'
if (Test-Path -LiteralPath $otherStatePath) {
    $otherState = Get-Content -LiteralPath $otherStatePath -Raw | ConvertFrom-Json
    $otherProcess = Get-CimInstance Win32_Process -Filter "ProcessId = $($otherState.ApiPid)" -ErrorAction SilentlyContinue
    if ($otherProcess -and $otherProcess.CommandLine.Contains((Join-Path $otherRoot 'api\MadeInMinas.Api.dll'))) {
        Write-Host 'Processos do perfil encerrados. PostgreSQL compartilhado preservado para o outro perfil.'
        exit 0
    }
}
$clusterPath = Join-Path $sharedRoot 'postgres'
if (Test-Path -LiteralPath (Join-Path $clusterPath 'postmaster.pid')) {
    & (Join-Path $PostgresBin 'pg_ctl.exe') -D $clusterPath -m fast -w stop *> $null
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL do piloto nao parou. Dados preservados.' }
}
Write-Host ('Perfil ' + $Profile + ' encerrado. Banco e historico preservados.')
