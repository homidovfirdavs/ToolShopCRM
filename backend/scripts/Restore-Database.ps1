<#
 Restore-Database.ps1 - restores a backup made by Backup-Database.ps1 into a NEW database.

 Safety rules:
  * the target is created fresh; an existing target is only replaced with -Recreate
  * targets whose name ends with _test or _restore_test are treated as disposable
  * any other target (including the real 'toolshop_crm') needs -AllowProduction AND you must type  RESTORE <name>
  * the SHA-256 file next to the backup (if present) must match

 Example (safe, disposable):  .\scripts\Restore-Database.ps1 -BackupFile ..\backups\toolshop_crm-2026....dump -TargetDatabase toolshop_crm_restore_test -Recreate
#>
param(
    [Parameter(Mandatory = $true)][string]$BackupFile,
    [Parameter(Mandatory = $true)][string]$TargetDatabase,
    [string]$PgHost = 'localhost',
    [int]$Port = 5432,
    [string]$User = 'postgres',
    [string]$PgBinPath,
    [switch]$Recreate,
    [switch]$AllowProduction
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_pg.ps1"

if (-not (Test-Path $BackupFile)) { throw "Backup file not found: $BackupFile" }
if ($TargetDatabase -notmatch '^[A-Za-z0-9_]+$') { throw 'TargetDatabase may contain only letters, digits and underscore.' }

$disposable = $TargetDatabase -match '_(restore_)?test$'
if (-not $disposable) {
    if (-not $AllowProduction) {
        throw "'$TargetDatabase' is not a disposable database (name must end with _test or _restore_test). Use -AllowProduction only for a real recovery."
    }
    $typed = Read-Host "Type  RESTORE $TargetDatabase  to continue"
    if ($typed -cne "RESTORE $TargetDatabase") { throw 'Confirmation did not match. Nothing was changed.' }
}

# integrity
$shaFile = "$BackupFile.sha256"
if (Test-Path $shaFile) {
    $expected = ((Get-Content $shaFile -Raw).Trim() -split '\s+')[0]
    $actual = (Get-FileHash $BackupFile -Algorithm SHA256).Hash
    if ($expected -ne $actual) { throw 'SHA-256 mismatch: the backup file was changed or is damaged. Not restoring.' }
    Write-Host 'SHA-256 matches.' -ForegroundColor Green
} else {
    Write-Host 'No .sha256 file next to the backup - integrity not verified.' -ForegroundColor Yellow
}

$psql = Find-PgTool 'psql' $PgBinPath
$pgRestore = Find-PgTool 'pg_restore' $PgBinPath
$setPw = Initialize-PgPassword $User
try {
    $exists = @(Invoke-PgQuery $psql $PgHost $Port $User 'postgres' "SELECT 1 FROM pg_database WHERE datname='$TargetDatabase'")
    if ($exists.Count -gt 0) {
        if (-not $Recreate) { throw "Database '$TargetDatabase' already exists. Use -Recreate to replace it." }
        Write-Host "Dropping existing '$TargetDatabase'" -ForegroundColor Yellow
        Invoke-PgQuery $psql $PgHost $Port $User 'postgres' "DROP DATABASE `"$TargetDatabase`" WITH (FORCE)" | Out-Null
    }
    Invoke-PgQuery $psql $PgHost $Port $User 'postgres' "CREATE DATABASE `"$TargetDatabase`"" | Out-Null

    Write-Host "Restoring into '$TargetDatabase' ..." -ForegroundColor Cyan
    & $pgRestore -h $PgHost -p $Port -U $User -d $TargetDatabase --no-owner --no-privileges --exit-on-error $BackupFile
    if ($LASTEXITCODE -ne 0) { throw 'pg_restore failed. The target database may be incomplete - do not use it.' }

    Write-Host "Restore finished: $TargetDatabase" -ForegroundColor Green
}
finally {
    if ($setPw) { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }
}
