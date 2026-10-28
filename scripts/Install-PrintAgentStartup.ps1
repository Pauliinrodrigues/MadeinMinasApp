[CmdletBinding(DefaultParameterSetName='Install', SupportsShouldProcess=$true)]
param(
    [Parameter(Mandatory=$true, ParameterSetName='Install')][string]$AgentPath,
    [Parameter(Mandatory=$true, ParameterSetName='Install')][string]$StorePath,
    [Parameter(Mandatory=$true, ParameterSetName='Remove')][switch]$Remove
)
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$taskName = 'MadeInMinas.PrintAgent.' + $identity.User.Value
if ($Remove) {
    if ($PSCmdlet.ShouldProcess($taskName, 'Remover inicio automatico')) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction Stop
        Write-Output 'Inicio automatico removido. Configuracao e fila preservadas.'
    }
    exit 0
}
$executable = (Resolve-Path -LiteralPath $AgentPath -ErrorAction Stop).Path
$store = (Resolve-Path -LiteralPath $StorePath -ErrorAction Stop).Path
if ([IO.Path]::GetFileName($executable) -ne 'MadeInMinas.PrintAgent.exe' -or -not (Test-Path -LiteralPath (Join-Path $store 'configuration.json'))) {
    throw 'Informe o executavel publicado e a estacao ja configurada para este usuario.'
}
$launcher = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'Run-InstalledPrintAgent.ps1')).Path
$arguments = '-NoProfile -NonInteractive -WindowStyle Hidden -File "' + $launcher + '" -AgentPath "' + $executable + '" -StorePath "' + $store + '"'
$windowsPowerShell = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell\v1.0\powershell.exe'
$action = New-ScheduledTaskAction -Execute $windowsPowerShell -Argument $arguments -WorkingDirectory ([IO.Path]::GetDirectoryName($executable))
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
$principal = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -StartWhenAvailable
$task = New-ScheduledTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Agente Made in Minas, executado no usuario que protegeu a credencial com DPAPI.'
if ($PSCmdlet.ShouldProcess($taskName, 'Instalar inicio automatico para este usuario')) {
    Register-ScheduledTask -TaskName $taskName -InputObject $task -Force | Out-Null
    Write-Output 'Inicio automatico configurado para o proximo login deste usuario do Windows. Valide a conexao na tela de impressao apos reiniciar.'
}
