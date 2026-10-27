param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\17\bin',
    [ValidateRange(9, 100000)][int]$MaxDailyRequests = 300,
    [ValidateSet('Synthetic', 'Rehearsal')][string]$Profile = 'Synthetic'
)

$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sharedRoot = Join-Path $workspace '.local\ai-pilot'
$pilotRoot = if ($Profile -eq 'Rehearsal') { Join-Path $workspace '.local\ai-rehearsal' } else { $sharedRoot }
$clusterPath = Join-Path $sharedRoot 'postgres'
$databaseName = if ($Profile -eq 'Rehearsal') { 'made_in_minas_ai_rehearsal' } else { 'made_in_minas_ai_pilot' }
$jwtIssuer = if ($Profile -eq 'Rehearsal') { 'MadeInMinas.AiRehearsal' } else { 'MadeInMinas.AiPilot' }
$apiPort = if ($Profile -eq 'Rehearsal') { 5083 } else { 5082 }
$frontendPort = if ($Profile -eq 'Rehearsal') { 8103 } else { 8102 }
$angularConfiguration = if ($Profile -eq 'Rehearsal') { 'rehearsal' } else { 'pilot' }
$configPath = Join-Path $pilotRoot 'pilot.local.json'
$apiRoot = Join-Path $workspace 'backend\MadeInMinas.Api'
$frontendRoot = Join-Path $workspace 'frontend\made-in-minas'
$apiOutput = Join-Path $pilotRoot 'api'
$apiProcess = $null
$frontendProcess = $null
$startedCluster = $false
$success = $false
$stage = 'pre-requisitos'
$environmentNames = @('ASPNETCORE_ENVIRONMENT', 'ASPNETCORE_URLS', 'ConnectionStrings__DefaultConnection', 'Jwt__SigningKey', 'Jwt__Issuer', 'Jwt__Audience', 'Cors__AllowedOrigins__0', 'Ai__Enabled', 'Ai__MaxOutputTokens', 'Ai__MaxDailyRequests', 'Ai__MaxTurnsPerConversation', 'StoreAttendance__ReceiveOrdersOutsideHours', 'PublicDelivery__FixedFee', 'PublicDelivery__PickupAddress', 'ProductImages__StoragePath', 'AiPilot__ConnectionString', 'AiPilot__AdminPassword', 'AiPilot__Profile', 'AiPilot__SnapshotPath', 'PGPASSWORD', 'Logging__LogLevel__MadeInMinas.Api.Services.Ai.AiService')
$previousEnvironment = @{}
foreach ($name in $environmentNames) { $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }

function New-LocalPassword {
    $bytes = New-Object byte[] 48
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($bytes) } finally { $random.Dispose() }
    return [Convert]::ToBase64String($bytes)
}

function Invoke-PostgresControl([string]$Arguments) {
    # A separate hidden console prevents the daemon from retaining our output
    # pipes. The process handle is owned by .NET, so its exit code is reliable.
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $PostgresBin 'pg_ctl.exe'
    $info.Arguments = $Arguments
    $info.UseShellExecute = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $control = [Diagnostics.Process]::Start($info)
    try {
        if (-not $control.WaitForExit(30000)) { throw 'Timeout do controle PostgreSQL.' }
        return $control.ExitCode
    } finally { $control.Dispose() }
}

try {
    foreach ($port in @($apiPort, $frontendPort)) {
        if (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue) {
            throw 'Uma porta do piloto esta ocupada. Nenhum processo existente sera encerrado.'
        }
    }
    foreach ($executable in @('initdb.exe', 'pg_ctl.exe', 'createdb.exe', 'psql.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $PostgresBin $executable))) { throw 'PostgreSQL 17 indisponivel.' }
    }
    $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
    $nodePath = (Get-Command node -ErrorAction Stop).Source
    $angularCli = Join-Path $frontendRoot 'node_modules\@angular\cli\bin\ng.js'
    if (-not (Test-Path -LiteralPath $angularCli)) { throw 'Instale as dependencias do frontend antes de iniciar o piloto.' }
    $userSecrets = Join-Path $env:APPDATA 'Microsoft\UserSecrets\made-in-minas-api-development\secrets.json'
    $aiSettings = Get-Content -LiteralPath $userSecrets -Raw | ConvertFrom-Json
    if ($aiSettings.'Ai:Provider' -ne 'Gemini' -or [string]::IsNullOrWhiteSpace($aiSettings.'Ai:Model') -or [string]::IsNullOrWhiteSpace($aiSettings.'Ai:ApiKey')) {
        throw 'Configure o Gemini usando scripts/Configure-Gemini.ps1.'
    }
    Remove-Variable aiSettings
    if ($Profile -eq 'Rehearsal' -and (-not (Test-Path -LiteralPath (Join-Path $sharedRoot 'pilot.local.json')) -or -not (Test-Path -LiteralPath (Join-Path $pilotRoot 'catalog.snapshot.json')))) {
        throw 'Inicialize o piloto sintetico e capture o catalogo publico antes do ensaio.'
    }
    New-Item -ItemType Directory -Path $pilotRoot -Force | Out-Null
    if (-not (Test-Path -LiteralPath $configPath)) {
        if ($Profile -eq 'Synthetic' -and (Test-Path -LiteralPath $clusterPath)) { throw 'Cluster existente sem configuracao local. Arquivos preservados.' }
        $databasePassword = if ($Profile -eq 'Rehearsal') { (Get-Content -LiteralPath (Join-Path $sharedRoot 'pilot.local.json') -Raw | ConvertFrom-Json).DatabasePassword } else { New-LocalPassword }
        $config = @{ DatabasePassword = $databasePassword; SigningKey = (New-LocalPassword); AdminPassword = (New-LocalPassword); AdminUsername = 'piloto.admin' }
        $config | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding UTF8
    } else { $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json }
    if (-not (Test-Path -LiteralPath (Join-Path $clusterPath 'PG_VERSION'))) {
        if (Get-NetTCPConnection -State Listen -LocalPort 55435 -ErrorAction SilentlyContinue) { throw 'Porta PostgreSQL do piloto ocupada.' }
        $passwordPath = Join-Path $pilotRoot 'password.tmp'
        try {
            [IO.File]::WriteAllText($passwordPath, $config.DatabasePassword)
            & (Join-Path $PostgresBin 'initdb.exe') -D $clusterPath -U ai_pilot --pwfile=$passwordPath --auth=scram-sha-256 --encoding=UTF8 --locale=C *> (Join-Path $pilotRoot 'initdb.log')
            if ($LASTEXITCODE -ne 0) { throw 'Falha ao inicializar o banco isolado.' }
        } finally {
            if (Test-Path -LiteralPath $passwordPath) { Remove-Item -LiteralPath $passwordPath }
        }
    }
    $stage = 'PostgreSQL'
    $postgresStatus = Invoke-PostgresControl ('-D "' + $clusterPath + '" status')
    if ($postgresStatus -ne 0) {
        if (Get-NetTCPConnection -State Listen -LocalPort 55435 -ErrorAction SilentlyContinue) { throw 'Porta PostgreSQL do piloto ocupada.' }
        # Wait only for pg_ctl. Windows PowerShell pipeline redirection can wait
        # indefinitely for the daemon that inherits the native command handles.
        $postgresArguments = '-D "' + $clusterPath + '" -l "' + (Join-Path $pilotRoot 'postgres.log') + '" -o "-h 127.0.0.1 -p 55435" -w start'
        if ((Invoke-PostgresControl $postgresArguments) -ne 0) { throw 'Falha ao iniciar o PostgreSQL isolado.' }
        $startedCluster = $true
    }
    $stage = 'criacao do banco'
    $env:PGPASSWORD = $config.DatabasePassword
    $databaseExists = & (Join-Path $PostgresBin 'psql.exe') -h 127.0.0.1 -p 55435 -U ai_pilot -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$databaseName'" 2> (Join-Path $pilotRoot 'database-check.log')
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao conferir o banco do piloto.' }
    if ($databaseExists -ne '1') {
        & (Join-Path $PostgresBin 'createdb.exe') -h 127.0.0.1 -p 55435 -U ai_pilot $databaseName *> (Join-Path $pilotRoot 'createdb.log')
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao criar banco isolado.' }
    }
    $connection = "Host=127.0.0.1;Port=55435;Database=$databaseName;Username=ai_pilot;Password=$($config.DatabasePassword);SSL Mode=Disable;Timeout=5"
    $env:AiPilot__ConnectionString = $connection
    $env:AiPilot__AdminPassword = $config.AdminPassword
    $env:AiPilot__Profile = $Profile
    $env:AiPilot__SnapshotPath = if ($Profile -eq 'Rehearsal') { Join-Path $pilotRoot 'catalog.snapshot.json' } else { '' }
    $stage = 'migration e dados ficticios'
    & $dotnetPath run --project (Join-Path $PSScriptRoot 'AiPilot\AiPilot.csproj') --configuration Release *> (Join-Path $pilotRoot 'prepare.log')
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao preparar o schema e os dados ficticios.' }
    Remove-Item Env:AiPilot__AdminPassword, Env:AiPilot__ConnectionString, Env:AiPilot__Profile, Env:AiPilot__SnapshotPath, Env:PGPASSWORD -ErrorAction SilentlyContinue
    $stage = 'compilacao da API'
    & $dotnetPath build (Join-Path $apiRoot 'MadeInMinas.Api.csproj') --configuration Release --output $apiOutput *> (Join-Path $pilotRoot 'api-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar a API.' }
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = 'http://localhost:' + $apiPort
    $env:ConnectionStrings__DefaultConnection = $connection
    $env:Jwt__SigningKey = $config.SigningKey
    $env:Jwt__Issuer = $jwtIssuer
    $env:Jwt__Audience = $jwtIssuer + '.Staff'
    $env:Cors__AllowedOrigins__0 = 'http://localhost:' + $frontendPort
    $env:Ai__Enabled = 'true'
    $env:Ai__MaxOutputTokens = '2048'
    $env:Ai__MaxDailyRequests = [string]$MaxDailyRequests
    $env:Ai__MaxTurnsPerConversation = '30'
    $env:StoreAttendance__ReceiveOrdersOutsideHours = if ($Profile -eq 'Rehearsal') { 'false' } else { 'true' }
    if ($Profile -eq 'Rehearsal') {
        [Environment]::SetEnvironmentVariable('Logging__LogLevel__MadeInMinas.Api.Services.Ai.AiService', 'Debug', 'Process')
        $snapshot = Get-Content -LiteralPath (Join-Path $pilotRoot 'catalog.snapshot.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $env:PublicDelivery__FixedFee = [Convert]::ToString($snapshot.options.deliveryFee, [Globalization.CultureInfo]::InvariantCulture)
        $env:PublicDelivery__PickupAddress = $snapshot.options.pickupAddress
        Remove-Variable snapshot
    }
    $env:ProductImages__StoragePath = Join-Path $pilotRoot 'product-images'
    $apiDll = Join-Path $apiOutput 'MadeInMinas.Api.dll'
    $stage = 'startup da API e frontend'
    $apiProcess = Start-Process -FilePath $dotnetPath -ArgumentList ('"' + $apiDll + '"') -WorkingDirectory $apiRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $pilotRoot 'api.log') -RedirectStandardError (Join-Path $pilotRoot 'api-errors.log')
    # The frontend does not inherit backend configuration, credentials or AI controls.
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process') }
    $frontendProcess = Start-Process -FilePath $nodePath -ArgumentList ('"' + $angularCli + '" serve --configuration ' + $angularConfiguration + ' --host localhost --port ' + $frontendPort) -WorkingDirectory $frontendRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $pilotRoot 'frontend.log') -RedirectStandardError (Join-Path $pilotRoot 'frontend-errors.log')
    @{ ApiPid = $apiProcess.Id; FrontendPid = $frontendProcess.Id } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $pilotRoot 'processes.json')
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $apiProcess.Refresh(); $frontendProcess.Refresh()
        if ($apiProcess.HasExited -or $frontendProcess.HasExited) { throw 'Um processo do piloto encerrou durante o startup.' }
        try {
            $health = Invoke-RestMethod -Uri ('http://localhost:' + $apiPort + '/health/ready') -TimeoutSec 2
            $page = Invoke-WebRequest -Uri ('http://localhost:' + $frontendPort) -UseBasicParsing -TimeoutSec 2
            if ($health.status -eq 'Healthy' -and $page.StatusCode -eq 200) { $ready = $true; break }
        } catch { }
        Start-Sleep -Seconds 2
    }
    if (-not $ready) { throw 'O piloto nao ficou disponivel no tempo esperado.' }
    $success = $true
    Write-Host ('Piloto Gemini ativo, perfil ' + $Profile + ': http://localhost:' + $frontendPort + '/pedido/atendimento')
    Write-Host ('Administrador: piloto.admin. Senha local em ' + $pilotRoot + '\pilot.local.json (nao compartilhar).')
    Write-Host 'Pedidos e estoque deste banco sao de teste. A impressora nao esta conectada ao piloto.'
    Write-Host ('Para parar: powershell -NoProfile -File scripts/Stop-AiPilot.ps1 -Profile ' + $Profile + '. Os dados serao preservados.')
} catch {
    # Do not print exceptions: native/process errors can include configuration values.
    Write-Host ('Piloto nao iniciado na etapa: ' + $stage + '. Confira os logs locais em ' + $pilotRoot + '. Nenhuma chave foi exibida.') -ForegroundColor Red
    exit 1
} finally {
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process') }
    if (-not $success) {
        foreach ($process in @($frontendProcess, $apiProcess)) {
            if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue }
        }
        if ($startedCluster -and (Test-Path -LiteralPath (Join-Path $clusterPath 'postmaster.pid'))) {
            & (Join-Path $PostgresBin 'pg_ctl.exe') -D $clusterPath -m fast -w stop *> $null
        }
    }
    Remove-Variable config, connection -ErrorAction SilentlyContinue
}
