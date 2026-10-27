param(
    [ValidatePattern('\A[a-zA-Z0-9][a-zA-Z0-9._-]{0,99}\z')]
    [string]$Model = 'gemini-3.5-flash-lite'
)

$ErrorActionPreference = 'Stop'
$apiProjectPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\backend\MadeInMinas.Api'))
$keySecure = $null
$keyText = $null
$configJson = $null
$commandResult = $null

try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'SDK .NET indisponivel.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $apiProjectPath 'MadeInMinas.Api.csproj'))) {
        throw 'Projeto da API indisponivel.'
    }
    Write-Host 'Configuracao local do Gemini. A IA permanecera desativada.'
    $keySecure = Read-Host 'Cole a nova chave do Google AI Studio (entrada oculta)' -AsSecureString
    $keyText = [System.Net.NetworkCredential]::new('', $keySecure).Password
    if ([string]::IsNullOrWhiteSpace($keyText) -or $keyText -match '\s') {
        throw 'Chave vazia ou com espacos.'
    }
    $configJson = @{
        'Ai:Provider' = 'Gemini'
        'Ai:Model' = $Model
        'Ai:ApiKey' = $keyText
        'Ai:Enabled' = 'false'
    } | ConvertTo-Json -Compress
    # stdin avoids putting the key in the child process arguments or command history.
    # user-secrets merges these entries without replacing JWT/database settings.
    $commandResult = $configJson | & dotnet user-secrets set --project $apiProjectPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw 'Falha ao gravar User Secrets.'
    }
    Write-Host 'Gemini configurado em User Secrets. Nenhuma chave foi exibida.' -ForegroundColor Green
    Write-Host 'A IA continua desativada. Confira a cota gratuita e prepare o banco do piloto antes de ativar e reiniciar a API.'
} catch {
    # Native errors can contain supplied input; never print the exception or captured output.
    Write-Host 'Configuracao nao concluida. Execute em um terminal interativo e confira o SDK .NET, a chave e as permissoes locais.' -ForegroundColor Red
    exit 1
} finally {
    if ($keySecure) { $keySecure.Dispose() }
    Remove-Variable keySecure, keyText, configJson, commandResult -ErrorAction SilentlyContinue
}
