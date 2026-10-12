<#
 Runs the backend integration tests against a DEDICATED PostgreSQL test database.
 - The database name must end with "_test" (the tests refuse to run otherwise) and ALL business data in it is deleted
   before every test. Never point this at toolshop_crm.
 - The password is typed hidden and lives only in this process's environment variable; it is never written to disk.
 Usage:  .\Run-Tests.ps1                      (all tests)
         .\Run-Tests.ps1 -Filter WalkIn       (only tests whose name contains WalkIn)
#>
param(
    [string]$PgHost = "localhost",
    [int]$Port = 5432,
    [string]$User = "postgres",
    [string]$Database = "toolshop_crm_test",
    [string]$Filter = ""
)

if (-not $Database.EndsWith("_test")) { throw "Database name must end with '_test'." }

$secure = Read-Host "PostgreSQL password for user '$User'" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    $env:TOOLSHOP_TEST_CONNECTION = "Host=$PgHost;Port=$Port;Database=$Database;Username=$User;Password=`"$plain`""
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    $plain = $null
}

$project = Join-Path $PSScriptRoot "..\ToolShop.Tests\ToolShop.Tests.csproj"
try {
    $args = @("test", $project, "--logger", "console;verbosity=normal")
    if ($Filter) { $args += @("--filter", "FullyQualifiedName~$Filter") }
    & dotnet @args
    exit $LASTEXITCODE
} finally {
    Remove-Item Env:\TOOLSHOP_TEST_CONNECTION -ErrorAction SilentlyContinue
}
