<#
 Rotate-DbCredentials.ps1 - moves the CRM from the 'postgres' superuser to a dedicated least-privilege role.

 What it does (in this order; it stops at the first problem):
   1. Backup + restore test of the CRM database (Test-BackupRestore.ps1) - nothing changes before this PASSES.
   2. Remembers the row count of every table.
   3. Creates/updates the role 'toolshop_app': LOGIN only - NOT superuser, no CREATEDB / CREATEROLE / REPLICATION,
      no DDL. It gets CONNECT, SELECT/INSERT/UPDATE/DELETE on the tables and USAGE on the sequences of schema public
      (+ default privileges for future migration tables). __EFMigrationsHistory is read-only for it.
   4. Generates a random 32-character password for that role (never printed) and writes the new connection string
      into the project's User Secrets (the old value is kept in memory only, for rollback).
   5. Verifies: the role can read data but CANNOT create/drop tables or roles; the Backend starts with the new
      connection string and /health/ready is Healthy; row counts are unchanged.
   6. With -RotateAdminPassword: asks (hidden, twice) for a NEW strong password for the 'postgres' account.

 Passwords are never printed, never put on a command line, and never written to disk except in User Secrets.
 Schema migrations (dotnet ef database update) must still be run with the admin account, not with toolshop_app.

 Example:  .\scripts\Rotate-DbCredentials.ps1 -RotateAdminPassword
#>
param(
    [string]$PgHost,
    [int]$Port = 0,
    [string]$AdminUser = 'postgres',
    [string]$Database,
    [string]$AppUser = 'toolshop_app',
    [string]$PgBinPath,
    [switch]$RotateAdminPassword,
    [switch]$SkipBackupTest
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\_pg.ps1"

$backendDir = Split-Path $PSScriptRoot -Parent
$apiDir = Join-Path $backendDir 'ToolShop.API'
$csproj = Join-Path $apiDir 'ToolShop.API.csproj'
if (-not (Test-Path $csproj)) { throw "Not found: $csproj" }

# ---- User Secrets file ----
$idMatch = Select-String -Path $csproj -Pattern '<UserSecretsId>([^<]+)</UserSecretsId>'
if (-not $idMatch) { throw 'UserSecretsId not found in ToolShop.API.csproj.' }
$secretsId = $idMatch.Matches[0].Groups[1].Value
$secretsPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$secretsId\secrets.json"
if (-not (Test-Path $secretsPath)) { throw 'User Secrets file not found. Configure the connection string first (README).' }
$secrets = Get-Content $secretsPath -Raw | ConvertFrom-Json
$connKey = 'ConnectionStrings:DefaultConnection'
$oldConn = $secrets.PSObject.Properties[$connKey].Value
if (-not $oldConn) { throw 'ConnectionStrings:DefaultConnection is not set in User Secrets.' }

function Get-ConnPart([string]$conn, [string]$name) {
    $m = [regex]::Match($conn, "(?i)(?:^|;)\s*$name\s*=\s*([^;]+)")
    if ($m.Success) { return $m.Groups[1].Value.Trim() }
    return $null
}
if (-not $PgHost) { $PgHost = Get-ConnPart $oldConn 'Host'; if (-not $PgHost) { $PgHost = 'localhost' } }
if ($Port -eq 0) { $p = Get-ConnPart $oldConn 'Port'; $Port = if ($p) { [int]$p } else { 5432 } }
if (-not $Database) { $Database = Get-ConnPart $oldConn 'Database'; if (-not $Database) { $Database = 'toolshop_crm' } }
if ($Database -match '_test$') { throw 'This script is for the real CRM database, not a *_test database.' }
if ($AppUser -notmatch '^[a-z_][a-z0-9_]*$') { throw 'AppUser may contain only lowercase letters, digits and underscore.' }
if ($Database -notmatch '^[A-Za-z0-9_]+$') { throw 'Unexpected characters in the database name.' }

function New-RandomPassword([int]$Length = 32) {
    $chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'.ToCharArray()
    $bytes = New-Object byte[] ($Length)
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $sb = New-Object Text.StringBuilder
    foreach ($b in $bytes) { [void]$sb.Append($chars[$b % $chars.Length]) }
    return $sb.ToString()
}

# Runs SQL read from STDIN (so secrets never appear on a command line). Output is masked.
function Invoke-PgSqlStdin {
    param([string]$Psql, [string]$Db, [string]$Sql, [string[]]$Mask = @())
    $out = $Sql | & $Psql -h $PgHost -p $Port -U $AdminUser -d $Db -X -q -v ON_ERROR_STOP=1 -f - 2>&1
    $code = $LASTEXITCODE
    $text = ($out | ForEach-Object { "$_" }) -join "`n"
    foreach ($m in $Mask) { if ($m) { $text = $text.Replace($m, '***') } }
    if ($code -ne 0) { throw "psql failed on '$Db': $text" }
}

function Read-SecretTwice([string]$Prompt) {
    for ($try = 1; $try -le 3; $try++) {
        $a = Read-Host "$Prompt (hidden, min 14 chars)" -AsSecureString
        $b = Read-Host 'Repeat it' -AsSecureString
        $pa = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($a))
        $pb = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($b))
        if ($pa -cne $pb) { Write-Host '  The two entries do not match. Try again.' -ForegroundColor Yellow; continue }
        if ($pa.Length -lt 14) { Write-Host '  Use at least 14 characters. Try again.' -ForegroundColor Yellow; continue }
        if ($pa -match "['""\\;]") { Write-Host '  Do not use quote, backslash or semicolon characters. Try again.' -ForegroundColor Yellow; continue }
        return $pa
    }
    throw 'No valid password entered after 3 attempts. Nothing was changed for this step.'
}

function Write-ConnectionString([string]$value) {
    $s = Get-Content $secretsPath -Raw | ConvertFrom-Json
    $s | Add-Member -NotePropertyName $connKey -NotePropertyValue $value -Force
    $json = $s | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText($secretsPath, $json, (New-Object Text.UTF8Encoding($false)))
}

$psql = Find-PgTool 'psql' $PgBinPath
$setPw = Initialize-PgPassword $AdminUser
$appPw = $null
$changedSecrets = $false
try {
    Write-Host "Target: $Database on ${PgHost}:$Port, admin '$AdminUser', new app role '$AppUser'" -ForegroundColor Cyan

    # ---- 1. backup + restore test ----
    if ($SkipBackupTest) {
        Write-Host 'WARNING: backup/restore test skipped by request.' -ForegroundColor Yellow
    } else {
        Write-Host '[1/6] Backup + restore test ...' -ForegroundColor Cyan
        & "$PSScriptRoot\Test-BackupRestore.ps1" -PgHost $PgHost -Port $Port -User $AdminUser -Database $Database -PgBinPath $PgBinPath
        if ($LASTEXITCODE -ne 0) { throw 'Backup/restore test FAILED. Nothing was changed.' }
    }

    # ---- 2. row counts before ----
    Write-Host '[2/6] Recording row counts ...' -ForegroundColor Cyan
    $before = Get-PgTableCounts $psql $PgHost $Port $AdminUser $Database

    # ---- 3. role + privileges ----
    Write-Host "[3/6] Creating role '$AppUser' with least privileges ..." -ForegroundColor Cyan
    $appPw = New-RandomPassword 32
    $roleSql = @'
SET log_statement = 'none';
DO $do$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '__ROLE__') THEN
    CREATE ROLE __ROLE__ LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
  END IF;
END
$do$;
ALTER ROLE __ROLE__ LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '__PW__';
'@
    Invoke-PgSqlStdin $psql 'postgres' ($roleSql.Replace('__ROLE__', $AppUser).Replace('__PW__', $appPw)) @($appPw)

    $grantSql = @'
GRANT CONNECT ON DATABASE "__DB__" TO __ROLE__;
GRANT USAGE ON SCHEMA public TO __ROLE__;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO __ROLE__;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO __ROLE__;
REVOKE INSERT, UPDATE, DELETE ON public."__EFMigrationsHistory" FROM __ROLE__;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO __ROLE__;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO __ROLE__;
'@
    Invoke-PgSqlStdin $psql $Database ($grantSql.Replace('__ROLE__', $AppUser).Replace('__DB__', $Database))

    # ---- 4. User Secrets ----
    Write-Host '[4/6] Updating User Secrets connection string ...' -ForegroundColor Cyan
    Write-ConnectionString "Host=$PgHost;Port=$Port;Database=$Database;Username=$AppUser;Password=$appPw"
    $changedSecrets = $true

    # ---- 5. verification ----
    Write-Host '[5/6] Verifying ...' -ForegroundColor Cyan
    $adminPw = $env:PGPASSWORD
    $env:PGPASSWORD = $appPw
    try {
        $who = @(Invoke-PgQuery $psql $PgHost $Port $AppUser $Database 'SELECT current_user')
        if ($who[0] -ne $AppUser) { throw "Unexpected current_user: $($who[0])" }
        $super = @(Invoke-PgQuery $psql $PgHost $Port $AppUser $Database 'SELECT rolsuper OR rolcreaterole OR rolcreatedb FROM pg_roles WHERE rolname = current_user')
        if ($super[0] -ne 'f') { throw 'The application role has elevated attributes.' }
        $null = Invoke-PgQuery $psql $PgHost $Port $AppUser $Database 'SELECT count(*) FROM public."Orders"'

        foreach ($forbidden in @('CREATE TABLE public.zz_probe(i int)', 'DROP TABLE public."AuditLogs"', 'CREATE ROLE zz_probe', 'TRUNCATE public."Orders"')) {
            $denied = $false
            try { $null = Invoke-PgQuery $psql $PgHost $Port $AppUser $Database $forbidden } catch { $denied = $true }
            if (-not $denied) { throw "SECURITY: the application role was allowed to run: $forbidden" }
        }
        Write-Host "  role '$AppUser': can read/write data; CREATE/DROP/TRUNCATE/CREATE ROLE are denied." -ForegroundColor Green
    }
    finally { $env:PGPASSWORD = $adminPw }

    # Backend start with the new connection string (reads User Secrets in Development)
    $listener = Get-NetTCPConnection -LocalPort 5252 -State Listen -ErrorAction SilentlyContinue
    if ($listener) { throw 'Port 5252 is in use: stop the running backend and run the script again (the role is already created).' }
    $oldEnv = @{ E = $env:ASPNETCORE_ENVIRONMENT; U = $env:ASPNETCORE_URLS }
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = 'http://localhost:5252'
    $log = Join-Path $env:TEMP 'toolshop-backend-rotate.log'
    $proc = $null
    try {
        $proc = Start-Process dotnet -ArgumentList @('run', '--no-launch-profile', '--project', $csproj) `
            -WorkingDirectory $apiDir -PassThru -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError "$log.err"
        $healthy = $false; $body = ''
        for ($i = 0; $i -lt 60 -and -not $healthy; $i++) {
            Start-Sleep -Seconds 2
            try {
                $r = Invoke-WebRequest -Uri 'http://localhost:5252/health/ready' -UseBasicParsing -TimeoutSec 5
                if ($r.StatusCode -eq 200) { $healthy = $true; $body = $r.Content }
            } catch { }
        }
        if (-not $healthy) { throw "Backend did not become healthy with the new connection string. See $log (it contains no password)." }
        Write-Host "  backend /health/ready: 200  $body" -ForegroundColor Green
    }
    finally {
        if ($proc) { & taskkill /PID $proc.Id /T /F 2>&1 | Out-Null }
        $env:ASPNETCORE_ENVIRONMENT = $oldEnv.E; $env:ASPNETCORE_URLS = $oldEnv.U
        Remove-Item $log, "$log.err" -ErrorAction SilentlyContinue
    }

    $after = Get-PgTableCounts $psql $PgHost $Port $AdminUser $Database
    $diff = @($before.Keys | Where-Object { $before[$_] -ne $after[$_] })
    if ($diff.Count -gt 0) { Write-Host "  note: row counts changed in: $($diff -join ', ') (the backend wrote while starting - e.g. RefreshTokens/AuditLogs)" -ForegroundColor Yellow }
    $lost = @($before.Keys | Where-Object { $after[$_] -lt $before[$_] })
    if ($lost.Count -gt 0) { throw "DATA LOSS check failed for: $($lost -join ', ')" }
    Write-Host "  data intact: $($before.Count) tables, no table lost rows." -ForegroundColor Green

    # ---- 6. admin password ----
    # Steps 1-5 are verified and complete: a failure below must NOT roll back the working restricted-role connection.
    $changedSecrets = $false
    if ($RotateAdminPassword) {
        Write-Host "[6/6] New password for '$AdminUser' ..." -ForegroundColor Cyan
        $newAdmin = Read-SecretTwice "New strong password for PostgreSQL account '$AdminUser'"
        $admSql = "SET log_statement = 'none';`nALTER ROLE $AdminUser PASSWORD '$newAdmin';"
        Invoke-PgSqlStdin $psql 'postgres' $admSql @($newAdmin)
        $env:PGPASSWORD = $newAdmin
        $ok = @(Invoke-PgQuery $psql $PgHost $Port $AdminUser $Database 'SELECT 1')
        if ($ok[0] -ne '1') { throw 'Could not connect with the new admin password.' }
        $newAdmin = $null
        Write-Host "  '$AdminUser' password changed and verified. Write it in your password manager now." -ForegroundColor Green
    } else {
        Write-Host '[6/6] Admin password NOT rotated (run again with -RotateAdminPassword).' -ForegroundColor Yellow
    }

    $changedSecrets = $false   # success: keep the new value
    Write-Host ''
    Write-Host 'DONE. The backend now connects as a restricted role. Next:' -ForegroundColor Green
    Write-Host "  - migrations: run 'dotnet ef database update' with the ADMIN account (a one-off connection string), never as $AppUser"
    Write-Host '  - Run-Tests.ps1 / Run-E2E.ps1 / Backup scripts will ask for the ADMIN password (the new one if you rotated it)'
}
catch {
    if ($changedSecrets) {
        Write-ConnectionString $oldConn
        Write-Host 'User Secrets connection string was RESTORED to the previous value.' -ForegroundColor Yellow
    }
    throw
}
finally {
    $appPw = $null
    if ($setPw) { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }
}
