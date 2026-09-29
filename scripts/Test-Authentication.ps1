param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\17\bin',
    [string]$Filter = ''
)

$ErrorActionPreference = 'Stop'
$workspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = Join-Path $workspace '.local'
$testRoot = Join-Path $artifactsRoot ('auth-tests-' + [Guid]::NewGuid().ToString('N'))
$clusterPath = Join-Path $testRoot 'postgres'
$previousPassword = $env:PGPASSWORD
$previousConnection = $env:AuthTests__ConnectionString
$testExitCode = 1

try {
    if (Get-NetTCPConnection -State Listen -LocalPort 55433 -ErrorAction SilentlyContinue) {
        throw 'A porta 55433 esta ocupada. Nenhum processo existente sera encerrado.'
    }
    foreach ($executable in @('initdb.exe', 'pg_ctl.exe', 'createdb.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $PostgresBin $executable))) {
            throw "Ferramenta PostgreSQL nao encontrada: $executable"
        }
    }
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $passwordBytes = New-Object byte[] 32
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($passwordBytes) } finally { $random.Dispose() }
    $password = [Convert]::ToBase64String($passwordBytes)
    $passwordPath = Join-Path $testRoot 'password.tmp'
    [System.IO.File]::WriteAllText($passwordPath, $password)
    & (Join-Path $PostgresBin 'initdb.exe') -D $clusterPath -U auth_tests --pwfile=$passwordPath --auth=scram-sha-256 --encoding=UTF8 --locale=C
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao inicializar PostgreSQL isolado.' }
    Remove-Item -LiteralPath $passwordPath
    & (Join-Path $PostgresBin 'pg_ctl.exe') -D $clusterPath -l (Join-Path $testRoot 'postgres.log') -o '-h 127.0.0.1 -p 55433' -w start
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao iniciar PostgreSQL isolado.' }
    $env:PGPASSWORD = $password
    & (Join-Path $PostgresBin 'createdb.exe') -h 127.0.0.1 -p 55433 -U auth_tests made_in_minas_auth_tests
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao criar banco de testes.' }
    $env:AuthTests__ConnectionString = "Host=127.0.0.1;Port=55433;Database=made_in_minas_auth_tests;Username=auth_tests;Password=$password;Timeout=5"
    $testArguments = @('test', (Join-Path $workspace 'backend\MadeInMinas.Api.Tests'), '--configuration', 'Release', '--artifacts-path', (Join-Path $artifactsRoot 'test-build'), '--logger', 'console;verbosity=normal')
    if ($Filter) { $testArguments += @('--filter', $Filter) }
    & dotnet @testArguments
    $testExitCode = $LASTEXITCODE
} finally {
    $env:PGPASSWORD = $previousPassword
    $env:AuthTests__ConnectionString = $previousConnection
    if (Test-Path -LiteralPath (Join-Path $clusterPath 'postmaster.pid')) {
        & (Join-Path $PostgresBin 'pg_ctl.exe') -D $clusterPath -m fast -w stop
        if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL de testes nao parou. Arquivos preservados para diagnostico.' }
    }
    if (Test-Path -LiteralPath $testRoot) {
        $resolvedRoot = (Resolve-Path -LiteralPath $testRoot).Path
        if (-not $resolvedRoot.StartsWith($artifactsRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Diretorio de limpeza fora da pasta local de testes.'
        }
        Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
    }
}
exit $testExitCode
