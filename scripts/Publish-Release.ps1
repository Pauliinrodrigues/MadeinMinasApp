param(
    [string]$Version = ('0.1.0-preview.' + (Get-Date -Format 'yyyyMMddHHmmss')),
    [ValidateSet('Staging', 'Production')][string]$Environment = 'Production'
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$') { throw 'Informe uma versao SemVer sem caminhos.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = Join-Path $repository ('.local\releases\' + $Version + '-' + $Environment.ToLowerInvariant())
if (Test-Path -LiteralPath $destination) { throw 'Versao ja preparada. Artefato anterior preservado.' }
New-Item -ItemType Directory -Path $destination | Out-Null
$commit = & git -c ('safe.directory=' + $repository.Replace('\','/')) -C $repository rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Commit nao identificado.' }
$dirty = & git -c ('safe.directory=' + $repository.Replace('\','/')) -C $repository status --porcelain
$buildVersion = $Version + '+' + $commit.Substring(0,12) + $(if ($dirty) { '.dirty' } else { '' })
try {
    & dotnet publish (Join-Path $repository 'backend\MadeInMinas.Api') --configuration Release --no-self-contained -p:InformationalVersion=$buildVersion -p:IncludeSourceRevisionInInformationalVersion=false --output (Join-Path $destination 'api') *> (Join-Path $destination 'api-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Build da API falhou.' }
    Push-Location (Join-Path $repository 'frontend\made-in-minas')
    try {
        $frontendScript = if ($Environment -eq 'Staging') { 'build:staging' } else { 'build' }
        & npm run $frontendScript *> (Join-Path $destination 'frontend-build.log')
        if ($LASTEXITCODE -ne 0) { throw 'Build do frontend falhou.' }
        Copy-Item -LiteralPath (Join-Path $PWD 'www\browser') -Destination (Join-Path $destination 'frontend') -Recurse
        $licenseFile = Join-Path $PWD 'www\3rdpartylicenses.txt'
        if (Test-Path -LiteralPath $licenseFile) {
            Copy-Item -LiteralPath $licenseFile -Destination (Join-Path $destination 'frontend\3rdpartylicenses.txt')
        }
    } finally { Pop-Location }
    Copy-Item -LiteralPath (Join-Path $repository 'deploy') -Destination (Join-Path $destination 'deploy') -Recurse
    $files = @(Get-ChildItem -LiteralPath $destination -Recurse -File | Where-Object { $_.Extension -ne '.log' } | ForEach-Object {
        @{ path = $_.FullName.Substring($destination.Length + 1).Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    @{ version = $buildVersion; targetEnvironment = $Environment; sourceCommit = $commit; uncommittedChanges = [bool]$dirty; preparedAt = [DateTimeOffset]::UtcNow.ToString('o'); files = $files } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'release.json') -Encoding UTF8
    Write-Output ('Artefato preparado localmente: ' + $destination)
    Write-Output 'Nenhuma implantacao, migration, tag, commit ou push foi executado.'
} catch {
    Write-Error ('Artefato incompleto. Consulte os logs em ' + $destination + '. Nenhum ambiente publicado foi alterado.')
    exit 1
}
