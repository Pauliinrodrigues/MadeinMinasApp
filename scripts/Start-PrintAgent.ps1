param(
    [string]$ConfigurationFile = '',
    [switch]$TestPrint,
    [switch]$Foreground,
    [switch]$Rebuild
)
$ErrorActionPreference = 'Stop'
$printWorkspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$printAgentRoot = Join-Path $printWorkspace '.local\print-agent'
$printAgentBuild = Join-Path $printAgentRoot 'app'
$printAgentStore = Join-Path $printAgentRoot 'station'
New-Item -ItemType Directory -Path $printAgentRoot -Force | Out-Null
$printAgentExe = Join-Path $printAgentBuild 'MadeInMinas.PrintAgent.exe'
if ($Rebuild -or -not (Test-Path -LiteralPath $printAgentExe)) {
    & dotnet publish (Join-Path $printWorkspace 'printing\MadeInMinas.PrintAgent') -c Release -o $printAgentBuild
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o agente de impressão.' }
}
if ($ConfigurationFile) {
    & $printAgentExe --install ([IO.Path]::GetFullPath($ConfigurationFile)) --store $printAgentStore
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao instalar a configuração da estação.' }
}
if ($TestPrint) {
    & $printAgentExe --test-print --store $printAgentStore
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao enviar o teste para a impressora.' }
    exit 0
}
if ($Foreground) {
    & $printAgentExe --store $printAgentStore
    exit $LASTEXITCODE
}
$printAgentProcess = Start-Process -FilePath $printAgentExe -ArgumentList @('--store', ('"' + $printAgentStore + '"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $printAgentRoot 'agent.log') -RedirectStandardError (Join-Path $printAgentRoot 'error.log')
$printAgentProcess.Id | Set-Content -LiteralPath (Join-Path $printAgentRoot 'agent.pid')
Write-Output ('Agente iniciado. PID: ' + $printAgentProcess.Id + '. Acompanhe a conexão em Equipe > Impressão automática.')
