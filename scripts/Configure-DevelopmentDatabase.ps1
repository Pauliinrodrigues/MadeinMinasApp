param()

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '..\backend\MadeInMinas.Api'
$previousPassword = $env:PGPASSWORD
$previousOutputEncoding = $OutputEncoding
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

function Invoke-DatabaseCommand {
    param(
        [string]$Sql,
        [string]$Username = 'postgres',
        [string]$Database = 'postgres'
    )

    $output = $Sql | & psql -X -w -h localhost -p 5432 -U $Username -d $Database -v ON_ERROR_STOP=1 -Atq 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw 'Falha no PostgreSQL. Confira a senha, o servico e as permissoes do usuario informado.'
    }
    return ($output -join "`n").Trim()
}

try {
    Get-Command psql, dotnet -ErrorAction Stop | Out-Null
    Write-Host 'Configuracao local da Made in Minas (localhost:5432).'
    Write-Host 'Nenhuma senha sera exibida ou gravada no repositorio.'
    $adminPassword = Read-Host 'Senha do usuario postgres' -AsSecureString
    $env:PGPASSWORD = [System.Net.NetworkCredential]::new('', $adminPassword).Password
    $adminPassword.Dispose()
    Invoke-DatabaseCommand 'SELECT 1;' | Out-Null

    $roleExists = (Invoke-DatabaseCommand "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'made_in_minas_app');") -eq 't'
    $databaseOwner = Invoke-DatabaseCommand "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = 'made_in_minas';"
    if ($databaseOwner -and $databaseOwner -ne 'made_in_minas_app') {
        throw 'O banco made_in_minas ja existe com outro proprietario. Nenhuma permissao foi alterada; revise antes de continuar.'
    }

    if ($roleExists) {
        Write-Host 'Usuario made_in_minas_app ja existe; sua senha sera preservada.'
        $existingPassword = Read-Host 'Senha atual de made_in_minas_app' -AsSecureString
        $applicationPassword = [System.Net.NetworkCredential]::new('', $existingPassword).Password
        $existingPassword.Dispose()
    } else {
        $randomBytes = New-Object byte[] 32
        $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        try { $generator.GetBytes($randomBytes) } finally { $generator.Dispose() }
        $applicationPassword = [Convert]::ToBase64String($randomBytes)
        # Base64 gerado localmente nao contem aspas; o SQL e enviado por stdin.
        Invoke-DatabaseCommand "CREATE ROLE made_in_minas_app LOGIN PASSWORD '$applicationPassword';" | Out-Null
        Write-Host 'Usuario da aplicacao criado com senha aleatoria.'
    }

    if (-not $databaseOwner) {
        Invoke-DatabaseCommand 'CREATE DATABASE made_in_minas OWNER made_in_minas_app;' | Out-Null
        Write-Host 'Banco made_in_minas criado.'
    } else {
        Write-Host 'Banco existente preservado.'
    }

    $env:PGPASSWORD = $applicationPassword
    Invoke-DatabaseCommand -Sql 'SELECT 1;' -Username 'made_in_minas_app' -Database 'made_in_minas' | Out-Null

    Add-Type -AssemblyName System.Data
    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection['Host'] = 'localhost'
    $connection['Port'] = 5432
    $connection['Database'] = 'made_in_minas'
    $connection['Username'] = 'made_in_minas_app'
    $connection['Password'] = $applicationPassword
    $connection['Timeout'] = 5
    $connection['Command Timeout'] = 5
    $secretJson = @{ 'ConnectionStrings:DefaultConnection' = $connection.ConnectionString } | ConvertTo-Json -Compress
    $secretResult = $secretJson | & dotnet user-secrets set --project $projectPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw 'Conexao validada, mas nao foi possivel salvar User Secrets. Confira as permissoes do perfil local.'
    }
    Write-Host 'Conexao autenticada e salva em User Secrets. Reinicie a API e verifique /health/ready.' -ForegroundColor Green
} catch {
    # Nao imprimir a excecao nativa: ela pode conter a instrucao SQL ou a conexao.
    Write-Host 'Configuracao nao concluida. Confira as credenciais, proprietario do banco e permissoes; nenhum banco existente foi removido.' -ForegroundColor Red
    exit 1
} finally {
    $env:PGPASSWORD = $previousPassword
    $OutputEncoding = $previousOutputEncoding
    if ($connection) { $connection.Clear() }
    Remove-Variable applicationPassword, secretJson, secretResult -ErrorAction SilentlyContinue
}
