<#
 Switches the second factor (MFA) OFF for ONE local user (default: admin) directly in PostgreSQL.
 Use it only when the user lost BOTH the authenticator app and the recovery codes and no other Admin can reset it
 (an Admin cannot reset their own MFA in the app).

 What it does, in ONE transaction:
   - AspNetUsers.TwoFactorEnabled = false, clears the lockout and the failed-attempt counter
   - deletes the authenticator key and the recovery codes of that user (AspNetUserTokens)
   - revokes every refresh token of that user (all sessions end)
 It does NOT touch the password. At the next login the user sets up MFA again (a new QR code and new recovery codes).

 Make a backup first:  .\Backup-Database.ps1
 Usage: powershell -ExecutionPolicy Bypass -File .\Reset-AdminMfa.ps1 [-UserName admin]
 psql asks for the PostgreSQL password itself (hidden). Nothing secret is printed or stored.
#>
param(
  [string]$UserName = "admin",
  [string]$Database = "toolshop_crm",
  [string]$DbHost = "localhost",
  [string]$DbUser = "postgres"
)
$ErrorActionPreference = "Stop"

Write-Host "This will switch MFA off for user '$UserName' in database '$Database' on $DbHost." -ForegroundColor Yellow
Write-Host "All sessions of that user end. The password is not changed." -ForegroundColor Yellow
$confirm = Read-Host "Type the user name '$UserName' to continue (anything else cancels)"
if ($confirm -ne $UserName) { Write-Host "Cancelled. Nothing was changed."; return }

$norm = $UserName.ToUpperInvariant().Replace("'", "''")
$sql = @"
BEGIN;
UPDATE "AspNetUsers" SET "TwoFactorEnabled" = false, "AccessFailedCount" = 0, "LockoutEnd" = NULL WHERE "NormalizedUserName" = '$norm';
DELETE FROM "AspNetUserTokens" WHERE "UserId" IN (SELECT "Id" FROM "AspNetUsers" WHERE "NormalizedUserName" = '$norm') AND "LoginProvider" = '[AspNetUserStore]' AND "Name" IN ('AuthenticatorKey', 'RecoveryCodes');
UPDATE "RefreshTokens" SET "RevokedAt" = now() WHERE "UserId" IN (SELECT "Id" FROM "AspNetUsers" WHERE "NormalizedUserName" = '$norm') AND "RevokedAt" IS NULL;
COMMIT;
"@
$sql | psql -h $DbHost -U $DbUser -d $Database -v ON_ERROR_STOP=1
Write-Host "Done. 'UPDATE 1' above means MFA was switched off for '$UserName'. At the next login the user sets MFA up again."
