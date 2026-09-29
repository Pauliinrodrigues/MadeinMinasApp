param(
    [string]$ApiBaseUrl = 'http://localhost:5080',
    [string]$FrontendBaseUrl = 'http://localhost:8101',
    [switch]$ExpectDatabaseReady
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.UseProxy = $false
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(15)

function Assert-Condition([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    Write-Output "OK: $Message"
}

function Get-Response([string]$Url, [string]$Origin = '', [string]$Accept = 'application/json') {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $Url)
    $request.Headers.Add('Accept', $Accept)
    if ($Origin) { $request.Headers.Add('Origin', $Origin) }
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $cors = if ($response.Headers.Contains('Access-Control-Allow-Origin')) {
                $response.Headers.GetValues('Access-Control-Allow-Origin') -join ','
            } else { '' }
            return [pscustomobject]@{
                Status = [int]$response.StatusCode
                Body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                ContentType = $response.Content.Headers.ContentType.MediaType
                AllowedOrigin = $cors
            }
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}

try {
    $live = Get-Response "$ApiBaseUrl/health/live"
    Assert-Condition ($live.Status -eq 200 -and ($live.Body | ConvertFrom-Json).status -eq 'Healthy') 'Liveness returns 200 / Healthy'

    $ready = Get-Response "$ApiBaseUrl/health/ready"
    $expectedStatus = if ($ExpectDatabaseReady) { 200 } else { 503 }
    $expectedHealth = if ($ExpectDatabaseReady) { 'Healthy' } else { 'Unhealthy' }
    $readyBody = $ready.Body | ConvertFrom-Json
    Assert-Condition ($ready.Status -eq $expectedStatus -and $readyBody.checks.database -eq $expectedHealth) "Readiness returns $expectedStatus / $expectedHealth"
    Assert-Condition ($ready.Body -notmatch '(?i)password|username|host=|exception|stacktrace') 'Health response does not expose connection details'

    $status = Get-Response "$ApiBaseUrl/api/system/status" $FrontendBaseUrl
    Assert-Condition ($status.Status -eq 200 -and ($status.Body | ConvertFrom-Json).status -eq 'available') 'Frontend status contract responds'
    Assert-Condition ($status.AllowedOrigin -eq $FrontendBaseUrl) 'Configured CORS origin is accepted'

    $denied = Get-Response "$ApiBaseUrl/api/system/status" 'https://untrusted.invalid'
    Assert-Condition ($denied.AllowedOrigin -eq '') 'Unconfigured origin has no CORS permission'

    $missing = Get-Response "$ApiBaseUrl/api/does-not-exist"
    Assert-Condition ($missing.Status -eq 404 -and $missing.ContentType -eq 'application/problem+json' -and ($missing.Body | ConvertFrom-Json).status -eq 404) 'Unknown route returns 404 ProblemDetails'

    $openApi = Get-Response "$ApiBaseUrl/openapi/v1.json"
    Assert-Condition ($openApi.Status -eq 200 -and ($openApi.Body | ConvertFrom-Json).paths.'/api/system/status') 'Development OpenAPI contains status endpoint'

    $frontend = Get-Response $FrontendBaseUrl -Accept 'text/html'
    Assert-Condition ($frontend.Status -eq 200 -and $frontend.Body.Contains('<app-root>')) 'Frontend serves the Angular application'
} finally {
    $client.Dispose()
    $handler.Dispose()
}
