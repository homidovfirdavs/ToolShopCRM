<#
 Resets the password of ONE local user (default: admin) directly in PostgreSQL by writing an
 ASP.NET Identity V3 password hash. The new password is typed here (hidden) and is never printed,
 logged or stored. Also clears the lockout. For LOCAL/DEV recovery only.
 Usage: powershell -ExecutionPolicy Bypass -File .\Reset-AdminPassword.ps1
#>
param(
  [string]$UserName = "admin",
  [string]$Database = "toolshop_crm",
  [string]$DbHost = "localhost",
  [string]$DbUser = "postgres"
)
$ErrorActionPreference = "Stop"

function Read-Plain($prompt) {
  $s = Read-Host $prompt -AsSecureString
  $b = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)
  try { [Runtime.InteropServices.Marshal]::PtrToStringAuto($b) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b) }
}

$p1 = Read-Plain "New password (min 8 chars)"
$p2 = Read-Plain "Repeat password"
if ($p1 -ne $p2) { throw "Passwords do not match." }
if ($p1.Length -lt 8) { throw "Password must be at least 8 characters." }

# ASP.NET Identity V3 format: 0x01 | prf(1=HMACSHA256) | iterations | saltLen | salt | subkey(32)
$iter = 100000
$salt = New-Object byte[] 16
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($salt)
$kdf = New-Object Security.Cryptography.Rfc2898DeriveBytes($p1, $salt, $iter, [Security.Cryptography.HashAlgorithmName]::SHA256)
$sub = $kdf.GetBytes(32)
function BE([int]$n) { $r = New-Object byte[] 4; $r[0] = [byte](($n -shr 24) -band 255); $r[1] = [byte](($n -shr 16) -band 255); $r[2] = [byte](($n -shr 8) -band 255); $r[3] = [byte]($n -band 255); ,$r }
$bytes = New-Object System.Collections.Generic.List[byte]
$bytes.Add(1)
$bytes.AddRange([byte[]](BE 1)); $bytes.AddRange([byte[]](BE $iter)); $bytes.AddRange([byte[]](BE 16))
$bytes.AddRange($salt); $bytes.AddRange($sub)
$hash = [Convert]::ToBase64String($bytes.ToArray())
$stamp = [guid]::NewGuid().ToString("N").ToUpper()
$p1 = $null; $p2 = $null

$norm = $UserName.ToUpperInvariant().Replace("'", "''")
$sql = "UPDATE ""AspNetUsers"" SET ""PasswordHash"" = '$hash', ""SecurityStamp"" = '$stamp', ""AccessFailedCount"" = 0, ""LockoutEnd"" = NULL WHERE ""NormalizedUserName"" = '$norm';"
$sql | psql -h $DbHost -U $DbUser -d $Database
Write-Host "Done. 'UPDATE 1' above means the password was changed. Now log in with the new password."
