<#
 Backup-Database.ps1 - full logical backup of the ToolShop database (pg_dump custom format, compressed).
 Read-only for the database. Verifies the file is readable (pg_restore --list) and writes a SHA-256 next to it.

 A backup you have never restored is not a backup: run .\scripts\Test-BackupRestore.ps1 to prove it.
 The file contains ALL data (including user password hashes) - keep it private and copy it OFF this machine.

 Example:  .\scripts\Backup-Database.ps1
           .\scripts\Backup-Database.ps1 -Database toolshop_crm -OutDir D:\backups
#>
param(
    [string]$PgHost = 'localhost',
    [int]$Port = 5432,
    [string]$User = 'postgres',
    [string]$Database = 'toolshop_crm',
    [string]$OutDir,
    [string]$PgBinPath
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_pg.ps1"

if (-not $OutDir) { $OutDir = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'backups' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$pgDump = Find-PgTool 'pg_dump' $PgBinPath
$pgRestore = Find-PgTool 'pg_restore' $PgBinPath
$setPw = Initialize-PgPassword $User
try {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $file = Join-Path $OutDir "$Database-$stamp.dump"

    Write-Host "Backing up '$Database' -> $file" -ForegroundColor Cyan
    & $pgDump -h $PgHost -p $Port -U $User -d $Database -Fc -Z 6 --no-owner --no-privileges -f $file
    if ($LASTEXITCODE -ne 0) {
        if (Test-Path $file) { Remove-Item $file -Force }   # a half-written file is not a backup
        throw 'pg_dump failed.'
    }

    # 1) the archive must be readable
    & $pgRestore --list $file | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Backup file is not readable by pg_restore: $file" }

    # 2) checksum for later integrity checks
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash
    Set-Content -Path "$file.sha256" -Value "$hash  $(Split-Path $file -Leaf)" -Encoding ASCII

    $mb = [math]::Round((Get-Item $file).Length / 1MB, 2)
    Write-Host "OK  $file  ($mb MB)" -ForegroundColor Green
    Write-Host "SHA-256: $hash"
    Write-Host 'Reminder: this file has not been restore-tested yet. Run .\scripts\Test-BackupRestore.ps1.' -ForegroundColor Yellow
    Write-Output $file
}
finally {
    if ($setPw) { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }
}
