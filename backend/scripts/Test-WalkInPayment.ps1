<#
  TASK 11 - Walk-in payment enforcement: live API regression test.
  Run with the backend already running (default http://localhost:5252).
  Usage:  powershell -ExecutionPolicy Bypass -File .\Test-WalkInPayment.ps1 -User admin
  NOTE: it creates a few REAL test sales (cheapest in-stock product, qty 1). It never deletes/edits old data.
#>
param(
  [string]$BaseUrl = "http://localhost:5252/api",
  [string]$User = "admin",
  [string]$Password
)
$ErrorActionPreference = "Stop"
if (-not $Password) {
  $sec = Read-Host "Password for $User" -AsSecureString
  $Password = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec))
}

function Call($Method, $Path, $Body = $null, $Token = $script:Token) {
  $h = @{}
  if ($Token) { $h["Authorization"] = "Bearer $Token" }
  $req = @{ Method = $Method; Uri = "$BaseUrl$Path"; Headers = $h; UseBasicParsing = $true }
  if ($null -ne $Body) { $req["Body"] = ($Body | ConvertTo-Json -Depth 6); $req["ContentType"] = "application/json" }
  try {
    $r = Invoke-WebRequest @req
    $json = if ($r.Content) { $r.Content | ConvertFrom-Json } else { $null }
    return [pscustomobject]@{ Status = [int]$r.StatusCode; Json = $json }
  } catch {
    $status = 0; $text = ""
    if ($_.Exception.Response) {
      $status = [int]$_.Exception.Response.StatusCode
      if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $text = $_.ErrorDetails.Message }
      else { try { $text = (New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd() } catch {} }
    }
    $json = $null; try { if ($text) { $json = $text | ConvertFrom-Json } } catch {}
    return [pscustomobject]@{ Status = $status; Json = $json }
  }
}

$script:Results = @()
function Check($Name, [bool]$Ok, $Detail = "") {
  $script:Results += [pscustomobject]@{ Test = $Name; Result = $(if ($Ok) { "PASS" } else { "FAIL" }); Detail = $Detail }
  Write-Host ("[{0}] {1}  {2}" -f $(if ($Ok) { "PASS" } else { "FAIL" }), $Name, $Detail) -ForegroundColor $(if ($Ok) { "Green" } else { "Red" })
}

# ---------- login ----------
$login = Call "POST" "/auth/login" @{ usernameOrEmail = $User; password = $Password } $null
if ($login.Status -ne 200) { throw "Login failed (HTTP $($login.Status))." }
if ($login.Json.mfaRequired) { throw "This user must use MFA, so the script cannot sign in with a password only. Use a Cashier user for this check, or run it against a test API where Security__MfaRequiredRoles is switched off." }
$script:Token = $login.Json.accessToken

# ---------- fixtures ----------
$stock = Call "GET" "/inventory/stock?pageSize=100"
$prod = $stock.Json.items | Where-Object { $_.currentStock -ge 8 } | Select-Object -First 1
if (-not $prod) { throw "Need a product with stock >= 8 (add stock-in first)." }
$pid_ = $prod.productId
$cust = (Call "GET" "/customers?pageSize=50").Json.items | Where-Object { $_.isActive } | Select-Object -First 1
if (-not $cust) { throw "Need at least one active customer." }
Write-Host "Product #$pid_ '$($prod.productName)' stock=$($prod.currentStock); customer #$($cust.id) '$($cust.name)'"

function StockOf { (Call "GET" "/inventory/stock/$pid_").Json.currentStock }
function NewOrder($CustomerId) {
  $r = Call "POST" "/orders" @{ customerId = $CustomerId; notes = "walkin-test"; discountAmount = 0; items = @(@{ productId = $pid_; quantity = 1; discountAmount = 0 }) }
  if ($r.Status -ne 201) { throw "Cannot create order (HTTP $($r.Status))." }
  return $r.Json
}
function Pay($Amount) { @{ initialPayment = @{ amount = $Amount; method = "Cash"; referenceNumber = ""; notes = "" } } }
function OrderStatus($Id) { (Call "GET" "/orders/$Id").Json.status }
function PaidOf($Id) { (Call "GET" "/orders/$Id/payments").Json }

# ---------- baseline (historical walk-in debt) ----------
$debt0 = (Call "GET" "/debts?pageSize=100").Json
$w0 = [decimal]$debt0.walkInUnresolvedDebt; $n0 = [int]$debt0.walkInUnresolvedOrderCount
Write-Host "Baseline historical walk-in debt: $w0 TJS in $n0 orders"

# ---------- T1 walk-in + full ----------
$o = NewOrder $null; $s0 = StockOf
$r = Call "POST" "/orders/$($o.id)/complete" (Pay $o.totalAmount)
$p = PaidOf $o.id
Check "Walk-in + Full payment -> PASS" ($r.Status -eq 200 -and (OrderStatus $o.id) -eq "Completed" -and [decimal]$p.totalPaid -eq [decimal]$o.totalAmount -and [decimal]$p.outstandingBalance -eq 0 -and (StockOf) -eq ($s0 - 1)) "HTTP $($r.Status), paid=$($p.totalPaid)/$($o.totalAmount), stock $s0->$(StockOf)"

# ---------- T2 walk-in + partial ----------
$o = NewOrder $null; $s0 = StockOf
$half = [math]::Round([decimal]$o.totalAmount / 2, 2)
$r = Call "POST" "/orders/$($o.id)/complete" (Pay $half)
Check "Walk-in + Partial payment -> REJECT" ($r.Status -eq 400 -and $r.Json.code -eq "WALKIN_FULL_PAYMENT_REQUIRED" -and (OrderStatus $o.id) -eq "Draft" -and (StockOf) -eq $s0 -and @((PaidOf $o.id).payments).Count -eq 0) "HTTP $($r.Status), code=$($r.Json.code), status=$(OrderStatus $o.id), stock $s0->$(StockOf)"
$null = Call "POST" "/orders/$($o.id)/cancel"

# ---------- T3 walk-in + no payment ----------
$o = NewOrder $null; $s0 = StockOf
$r = Call "POST" "/orders/$($o.id)/complete"
Check "Walk-in + No payment -> REJECT" ($r.Status -eq 400 -and $r.Json.code -eq "WALKIN_FULL_PAYMENT_REQUIRED" -and (OrderStatus $o.id) -eq "Draft" -and (StockOf) -eq $s0) "HTTP $($r.Status), status=$(OrderStatus $o.id), stock $s0->$(StockOf)"
$null = Call "POST" "/orders/$($o.id)/cancel"

# ---------- T4 registered + partial ----------
$o = NewOrder $cust.id
$half = [math]::Round([decimal]$o.totalAmount / 2, 2)
$r = Call "POST" "/orders/$($o.id)/complete" (Pay $half)
$p = PaidOf $o.id
Check "Registered + Partial payment -> PASS" ($r.Status -eq 200 -and (OrderStatus $o.id) -eq "Completed" -and [decimal]$p.totalPaid -eq $half -and [decimal]$p.outstandingBalance -gt 0) "HTTP $($r.Status), paid=$($p.totalPaid), debt=$($p.outstandingBalance)"

# ---------- T5 registered + unpaid ----------
$o = NewOrder $cust.id
$r = Call "POST" "/orders/$($o.id)/complete"
$p = PaidOf $o.id
Check "Registered + Unpaid -> PASS" ($r.Status -eq 200 -and (OrderStatus $o.id) -eq "Completed" -and [decimal]$p.totalPaid -eq 0 -and [decimal]$p.outstandingBalance -eq [decimal]$o.totalAmount) "HTTP $($r.Status), debt=$($p.outstandingBalance)"

# ---------- T6 payment failure (overpayment) -> nothing persisted ----------
$o = NewOrder $cust.id; $s0 = StockOf
$r = Call "POST" "/orders/$($o.id)/complete" (Pay ([decimal]$o.totalAmount + 1))
Check "Payment failure -> no completed order / no stock deduction / no payment" ($r.Status -eq 409 -and (OrderStatus $o.id) -eq "Draft" -and (StockOf) -eq $s0 -and @((PaidOf $o.id).payments).Count -eq 0) "HTTP $($r.Status), status=$(OrderStatus $o.id), stock $s0->$(StockOf)"
$null = Call "POST" "/orders/$($o.id)/cancel"

# ---------- T7 double submit (parallel) ----------
$o = NewOrder $null; $s0 = StockOf
$body = (Pay $o.totalAmount)
$jobs = 1..2 | ForEach-Object {
  Start-Job -ArgumentList $BaseUrl, $script:Token, $o.id, ($body | ConvertTo-Json -Depth 5) -ScriptBlock {
    param($b, $t, $id, $json)
    try {
      $r = Invoke-WebRequest -Method Post -Uri "$b/orders/$id/complete" -Headers @{ Authorization = "Bearer $t" } -Body $json -ContentType "application/json" -UseBasicParsing
      [int]$r.StatusCode
    } catch { if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 } }
  }
}
$codes = $jobs | Wait-Job | Receive-Job; $jobs | Remove-Job
$p = PaidOf $o.id
$ok200 = @($codes | Where-Object { $_ -eq 200 }).Count
Check "Double submit -> exactly one sale and one payment" ($ok200 -eq 1 -and @($p.payments).Count -eq 1 -and (StockOf) -eq ($s0 - 1)) "codes=$($codes -join ','), payments=$(@($p.payments).Count), stock $s0->$(StockOf)"

# ---------- T8 historical walk-in debt unchanged ----------
$debt1 = (Call "GET" "/debts?pageSize=100").Json
Check "Historical Walk-in debt unchanged" ([decimal]$debt1.walkInUnresolvedDebt -eq $w0 -and [int]$debt1.walkInUnresolvedOrderCount -eq $n0) "before=$w0/$n0 after=$($debt1.walkInUnresolvedDebt)/$($debt1.walkInUnresolvedOrderCount)"

# ---------- T9 Dashboard = Finance = Debts = Reports ----------
$from = "2026-01-01"; $to = (Get-Date).AddDays(1).ToString("yyyy-MM-dd")
$dash = (Call "GET" "/dashboard/summary?from=$from&to=$to").Json
$fin  = (Call "GET" "/finance/summary?from=$from&to=$to").Json
$rep  = (Call "GET" "/reports/sales/summary?from=$from&to=$to").Json
$all = @(); $pg = 1
do { $d = (Call "GET" "/debts?pageSize=100&page=$pg").Json; $all += @($d.items); $pg++ } while ($pg -le [int]$d.totalPages)
$custDebt = [decimal](($all | Measure-Object totalDebt -Sum).Sum)
$debtsTotal = $custDebt + [decimal]$d.walkInUnresolvedDebt
Check "Dashboard = Finance = Debts(+walk-in) = Sales report(Completed)" (
  [decimal]$dash.accountsReceivable -eq [decimal]$fin.accountsReceivable -and
  [decimal]$fin.accountsReceivable -eq $debtsTotal -and
  [decimal]$fin.accountsReceivable -eq [decimal]$rep.completedOutstandingBalance) "dashboard=$($dash.accountsReceivable) finance=$($fin.accountsReceivable) debts=$debtsTotal (customers $custDebt + walk-in $($d.walkInUnresolvedDebt)) report=$($rep.completedOutstandingBalance)"

Write-Host ""; $script:Results | Format-Table -AutoSize
$fail = @($script:Results | Where-Object { $_.Result -eq "FAIL" }).Count
Write-Host ("TOTAL: {0} PASS, {1} FAIL" -f (@($script:Results).Count - $fail), $fail) -ForegroundColor $(if ($fail) { "Red" } else { "Green" })
