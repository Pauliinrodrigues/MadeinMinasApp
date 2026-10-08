[CmdletBinding()]
param(
    [string]$PrinterName = 'Diebold Procomp IM453HU_A',
    [ValidateSet(58, 80)]
    [int]$WidthMm = 80
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName ReachFramework
Add-Type -AssemblyName System.Printing

$thermalPrinter = Get-Printer -Name $PrinterName
$thermalDriver = Get-PrinterDriver -Name $thermalPrinter.DriverName
$thermalSettings = [System.Drawing.Printing.PrinterSettings]::new()
$thermalSettings.PrinterName = $PrinterName

if (-not $thermalSettings.IsValid) {
    throw "Printer not available: $PrinterName"
}

$thermalDefaultPaper = $thermalSettings.DefaultPageSettings.PaperSize
$thermalCustomSizeDeclared = $null
if ([System.IO.Path]::GetExtension($thermalDriver.DataFile) -eq '.gpd') {
    $thermalGpd = Get-Content -LiteralPath $thermalDriver.DataFile -Raw
    $thermalCustomSizeDeclared = $thermalGpd -match '(?im)^\s*\*Option:\s*CUSTOMSIZE\b'
}

$thermalServer = [System.Printing.LocalPrintServer]::new()
try {
    $thermalQueue = $thermalServer.GetPrintQueue($PrinterName)
    try {
        # Negotiate page dimensions in memory; Commit and print submission are not used.
        $thermalNegotiations = @(foreach ($thermalHeightMm in @(100, 150)) {
            $thermalRequest = [System.Printing.PrintTicket]::new()
            $thermalRequest.PageMediaSize = [System.Printing.PageMediaSize]::new(
                $WidthMm / 25.4 * 96,
                $thermalHeightMm / 25.4 * 96
            )
            $thermalValidation = $thermalQueue.MergeAndValidatePrintTicket(
                $thermalQueue.UserPrintTicket,
                $thermalRequest
            )
            $thermalReturnedSize = $thermalValidation.ValidatedPrintTicket.PageMediaSize
            $thermalReturnedWidth = if ($null -ne $thermalReturnedSize.Width) {
                [math]::Round([double]$thermalReturnedSize.Width * 25.4 / 96, 2)
            } else { $null }
            $thermalReturnedHeight = if ($null -ne $thermalReturnedSize.Height) {
                [math]::Round([double]$thermalReturnedSize.Height * 25.4 / 96, 2)
            } else { $null }
            [pscustomobject]@{
                RequestedWidthMm = $WidthMm
                RequestedHeightMm = $thermalHeightMm
                ReturnedWidthMm = $thermalReturnedWidth
                ReturnedHeightMm = $thermalReturnedHeight
                ConflictStatus = $thermalValidation.ConflictStatus.ToString()
                MatchesRequest = (
                    $null -ne $thermalReturnedWidth -and $null -ne $thermalReturnedHeight -and
                    [math]::Abs($thermalReturnedWidth - $WidthMm) -lt 0.5 -and
                    [math]::Abs($thermalReturnedHeight - $thermalHeightMm) -lt 0.5
                )
            }
        })
    } finally {
        $thermalQueue.Dispose()
    }
} finally {
    $thermalServer.Dispose()
}

[pscustomobject]@{
    PrinterName = $thermalPrinter.Name
    DriverName = $thermalPrinter.DriverName
    PortName = $thermalPrinter.PortName
    Landscape = $thermalSettings.DefaultPageSettings.Landscape
    DefaultPaper = [pscustomobject]@{
        Name = $thermalDefaultPaper.PaperName
        WidthMm = [math]::Round($thermalDefaultPaper.Width * 0.254, 2)
        HeightMm = [math]::Round($thermalDefaultPaper.Height * 0.254, 2)
    }
    AvailablePapers = @($thermalSettings.PaperSizes | ForEach-Object {
        [pscustomobject]@{
            Name = $_.PaperName
            WidthMm = [math]::Round($_.Width * 0.254, 2)
            HeightMm = [math]::Round($_.Height * 0.254, 2)
        }
    })
    GpdDeclaresCustomSize = $thermalCustomSizeDeclared
    Negotiations = $thermalNegotiations
} | ConvertTo-Json -Depth 5
