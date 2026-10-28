param(
    [Parameter(Mandatory=$true)][string]$AgentPath,
    [Parameter(Mandatory=$true)][string]$StorePath
)
$ErrorActionPreference = 'Stop'
$executable = (Resolve-Path -LiteralPath $AgentPath -ErrorAction Stop).Path
$store = (Resolve-Path -LiteralPath $StorePath -ErrorAction Stop).Path
if ([IO.Path]::GetFileName($executable) -ne 'MadeInMinas.PrintAgent.exe') { throw 'Executavel invalido.' }
# Scheduled Task starts this PowerShell hidden; the console agent inherits it.
& $executable --store $store
exit $LASTEXITCODE
