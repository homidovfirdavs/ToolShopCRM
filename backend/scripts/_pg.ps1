# Shared helpers for the backup / restore scripts. Dot-source this file: . "$PSScriptRoot\_pg.ps1"
# The PostgreSQL password is read hidden, kept only in $env:PGPASSWORD of THIS process, and never written to disk
# or printed.

function Find-PgTool {
    param([string]$Name, [string]$BinPath)
    if ($BinPath) {
        $p = Join-Path $BinPath "$Name.exe"
        if (Test-Path $p) { return $p }
        throw "$Name.exe not found in '$BinPath'."
    }
    $cmd = Get-Command "$Name.exe" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($root in @("$env:ProgramFiles\PostgreSQL", "${env:ProgramFiles(x86)}\PostgreSQL")) {
        if (-not (Test-Path $root)) { continue }
        $dirs = Get-ChildItem $root -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending
        foreach ($d in $dirs) {
            $p = Join-Path $d.FullName "bin\$Name.exe"
            if (Test-Path $p) { return $p }
        }
    }
    throw "$Name.exe not found. Add PostgreSQL's 'bin' folder to PATH or pass -PgBinPath."
}

# Returns $true when this call set the password (the caller must then clear it in `finally`).
function Initialize-PgPassword {
    param([string]$User)
    if ($env:PGPASSWORD) { return $false }
    $sec = Read-Host "PostgreSQL password for '$User' (hidden)" -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
    try { $env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
    return $true
}

function Invoke-PgQuery {
    param([string]$Psql, [string]$PgHost, [int]$Port, [string]$User, [string]$Database, [string]$Sql)
    $out = & $Psql -h $PgHost -p $Port -U $User -d $Database -X -A -t -v ON_ERROR_STOP=1 -c $Sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql failed on '$Database': $($out -join ' ')" }
    return @($out | ForEach-Object { "$_".Trim() } | Where-Object { $_ -ne '' })
}

# table name -> row count for every table of the public schema
function Get-PgTableCounts {
    param([string]$Psql, [string]$PgHost, [int]$Port, [string]$User, [string]$Database)
    $tables = @(Invoke-PgQuery $Psql $PgHost $Port $User $Database "SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY 1")
    $counts = [ordered]@{}
    foreach ($t in $tables) {
        $n = @(Invoke-PgQuery $Psql $PgHost $Port $User $Database ('SELECT count(*) FROM public."' + $t + '"'))
        $counts[$t] = [int64]$n[0]
    }
    return $counts
}
