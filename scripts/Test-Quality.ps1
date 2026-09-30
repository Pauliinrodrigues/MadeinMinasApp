param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\17\bin'
)

$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Invoke-Check {
    param([string]$Command, [string[]]$Arguments)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Verificacao falhou: $Command $($Arguments -join ' ')"
    }
}

Push-Location $workspace
try {
    Invoke-Check dotnet @('restore', 'MadeinMinasApp.sln', '--locked-mode')
    Invoke-Check dotnet @('format', 'whitespace', 'MadeinMinasApp.sln', '--no-restore', '--verify-no-changes', '--exclude', 'backend/MadeInMinas.Api/Data/Migrations')
    # O script compila e testa em .local/test-build, sem substituir a API em execucao.
    Invoke-Check powershell @('-NoProfile', '-File', 'scripts/Test-Authentication.ps1', '-PostgresBin', $PostgresBin)

    Set-Location (Join-Path $workspace 'frontend/made-in-minas')
    Invoke-Check npm.cmd @('run', 'format:check')
    Invoke-Check npm.cmd @('run', 'lint')
    Invoke-Check npm.cmd @('run', 'build')
    Invoke-Check npm.cmd @('run', 'test:e2e')
} finally {
    Pop-Location
}
