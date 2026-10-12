<#
 Scan-Secrets.ps1 - searches the working tree AND git history for likely secrets.
 Read-only. Values are MASKED in the output (never printed in full).
 Run from the repo root or from backend:   .\scripts\Scan-Secrets.ps1
 Exit code 1 if anything suspicious is found.
#>
param([switch]$SkipHistory)

$ErrorActionPreference = 'Stop'
$root = (git rev-parse --show-toplevel 2>$null)
if (-not $root) { throw 'Not inside a git repository.' }
Set-Location $root

$patterns = @(
  @{ Name='Password in connection string'; Rx='(?i)(password|pwd)\s*=\s*[^;"''\s]{3,}' },
  @{ Name='JWT key / secret assignment';   Rx='(?i)("?(jwt:?key|secret|apikey|api_key|client_secret)"?\s*[:=]\s*"[^"]{8,}")' },
  @{ Name='Private key block';             Rx='-----BEGIN (RSA |EC |OPENSSH |)PRIVATE KEY-----' },
  @{ Name='Bearer / long token literal';   Rx='(?i)bearer\s+[A-Za-z0-9\-_\.]{30,}' },
  @{ Name='AWS access key';                Rx='AKIA[0-9A-Z]{16}' },
  @{ Name='Generic hex/base64 secret';     Rx='(?i)(token|secret|key)\w*\s*[:=]\s*"[A-Za-z0-9+/=_\-]{32,}"' }
)

function Mask([string]$s) {
  $s = $s.Trim()
  if ($s.Length -gt 140) { $s = $s.Substring(0,140) }
  # mask anything after '=' or ':' value part
  return ($s -replace '(?i)((password|pwd|key|secret|token)\w*\s*[:=]\s*"?)([^;"\s]{2})[^;"\s]*', '$1$3***')
}

$found = 0

Write-Host '== Tracked + untracked files (working tree) ==' -ForegroundColor Cyan
$files = git ls-files --cached --others --exclude-standard |
  Where-Object { $_ -notmatch '(^|/)(node_modules|bin|obj|dist|e2e-report|e2e-results)/' -and $_ -notmatch '\.(png|jpg|ico|woff2?|lock)$' -and $_ -ne 'backend/scripts/Scan-Secrets.ps1' }
foreach ($f in $files) {
  if (-not (Test-Path $f -PathType Leaf)) { continue }
  foreach ($p in $patterns) {
    $m = Select-String -Path $f -Pattern $p.Rx -ErrorAction SilentlyContinue
    foreach ($hit in $m) {
      $found++
      Write-Host ("[{0}] {1}:{2}  {3}" -f $p.Name, $f, $hit.LineNumber, (Mask $hit.Line)) -ForegroundColor Yellow
    }
  }
}

Write-Host '== Suspicious file names (tracked) ==' -ForegroundColor Cyan
git ls-files | Where-Object { $_ -match '(^|/)(_dump\d*\.txt|\.env(\..*)?|.*\.pfx|.*\.pem|.*\.key|appsettings\.Production\.json|secrets\.json)$' } |
  ForEach-Object { $found++; Write-Host "[tracked suspicious file] $_" -ForegroundColor Yellow }

if (-not $SkipHistory) {
  Write-Host '== Git history (all commits; masked) ==' -ForegroundColor Cyan
  foreach ($p in $patterns) {
    $out = git log --all -G $p.Rx --pretty=format:'%h %ad %s' --date=short -- . ':(exclude)backend/scripts/Scan-Secrets.ps1' 2>$null
    if ($out) {
      $found++
      Write-Host "[$($p.Name)] touched in commits:" -ForegroundColor Yellow
      $out | Select-Object -First 15 | ForEach-Object { Write-Host "   $_" }
    }
  }
}

Write-Host ''
if ($found -eq 0) { Write-Host 'No suspicious items found.' -ForegroundColor Green; exit 0 }
Write-Host "$found suspicious item(s). Review each: false positives are common (placeholders, test code)." -ForegroundColor Red
Write-Host 'If a REAL secret appears: rotate it first, then remove it from history (git filter-repo) - deleting the file alone is not enough.'
exit 1
