param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\17\bin',
    [string]$OutputDirectory = '.local\database-upgrade'
)

# Read the operational database only; restore/migrate exclusively on the pilot cluster.
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath((Join-Path $repository $OutputDirectory))
if (-not $output.StartsWith((Join-Path $repository '.local\'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Use uma pasta .local do projeto.' }
$previousPassword = $env:PGPASSWORD
$previousConnection = $env:ConnectionStrings__DefaultConnection
$previousEncoding = $OutputEncoding
$OutputEncoding = [Text.UTF8Encoding]::new($false)
function Invoke-LocalSql([string]$Sql, [string]$Database, [string]$Username, [int]$Port) {
    $result = $Sql | & (Join-Path $PostgresBin 'psql.exe') -X -w -h 127.0.0.1 -p $Port -U $Username -d $Database -v ON_ERROR_STOP=1 -Atq 2>> (Join-Path $output 'database-errors.log')
    if ($LASTEXITCODE -ne 0) { throw 'Falha na verificacao de banco. Consulte o log local protegido.' }
    return ($result -join "`n").Trim()
}
try {
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    $settings = Get-Content -Raw -LiteralPath (Join-Path $env:APPDATA 'Microsoft\UserSecrets\made-in-minas-api-development\secrets.json') | ConvertFrom-Json
    $source = [System.Data.Common.DbConnectionStringBuilder]::new()
    $source.set_ConnectionString([string]$settings.'ConnectionStrings:DefaultConnection')
    if ($source.get_Item('Host') -notin @('localhost', '127.0.0.1') -or $source.get_Item('Database') -ne 'made_in_minas' -or ($source.ContainsKey('Port') -and [int]$source.get_Item('Port') -ne 5432)) { throw 'Banco operacional local nao confirmado.' }
    $pilot = Get-Content -Raw -LiteralPath (Join-Path $repository '.local\ai-pilot\pilot.local.json') | ConvertFrom-Json
    $stamp = Get-Date -Format 'yyyyMMddHHmmss'
    $clone = 'made_in_minas_v1_restore_' + $stamp
    $dump = Join-Path $output ('made_in_minas-' + $stamp + '.dump')
    $env:PGPASSWORD = [string]$source.get_Item('Password')
    & (Join-Path $PostgresBin 'pg_dump.exe') -w -h 127.0.0.1 -p 5432 -U ([string]$source.get_Item('Username')) -d made_in_minas -Fc -f $dump 2>> (Join-Path $output 'database-errors.log')
    if ($LASTEXITCODE -ne 0) { throw 'Backup nao concluido.' }
    & (Join-Path $PostgresBin 'pg_restore.exe') -l $dump > (Join-Path $output 'backup-contents.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Arquivo de backup invalido.' }
    $env:PGPASSWORD = $pilot.DatabasePassword
    & (Join-Path $PostgresBin 'createdb.exe') -w -h 127.0.0.1 -p 55435 -U ai_pilot $clone 2>> (Join-Path $output 'database-errors.log')
    if ($LASTEXITCODE -ne 0) { throw 'Banco isolado nao criado.' }
    & (Join-Path $PostgresBin 'pg_restore.exe') -w -h 127.0.0.1 -p 55435 -U ai_pilot -d $clone --no-owner --no-acl --exit-on-error $dump 2>> (Join-Path $output 'database-errors.log')
    if ($LASTEXITCODE -ne 0) { throw 'Restauracao nao concluida.' }
    # Fixed projections of every pre-existing column prove preservation despite added columns.
    $projectionSql = @'
SELECT format('SELECT json_build_object(''table'', %L, ''count'', count(*), ''digest'', md5(coalesce(string_agg(digest, '''' ORDER BY digest), ''''))) FROM (SELECT md5(row_to_json(r)::text) digest FROM (SELECT %s FROM public.%I) r) h;', table_name, string_agg(format('%I', column_name), ', ' ORDER BY ordinal_position), table_name)
FROM information_schema.columns WHERE table_schema='public' AND table_name <> '__EFMigrationsHistory' GROUP BY table_name ORDER BY table_name;
'@
    $projections = Invoke-LocalSql $projectionSql $clone ai_pilot 55435
    $projections | Set-Content -LiteralPath (Join-Path $output 'preservation.sql') -Encoding UTF8
    $before = Invoke-LocalSql $projections $clone ai_pilot 55435
    $migrationSql = 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";'
    $historyBefore = Invoke-LocalSql $migrationSql $clone ai_pilot 55435
    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection['Host'] = '127.0.0.1'; $connection['Port'] = 55435; $connection['Database'] = $clone
    $connection['Username'] = 'ai_pilot'; $connection['Password'] = $pilot.DatabasePassword
    $env:ConnectionStrings__DefaultConnection = $connection.get_ConnectionString()
    & dotnet ef database update --project (Join-Path $repository 'backend\MadeInMinas.Api') -- --environment Development *> (Join-Path $output 'migration.log')
    if ($LASTEXITCODE -ne 0) { throw 'Migration no clone falhou. Banco operacional preservado.' }
    $after = Invoke-LocalSql $projections $clone ai_pilot 55435
    if ($before -ne $after) { throw 'Verificacao de preservacao falhou no clone. Banco operacional preservado.' }
    $historyAfter = Invoke-LocalSql $migrationSql $clone ai_pilot 55435
    # Compare the restored/upgraded schema against a fresh application database.
    $reference = $clone + '_schema'
    & (Join-Path $PostgresBin 'createdb.exe') -w -h 127.0.0.1 -p 55435 -U ai_pilot $reference 2>> (Join-Path $output 'database-errors.log')
    if ($LASTEXITCODE -ne 0) { throw 'Banco de referencia nao criado.' }
    $connection['Database'] = $reference
    $env:ConnectionStrings__DefaultConnection = $connection.get_ConnectionString()
    & dotnet ef database update --no-build --project (Join-Path $repository 'backend\MadeInMinas.Api') -- --environment Development *> (Join-Path $output 'reference-migration.log')
    if ($LASTEXITCODE -ne 0) { throw 'Schema de referencia nao preparado.' }
    # pg_dump/restoration re-parses CHECK expressions (e.g. varchar[] -> text[]).
    # Compare both schemas after the same round trip, without weakening SQL checks.
    $roundtrip = $clone + '_roundtrip'
    $referenceDump = Join-Path $output 'reference-schema.dump'
    & (Join-Path $PostgresBin 'pg_dump.exe') -w -h 127.0.0.1 -p 55435 -U ai_pilot -d $reference -Fc --schema-only -f $referenceDump 2>> (Join-Path $output 'database-errors.log')
    if ($LASTEXITCODE -ne 0) { throw 'Schema de referencia nao exportado.' }
    & (Join-Path $PostgresBin 'createdb.exe') -w -h 127.0.0.1 -p 55435 -U ai_pilot $roundtrip 2>> (Join-Path $output 'database-errors.log')
    if ($LASTEXITCODE -ne 0) { throw 'Referencia de restauracao nao criada.' }
    & (Join-Path $PostgresBin 'pg_restore.exe') -w -h 127.0.0.1 -p 55435 -U ai_pilot -d $roundtrip --no-owner --no-acl --exit-on-error $referenceDump 2>> (Join-Path $output 'database-errors.log')
    if ($LASTEXITCODE -ne 0) { throw 'Schema de referencia nao restaurado.' }
    $actualReference = $clone + '_actualschema'
    $actualDump = Join-Path $output 'actual-schema.dump'
    & (Join-Path $PostgresBin 'pg_dump.exe') -w -h 127.0.0.1 -p 55435 -U ai_pilot -d $clone -Fc --schema-only -f $actualDump 2>> (Join-Path $output 'database-errors.log')
    if ($LASTEXITCODE -ne 0) { throw 'Schema atualizado nao exportado.' }
    & (Join-Path $PostgresBin 'createdb.exe') -w -h 127.0.0.1 -p 55435 -U ai_pilot $actualReference 2>> (Join-Path $output 'database-errors.log')
    if ($LASTEXITCODE -ne 0) { throw 'Referencia do schema atualizado nao criada.' }
    & (Join-Path $PostgresBin 'pg_restore.exe') -w -h 127.0.0.1 -p 55435 -U ai_pilot -d $actualReference --no-owner --no-acl --exit-on-error $actualDump 2>> (Join-Path $output 'database-errors.log')
    if ($LASTEXITCODE -ne 0) { throw 'Schema atualizado nao restaurado.' }

    $schemaSql = @'
SELECT concat_ws('|','column',table_name,column_name,udt_name,is_nullable,character_maximum_length,numeric_precision,numeric_scale,column_default,is_identity)
FROM information_schema.columns WHERE table_schema='public'
UNION ALL
SELECT concat_ws('|','constraint',rel.relname,con.conname,pg_get_constraintdef(con.oid))
FROM pg_constraint con JOIN pg_class rel ON rel.oid=con.conrelid JOIN pg_namespace ns ON ns.oid=rel.relnamespace WHERE ns.nspname='public'
UNION ALL
SELECT concat_ws('|','index',tablename,indexname,indexdef) FROM pg_indexes WHERE schemaname='public'
ORDER BY 1;
'@
    $expectedSchema = @( (Invoke-LocalSql $schemaSql $roundtrip ai_pilot 55435) -split "`n" )
    $actualSchema = @( (Invoke-LocalSql $schemaSql $actualReference ai_pilot 55435) -split "`n" )
    $differences = @(Compare-Object -ReferenceObject $expectedSchema -DifferenceObject $actualSchema)
    $differences | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'schema-differences.json') -Encoding UTF8
    if (@($differences | Where-Object SideIndicator -eq '<=').Count -gt 0) { throw 'O clone diverge do schema esperado. Banco operacional preservado.' }
    foreach ($migration in ($historyBefore -split "`n")) {
        if (($historyAfter -split "`n") -notcontains $migration) { throw 'Historico anterior nao preservado.' }
    }
    $result = @{ backup = $dump; cloneDatabase = $clone; port = 55435; preserved = $true; schemaVerified = $true; legacySchemaDefinitions = @($differences | Where-Object SideIndicator -eq '=>').Count; checkedAt = [DateTimeOffset]::UtcNow.ToString('o'); migrationsBefore = @($historyBefore -split "`n"); migrationsAfter = @($historyAfter -split "`n") }
    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'result.json') -Encoding UTF8
    Write-Output 'Backup restaurado e atualizado no cluster isolado; todas as linhas anteriores e o historico foram preservados. Banco operacional nao alterado.'
    Write-Output ('Resultado: ' + (Join-Path $output 'result.json'))
} catch {
    Write-Error 'Ensaio nao concluido. Consulte os logs em .local/database-upgrade; nenhuma migration foi executada no banco operacional.'
    exit 1
} finally {
    $env:PGPASSWORD = $previousPassword; $env:ConnectionStrings__DefaultConnection = $previousConnection; $OutputEncoding = $previousEncoding
    Remove-Variable settings, source, pilot, connection -ErrorAction SilentlyContinue
}
