param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\17\bin',
    [string]$BackupDirectory = '.local\backups',
    [string]$ImagesDirectory = 'backend\MadeInMinas.Api\App_Data\product-images',
    [string]$KeyRingDirectory = (Join-Path $env:LOCALAPPDATA 'ASP.NET\DataProtection-Keys')
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$previousPassword = $env:PGPASSWORD
$stage = 'configuration'
try {
    $settings = Get-Content -Raw -LiteralPath (Join-Path $env:APPDATA 'Microsoft\UserSecrets\made-in-minas-api-development\secrets.json') | ConvertFrom-Json
    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection.set_ConnectionString([string]$settings.'ConnectionStrings:DefaultConnection')
    if ($connection.get_Item('Host') -notin @('localhost','127.0.0.1') -or $connection.get_Item('Database') -ne 'made_in_minas' -or ($connection.ContainsKey('Port') -and [int]$connection.get_Item('Port') -ne 5432)) { throw 'Banco local nao confirmado.' }
    $destination = Join-Path ([IO.Path]::GetFullPath((Join-Path $repository $BackupDirectory))) (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
    New-Item -ItemType Directory -Path $destination -ErrorAction Stop | Out-Null
    $stage = 'database'
    $env:PGPASSWORD = [string]$connection.get_Item('Password')
    $dump = Join-Path $destination 'database.dump'
    & (Join-Path $PostgresBin 'pg_dump.exe') -w -h 127.0.0.1 -p 5432 -U ([string]$connection.get_Item('Username')) -d made_in_minas -Fc -f $dump 2> (Join-Path $destination 'database-errors.log')
    if ($LASTEXITCODE -ne 0) { throw 'Backup do banco falhou.' }
    & (Join-Path $PostgresBin 'pg_restore.exe') -l $dump > (Join-Path $destination 'database-contents.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Arquivo de banco invalido.' }
    $stage = 'files'
    $images = [IO.Path]::GetFullPath((Join-Path $repository $ImagesDirectory))
    if (-not (Test-Path -LiteralPath $images -PathType Container)) { throw 'Pasta de imagens ausente. Informe ImagesDirectory.' }
    if (-not (Test-Path -LiteralPath $KeyRingDirectory -PathType Container)) { throw 'Chaves ausentes. Informe KeyRingDirectory.' }
    Copy-Item -LiteralPath $images -Destination (Join-Path $destination 'product-images') -Recurse
    Copy-Item -LiteralPath $KeyRingDirectory -Destination (Join-Path $destination 'data-protection') -Recurse
    $manifest = @{
        completedAt = [DateTimeOffset]::UtcNow.ToString('o'); database = 'made_in_minas'; databaseSha256 = (Get-FileHash -LiteralPath $dump -Algorithm SHA256).Hash
        images = @(Get-ChildItem -LiteralPath (Join-Path $destination 'product-images') -File -Recurse).Count
        protectedKeyFiles = @(Get-ChildItem -LiteralPath (Join-Path $destination 'data-protection') -File -Recurse).Count
        restoreValidated = $false
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'manifest.json') -Encoding UTF8
    @{ success = $true; completedAt = $manifest.completedAt } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $repository '.local\backup-status.json') -Encoding UTF8
    Write-Output ('Backup local concluido: ' + $destination)
    Write-Output 'O arquivo do banco foi conferido; ensaie a restauracao completa em outro banco. Mantenha copia protegida fora deste computador e os segredos no gerenciador escolhido.'
} catch {
    Write-Error ('Backup incompleto na etapa ' + $stage + '. Arquivos parciais preservados; nenhum arquivo anterior removido.')
    exit 1
} finally {
    $env:PGPASSWORD = $previousPassword
    Remove-Variable settings,connection -ErrorAction SilentlyContinue
}
