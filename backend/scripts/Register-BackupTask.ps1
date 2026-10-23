<#
 Register-BackupTask.ps1 - creates the Windows scheduled task 'ToolShopBackup' that runs Backup-Scheduled.ps1 every day.

 * Runs as YOU, only while you are signed in to Windows. No Windows password is stored anywhere.
 * If the computer was off at the scheduled time, the backup runs as soon as it is available again (StartWhenAvailable).
 * Refuses to replace an existing task unless you pass -Replace.
 * Remove it any time:  Unregister-ScheduledTask -TaskName ToolShopBackup -Confirm:$false

 Example:  powershell -ExecutionPolicy Bypass -File .\Register-BackupTask.ps1 -At 23:30 -CopyTo E:\ToolShopBackups
#>
param(
    [string]$At = '23:30',
    [string]$CopyTo,
    [string]$TaskName = 'ToolShopBackup',
    [switch]$Replace
)

$ErrorActionPreference = 'Stop'

$script = Join-Path $PSScriptRoot 'Backup-Scheduled.ps1'
if (-not (Test-Path -LiteralPath $script)) { throw "Not found: $script" }
if ($At -notmatch '^([01]\d|2[0-3]):[0-5]\d$') { throw "-At must look like 23:30" }

$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing -and -not $Replace) { throw "Task '$TaskName' already exists. Use -Replace to replace it." }

$argText = "-NoProfile -ExecutionPolicy Bypass -File `"$script`""
if ($CopyTo) { $argText += " -CopyTo `"$CopyTo`"" }

$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $argText -WorkingDirectory $PSScriptRoot
$trigger = New-ScheduledTaskTrigger -Daily -At $At
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 30) -MultipleInstances IgnoreNew
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force:$Replace | Out-Null
Write-Host "Task '$TaskName' registered: every day at $At as $env:USERDOMAIN\$env:USERNAME." -ForegroundColor Green
Write-Host "Run it now to test:  Start-ScheduledTask -TaskName $TaskName"
Write-Host "Result of the last run:  (Get-ScheduledTaskInfo -TaskName $TaskName).LastTaskResult   (0 = OK)"
