<#
 Runs the Playwright end-to-end tests (POS, Dashboard, Debts, Login) against a REAL backend that is connected to a
 DEDICATED PostgreSQL test database.

 Safety:
  - The database name must end with "_test" (never use toolshop_crm). The database must already be migrated
    (run .\Run-Tests.ps1 once first: it applies the migrations).
  - Ports 5252 (API) and 5173 (frontend) must be FREE, so the tests can never talk to your normal dev backend / real database.
    Stop `dotnet run` and `npm run dev` before running this script.
  - The PostgreSQL password is typed hidden and lives only in this process's environment. The E2E admin user and the
    JWT key are random for every run and are never printed or written to disk.
  - The tests only ADD uniquely named data (E2E Product ..., E2E Customer ..., a new e2e_xxxxxxxx admin user) to the test database.

 Usage:  .\Run-E2E.ps1                 (all E2E tests)
         .\Run-E2E.ps1 -Grep "POS"     (only tests whose title matches)
         .\Run-E2E.ps1 -BrowserPath "C:\Program Files\Google\Chrome\Application\chrome.exe"
#>
param(
    [string]$PgHost = "localhost",
    [int]$Port = 5432,
    [string]$User = "postgres",
    [string]$Database = "toolshop_crm_test",
    [string]$Grep = "",
    [switch]$SkipBrowserInstall,
    [string]$BrowserPath = ""    # optional: full path to chrome.exe / msedge.exe
)

$ErrorActionPreference = "Stop"
if (-not $Database.EndsWith("_test")) { throw "Database name must end with '_test'." }

$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$apiProject = Join-Path $root "backend\ToolShop.API\ToolShop.API.csproj"
$frontend = Join-Path $root "frontend\toolshop-client"
$apiUrl = "http://localhost:5252"
$webUrl = "http://localhost:5173"

foreach ($p in 5252, 5173) {
    if (Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue) {
        throw "Port $p is already in use. Stop your running backend / frontend first, so E2E can never touch the real database."
    }
}

function New-RandomSecret([int]$bytes) {
    $b = New-Object byte[] $bytes
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
    return ([Convert]::ToBase64String($b)).Replace("+", "A").Replace("/", "b").Replace("=", "c")
}

$secure = Read-Host "PostgreSQL password for user '$User'" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try { $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }

$e2eUser = "e2e_" + (-join ((97..122) | Get-Random -Count 8 | ForEach-Object { [char]$_ }))   # new user per run (the seeder never resets an existing user's password)
$e2ePass = "E2e-" + (New-RandomSecret 18) + "-9a"

$env:ConnectionStrings__DefaultConnection = "Host=$PgHost;Port=$Port;Database=$Database;Username=$User;Password=`"$plain`""
$plain = $null
$env:ASPNETCORE_ENVIRONMENT = "Testing"
$env:ASPNETCORE_URLS = $apiUrl
$env:Jwt__Key = New-RandomSecret 48
$env:Jwt__Issuer = "toolshop-e2e"
$env:Jwt__Audience = "toolshop-e2e"
$env:Https__Redirect = "false"
$env:RateLimiting__AuthPermitPerMinute = "100000"
$env:RateLimiting__GlobalPermitPerMinute = "1000000"
$env:AdminUser__Username = $e2eUser
$env:AdminUser__Email = "$e2eUser@test.local"
$env:AdminUser__Password = $e2ePass
$env:E2E_ADMIN_USER = $e2eUser
$env:E2E_ADMIN_PASSWORD = $e2ePass
$env:E2E_BASE_URL = $webUrl
$env:E2E_API_URL = $apiUrl

$logDir = Join-Path $env:TEMP "toolshop-e2e"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$apiProc = $null
$webProc = $null
$exit = 1

function Wait-For([string]$url, [int]$seconds, [string]$name) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        try { Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3 | Out-Null; return } catch { Start-Sleep -Milliseconds 800 }
    }
    throw "$name did not become ready at $url within $seconds s."
}

try {
    Write-Host "Starting backend on $apiUrl (database: $Database)..."
    $apiProc = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--project", "`"$apiProject`"", "--no-launch-profile") `
        -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $logDir "api.out.log") -RedirectStandardError (Join-Path $logDir "api.err.log")
    try { Wait-For "$apiUrl/health/live" 120 "Backend" }
    catch {
        Write-Host "Backend log (last lines) in $logDir :" -ForegroundColor Yellow
        Get-Content (Join-Path $logDir "api.err.log") -Tail 15 -ErrorAction SilentlyContinue
        Get-Content (Join-Path $logDir "api.out.log") -Tail 15 -ErrorAction SilentlyContinue
        Write-Host "Hint: the test database must be migrated - run .\Run-Tests.ps1 once first." -ForegroundColor Yellow
        throw
    }

    Write-Host "Starting frontend on $webUrl ..."
    $webProc = Start-Process -FilePath "cmd.exe" -ArgumentList @("/c", "npm run dev -- --port 5173 --strictPort") `
        -WorkingDirectory $frontend -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $logDir "web.out.log") -RedirectStandardError (Join-Path $logDir "web.err.log")
    Wait-For $webUrl 90 "Frontend"

    Push-Location $frontend
    try {
        # Call the locally installed Playwright directly (no npx: npx may ask to download packages).
        $pwCli = Join-Path $frontend "node_modules\@playwright\test\cli.js"
        if (-not (Test-Path $pwCli)) { throw "Playwright is not installed. Run 'npm install' in $frontend first." }
        # Prefer an already installed Chrome / Edge (no download needed); fall back to Playwright's own Chromium.
        $candidates = @($BrowserPath,
            "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
            "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
            "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe",
            "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
            "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { $_ -and (Test-Path $_) }
        if ($candidates) {
            $env:E2E_CHROMIUM_PATH = @($candidates)[0]
            Write-Host "Using installed browser: $env:E2E_CHROMIUM_PATH"
        } elseif (-not $SkipBrowserInstall) {
            & node $pwCli install chromium
        }
        $pwArgs = @($pwCli, "test")
        if ($Grep) { $pwArgs += @("--grep", $Grep) }
        & node @pwArgs
        $exit = $LASTEXITCODE
    } finally { Pop-Location }
}
finally {
    foreach ($p in @($webProc, $apiProc)) {
        if ($p -and -not $p.HasExited) { & taskkill /PID $p.Id /T /F | Out-Null }
    }
    foreach ($n in "ConnectionStrings__DefaultConnection", "Jwt__Key", "AdminUser__Password", "E2E_ADMIN_PASSWORD",
                   "AdminUser__Username", "AdminUser__Email", "E2E_ADMIN_USER", "ASPNETCORE_ENVIRONMENT", "ASPNETCORE_URLS",
                   "Jwt__Issuer", "Jwt__Audience", "Https__Redirect", "RateLimiting__AuthPermitPerMinute",
                   "RateLimiting__GlobalPermitPerMinute", "E2E_BASE_URL", "E2E_API_URL", "E2E_CHROMIUM_PATH") {
        Remove-Item "Env:\$n" -ErrorAction SilentlyContinue
    }
}
Write-Host "HTML report: frontend\toolshop-client\e2e-report\index.html (npx playwright show-report e2e-report)"
exit $exit
