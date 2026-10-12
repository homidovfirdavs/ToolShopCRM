<#
 Test-BackupRestore.ps1 - proves that a backup can really be restored.
  1. backs up the source database
  2. restores it into a disposable '<db>_restore_test' database
  3. compares the row count of EVERY table (source vs restored) and checks the EF migration history
  4. drops the disposable database (unless -KeepRestored)

 Stop writes to the source (close the app) while it runs, otherwise counts may differ legitimately.
 Prints PASS only if every table matches. Exit code 0 = PASS, 1 = FAIL.
#>
param(
    [string]$PgHost = 'localhost',
    [int]$Port = 5432,
    [string]$User = 'postgres',
    [string]$Database = 'toolshop_crm',
    [string]$RestoreDatabase,
    [string]$OutDir,
    [string]$PgBinPath,
    [switch]$KeepRestored
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_pg.ps1"

if (-not $RestoreDatabase) { $RestoreDatabase = "${Database}_restore_test" }
if ($RestoreDatabase -notmatch '_(restore_)?test$') { throw 'RestoreDatabase must end with _test or _restore_test.' }
if ($RestoreDatabase -eq $Database) { throw 'RestoreDatabase must differ from the source database.' }

$psql = Find-PgTool 'psql' $PgBinPath
$setPw = Initialize-PgPassword $User
$failed = $false
try {
    $common = @{ PgHost = $PgHost; Port = $Port; User = $User; PgBinPath = $PgBinPath }
    $backupArgs = $common.Clone(); $backupArgs.Database = $Database
    if ($OutDir) { $backupArgs.OutDir = $OutDir }

    $file = & "$PSScriptRoot\Backup-Database.ps1" @backupArgs | Select-Object -Last 1
    if (-not $file -or -not (Test-Path $file)) { throw 'Backup did not produce a file.' }

    $restoreArgs = $common.Clone()
    & "$PSScriptRoot\Restore-Database.ps1" -BackupFile $file -TargetDatabase $RestoreDatabase -Recreate @restoreArgs

    Write-Host 'Comparing row counts ...' -ForegroundColor Cyan
    $src = Get-PgTableCounts $psql $PgHost $Port $User $Database
    $dst = Get-PgTableCounts $psql $PgHost $Port $User $RestoreDatabase

    $rows = foreach ($t in $src.Keys) {
        $d = if ($dst.Contains($t)) { $dst[$t] } else { -1 }
        [pscustomobject]@{ Table = $t; Source = $src[$t]; Restored = $d; Match = ($src[$t] -eq $d) }
    }
    $extra = @($dst.Keys | Where-Object { -not $src.Contains($_) })
    $rows | Format-Table -AutoSize | Out-String | Write-Host

    $bad = @($rows | Where-Object { -not $_.Match })
    if ($bad.Count -gt 0 -or $extra.Count -gt 0) {
        $failed = $true
        Write-Host "FAIL: $($bad.Count) table(s) differ; $($extra.Count) unexpected table(s) in the restored copy." -ForegroundColor Red
    }
    elseif ($src.Count -eq 0) {
        $failed = $true
        Write-Host 'FAIL: the source database has no tables.' -ForegroundColor Red
    }
    else {
        $total = ($src.Values | Measure-Object -Sum).Sum
        Write-Host "PASS: $($src.Count) tables, $total rows - restored copy matches the source." -ForegroundColor Green
    }

    Write-Host "Backup file: $file"
    if (-not $KeepRestored) {
        Invoke-PgQuery $psql $PgHost $Port $User 'postgres' "DROP DATABASE IF EXISTS `"$RestoreDatabase`" WITH (FORCE)" | Out-Null
        Write-Host "Disposable database '$RestoreDatabase' removed."
    }
}
finally {
    if ($setPw) { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }
}
if ($failed) { exit 1 }
