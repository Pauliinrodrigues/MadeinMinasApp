param([string]$Version = ('0.1.0-preview.' + (Get-Date -Format 'yyyyMMddHHmmss')))
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$') { throw 'Informe uma versao SemVer sem caminhos.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = Join-Path $repository ('.local\releases\print-agent-' + $Version)
if (Test-Path -LiteralPath $destination) { throw 'Versao ja preparada. Arquivos preservados.' }
New-Item -ItemType Directory -Path $destination | Out-Null
& dotnet publish (Join-Path $repository 'printing\MadeInMinas.PrintAgent') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:InformationalVersion=$Version ('-p:NuGetLockFilePath=' + (Join-Path $destination 'packages.lock.json')) -o $destination *> (Join-Path $destination 'build.log')
if ($LASTEXITCODE -ne 0) { throw 'Publicacao do agente falhou. Consulte build.log.' }
foreach ($name in @('Install-PrintAgentStartup.ps1','Run-InstalledPrintAgent.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $destination $name)
}
$executable = Join-Path $destination 'MadeInMinas.PrintAgent.exe'
& $executable --verify (Join-Path $destination 'verification.png') --store (Join-Path $destination 'verification') *> (Join-Path $destination 'verification.log')
if ($LASTEXITCODE -ne 0) { throw 'Verificacao do layout falhou. Nenhum papel enviado.' }
@{ version=$Version; runtime='win-x64'; selfContained=$true; sha256=(Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash; preparedAt=[DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'release.json') -Encoding UTF8
Write-Output ('Agente preparado e layout validado sem impressao fisica: '+$destination)
