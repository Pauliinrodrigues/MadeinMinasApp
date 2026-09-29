$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '..\backend\MadeInMinas.Api'
[xml]$project = Get-Content -LiteralPath (Join-Path $projectPath 'MadeInMinas.Api.csproj')
$secretsId = [string]$project.Project.PropertyGroup.UserSecretsId
$secretPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$secretsId\secrets.json"
if (Test-Path -LiteralPath $secretPath) {
    $secrets = Get-Content -Raw -LiteralPath $secretPath | ConvertFrom-Json
    if ($secrets.'Jwt:SigningKey' -or $secrets.Jwt.SigningKey) {
        Write-Output 'Chave JWT existente preservada.'
        exit 0
    }
}

$bytes = New-Object byte[] 32
$generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try { $generator.GetBytes($bytes) } finally { $generator.Dispose() }
$json = @{ 'Jwt:SigningKey' = [Convert]::ToBase64String($bytes) } | ConvertTo-Json -Compress
$result = $json | & dotnet user-secrets set --project $projectPath 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel gravar a chave JWT em User Secrets.' }
Remove-Variable bytes, json, result, secrets -ErrorAction SilentlyContinue
Write-Output 'Chave JWT aleatoria salva em User Secrets. Nenhuma chave foi exibida.'
