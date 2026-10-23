<#
 Backup-Scheduled.ps1 - unattended nightly backup, started by Windows Task Scheduler (see Register-BackupTask.ps1).

 * Connects as the READ-ONLY role 'toolshop_backup'. It NEVER asks for a password and NEVER takes one on the command line:
   libpq reads it from %APPDATA%\postgresql\pgpass.conf (a file only you create, outside the repository). If it is
   missing the script fails with a clear message instead of waiting for input (-w).
 * Writes  <OutDir>\<db>-auto-<yyyyMMdd-HHmmss>.dump  (+ .sha256). Only files with '-auto-' in the name are ever rotated;
   your manual backups are never touched. Keeps the newest -KeepAuto (default 14, never fewer than 3).
 * Checks: pg_restore can read it, it is not tiny, it is not less than half of the previous automatic backup.
 * Optional second copy: -CopyTo E:\ToolShopBackups (a USB stick / another disk). If that path is not there tonight it is
   logged as a WARNING, not a failure. The copy is verified by SHA-256.
 * Log: <OutDir>\backup.log (no secrets). <OutDir>\LAST-BACKUP-OK.txt after success, <OutDir>\BACKUP-FAILED.txt after a failure.
 * Exit code 0 = OK, 1 = FAILED.

 Manual run:  powershell -ExecutionPolicy Bypass -File .\Backup-Scheduled.ps1
#>
param(
    [string]$PgHost = 'localhost',
    [int]$Port = 5432,
    [string]$User = 'toolshop_backup',
    [string]$Database = 'toolshop_crm',
    [string]$OutDir,
    [int]$KeepAuto = 14,
    [long]$MinBytes = 20480,
    [string]$CopyTo,
    [string]$PgBinPath
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_pg.ps1"

if ($KeepAuto -lt 3) { $KeepAuto = 3 }
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $OutDir) { $OutDir = Join-Path $repoRoot 'backups' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$log = Join-Path $OutDir 'backup.log'
$okFile = Join-Path $OutDir 'LAST-BACKUP-OK.txt'
$failFile = Join-Path $OutDir 'BACKUP-FAILED.txt'

function Write-Log([string]$Message) {
    $line = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    Add-Content -Path $log -Value $line -Encoding UTF8
    Write-Host $line
}

function Invoke-Native([string]$Exe, [string[]]$Arguments) {
    # Native tools write progress to stderr; do not let that stop the script. Judge by the exit code only.
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & $Exe @Arguments 2>&1
        return [pscustomobject]@{ Code = $LASTEXITCODE; Output = (($out | ForEach-Object { "$_" }) -join ' ') }
    }
    finally { $ErrorActionPreference = $prev }
}

function Remove-OldAuto([string]$Dir, [string]$Prefix, [int]$Keep) {
    $files = @(Get-ChildItem -Path $Dir -Filter "$Prefix-auto-*.dump" -File -ErrorAction SilentlyContinue | Sort-Object Name -Descending)
    if ($files.Count -le $Keep) { return }
    foreach ($f in ($files | Select-Object -Skip $Keep)) {
        Remove-Item -LiteralPath $f.FullName -Force
        if (Test-Path -LiteralPath "$($f.FullName).sha256") { Remove-Item -LiteralPath "$($f.FullName).sha256" -Force }
        Write-Log "Rotated out old automatic backup: $($f.Name)"
    }
}

$file = $null
try {
    $pgDump = Find-PgTool 'pg_dump' $PgBinPath
    $pgRestore = Find-PgTool 'pg_restore' $PgBinPath

    $previous = @(Get-ChildItem -Path $OutDir -Filter "$Database-auto-*.dump" -File -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1)

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $file = Join-Path $OutDir "$Database-auto-$stamp.dump"
    Write-Log "Backup of '$Database' as '$User' -> $file"

    $r = Invoke-Native $pgDump @('-h', $PgHost, '-p', "$Port", '-U', $User, '-d', $Database, '-w', '-Fc', '-Z', '6', '--no-owner', '--no-privileges', '-f', $file)
    if ($r.Code -ne 0) {
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }   # a half-written file is not a backup
        throw "pg_dump failed (exit $($r.Code)): $($r.Output)"
    }

    $r = Invoke-Native $pgRestore @('--list', $file)
    if ($r.Code -ne 0) { throw "The backup file is not readable by pg_restore: $file" }

    $size = (Get-Item -LiteralPath $file).Length
    if ($size -lt $MinBytes) { throw "The backup is only $size bytes (minimum $MinBytes). Something is wrong; the file was kept for inspection: $file" }
    if ($previous.Count -gt 0 -and $size -lt ($previous[0].Length / 2)) {
        throw "The backup ($size bytes) is less than half of the previous automatic one ($($previous[0].Length) bytes). The file was kept for inspection: $file"
    }

    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    Set-Content -Path "$file.sha256" -Value "$hash  $(Split-Path $file -Leaf)" -Encoding ASCII
    $mb = [math]::Round($size / 1MB, 3)
    Write-Log "OK $(Split-Path $file -Leaf) ($mb MB) SHA-256 $hash"

    # second copy (optional)
    $copyNote = 'not requested'
    if ($CopyTo) {
        if (Test-Path -LiteralPath $CopyTo) {
            Copy-Item -LiteralPath $file -Destination $CopyTo -Force
            Copy-Item -LiteralPath "$file.sha256" -Destination $CopyTo -Force
            $copied = Join-Path $CopyTo (Split-Path $file -Leaf)
            $copiedHash = (Get-FileHash -LiteralPath $copied -Algorithm SHA256).Hash
            if ($copiedHash -ne $hash) { throw "The copy in '$CopyTo' does not match the original (SHA-256 differs)." }
            Remove-OldAuto $CopyTo $Database $KeepAuto
            $copyNote = "copied and verified: $CopyTo"
            Write-Log "Second copy verified in $CopyTo"
        }
        else {
            $copyNote = "NOT copied ($CopyTo is not available)"
            Write-Log "WARNING: second copy skipped, '$CopyTo' is not available (is the USB stick / disk connected?)"
        }
    }

    Remove-OldAuto $OutDir $Database $KeepAuto

    Set-Content -Path $okFile -Encoding UTF8 -Value @(
        "Last successful automatic backup: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
        "File: $file",
        "Size: $mb MB",
        "SHA-256: $hash",
        "Second copy: $copyNote",
        'Not restore-tested by this script. Test an automatic file with: Test-BackupRestore.ps1 -ExistingBackup <file>'
    )
    if (Test-Path -LiteralPath $failFile) { Remove-Item -LiteralPath $failFile -Force }
    exit 0
}
catch {
    $msg = $_.Exception.Message
    Write-Log "FAILED: $msg"
    if ($msg -match 'password|authentication|pgpass') {
        Write-Log "Hint: check %APPDATA%\postgresql\pgpass.conf (one line: localhost:5432:$Database`:$User`:<password>) and that role '$User' exists."
    }
    Set-Content -Path $failFile -Encoding UTF8 -Value @(
        "The automatic backup FAILED at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss').",
        $msg,
        "See $log. Run  .\Backup-Database.ps1  by hand until this is fixed."
    )
    exit 1
}
