using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToolShop.API.DTOs.Cash;
using ToolShop.API.DTOs.Catalog;
using ToolShop.Domain.Authorization;
using ToolShop.Domain.Entities.Cash;
using ToolShop.Infrastructure.Cash;
using ToolShop.Infrastructure.Idempotency;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.API.Controllers;

/// <summary>
/// Cash shifts: open / close a drawer, count the cash, put money in / take it out. Works only when CashShifts:Enabled is true.
/// A cashier works with their own shift; a manager / admin sees and fixes every shift. While a shift is open the EXPECTED cash is
/// shown only to people who may review it (the cashier counts first, then learns the difference).
/// </summary>
[ApiController]
[Route("api/cash-shifts")]
[Authorize]
public class CashShiftsController : ControllerBase
{
    private const int MaxPageSize = 100;
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string ReplayHeader = "Idempotent-Replay";

    private static readonly System.Text.RegularExpressions.Regex IdempotencyKeyFormat =
        new("^[A-Za-z0-9_-]{16,64}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly CashShiftService _service;
    private readonly IAuthorizationService _authorization;

    public CashShiftsController(AppDbContext db, CashShiftService service, IAuthorizationService authorization)
    {
        _db = db;
        _service = service;
        _authorization = authorization;
    }

    private string CurrentUserId =>
        User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value ?? string.Empty;

    private async Task<bool> HasAsync(string permission) =>
        (await _authorization.AuthorizeAsync(User, permission)).Succeeded;

    private IActionResult? DisabledResult() =>
        _service.Options.Enabled
            ? null
            : StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { code = "CASH_SHIFTS_DISABLED", message = "Cash shifts are not enabled." });

    // ------------------------------------------------------------------ responses

    private async Task<Dictionary<string, string>> NamesAsync(params string?[] ids)
    {
        var list = ids.Where(i => !string.IsNullOrEmpty(i)).Select(i => i!).Distinct().ToList();
        if (list.Count == 0) return new Dictionary<string, string>();
        var users = await _db.Users.AsNoTracking().Where(u => list.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName }).ToListAsync();
        return users.ToDictionary(u => u.Id, u => u.UserName ?? "Unknown");
    }

    private static string? Name(Dictionary<string, string> names, string? id) =>
        id is null ? null : names.GetValueOrDefault(id, "Unknown");

    private async Task<object> ToResponseAsync(CashShift s, CashBreakdown? breakdown = null)
    {
        breakdown ??= await CashShiftCalculator.ComputeAsync(_db, s);
        var names = await NamesAsync(s.CashierUserId, s.OpenedByUserId, s.ClosedByUserId, s.ReviewedByUserId);
        var registerName = await _db.CashRegisters.AsNoTracking()
            .Where(r => r.Id == s.CashRegisterId).Select(r => r.Name).FirstOrDefaultAsync();

        var closed = s.Status == CashShiftStatus.Closed;
        var canSeeExpected = closed || await HasAsync(Permissions.CashShiftsReview);

        return new
        {
            id = s.Id,
            shiftNumber = s.ShiftNumber,
            cashRegisterId = s.CashRegisterId,
            cashRegisterName = registerName,
            status = s.Status.ToString(),
            cashierUserId = s.CashierUserId,
            cashierName = Name(names, s.CashierUserId),
            openedAt = s.OpenedAt,
            openedByName = Name(names, s.OpenedByUserId),
            openingCash = s.OpeningCash,
            closedAt = s.ClosedAt,
            closedByName = Name(names, s.ClosedByUserId),
            closingCash = s.ClosingCash,                         // null (N/A) while open
            expectedCash = closed ? s.ExpectedCashAtClose : (canSeeExpected ? breakdown.ExpectedCash : (decimal?)null),
            cashDifference = s.CashDifference,                   // null (N/A) while open
            closeNote = s.CloseNote,
            closedByOther = s.ClosedByOther,
            requiresReview = s.RequiresReview,
            reviewed = s.ReviewedAt != null,
            reviewedAt = s.ReviewedAt,
            reviewedByName = Name(names, s.ReviewedByUserId),
            reviewNote = s.ReviewNote,
            breakdown = new
            {
                cashSales = canSeeExpected ? breakdown.CashSales : (decimal?)null,
                cashRefunds = canSeeExpected ? breakdown.CashRefunds : (decimal?)null,
                cashPaymentsVoidedFromOtherShifts = canSeeExpected ? breakdown.CashPaymentsVoidedFromOtherShifts : (decimal?)null,
                cashIn = canSeeExpected ? breakdown.CashIn : (decimal?)null,
                cashOut = canSeeExpected ? breakdown.CashOut : (decimal?)null,
                cardSales = breakdown.CardSales,
                bankTransferSales = breakdown.BankTransferSales,
                qrSales = breakdown.QrSales,
                otherSales = breakdown.OtherSales,
                paymentCount = breakdown.PaymentCount,
                refundCount = breakdown.RefundCount,
                movementCount = breakdown.MovementCount
            }
        };
    }

    private async Task<object> MovementResponseAsync(CashMovement m)
    {
        var names = await NamesAsync(m.CreatedByUserId, m.VoidedByUserId);
        return new
        {
            id = m.Id,
            cashShiftId = m.CashShiftId,
            type = m.Type.ToString(),
            amount = m.Amount,
            reason = m.Reason,
            expenseId = m.ExpenseId,
            createdAt = m.CreatedAt,
            createdByName = Name(names, m.CreatedByUserId),
            isVoided = m.IsVoided,
            voidedAt = m.VoidedAt,
            voidedByName = Name(names, m.VoidedByUserId),
            voidReason = m.VoidReason
        };
    }

    // ------------------------------------------------------------------ reads

    [HttpGet("registers")]
    [Authorize(Policy = Permissions.CashShiftsView)]
    public async Task<IActionResult> GetRegisters()
    {
        if (DisabledResult() is { } off) return off;

        var openRegisterIds = await _db.CashShifts.AsNoTracking()
            .Where(s => s.Status == CashShiftStatus.Open).Select(s => s.CashRegisterId).ToListAsync();
        var registers = await _db.CashRegisters.AsNoTracking().OrderBy(r => r.Id).ToListAsync();

        return Ok(registers.Select(r => new
        {
            id = r.Id,
            name = r.Name,
            isActive = r.IsActive,
            hasOpenShift = openRegisterIds.Contains(r.Id)
        }));
    }

    /// <summary>The open shift (if any) plus the cash the previous shift closed with, so the next cashier can compare.</summary>
    [HttpGet("current")]
    [Authorize(Policy = Permissions.CashShiftsView)]
    public async Task<IActionResult> GetCurrent([FromQuery] int? registerId)
    {
        if (DisabledResult() is { } off) return off;

        var shift = await _db.CashShifts.AsNoTracking()
            .Where(s => s.Status == CashShiftStatus.Open && (registerId == null || s.CashRegisterId == registerId))
            .OrderBy(s => s.OpenedAt)
            .FirstOrDefaultAsync();

        if (shift is null)
        {
            var previousClosing = await _db.CashShifts.AsNoTracking()
                .Where(s => s.Status == CashShiftStatus.Closed && (registerId == null || s.CashRegisterId == registerId))
                .OrderByDescending(s => s.ClosedAt)
                .Select(s => s.ClosingCash)
                .FirstOrDefaultAsync();

            return Ok(new { isOpen = false, previousClosingCash = previousClosing });
        }

        var movements = await _db.CashMovements.AsNoTracking()
            .Where(m => m.CashShiftId == shift.Id).OrderBy(m => m.Id).ToListAsync();
        var movementResponses = new List<object>();
        foreach (var m in movements) movementResponses.Add(await MovementResponseAsync(m));

        return Ok(new { isOpen = true, shift = await ToResponseAsync(shift), movements = movementResponses });
    }

    [HttpGet]
    [Authorize(Policy = Permissions.CashShiftsView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] CashShiftStatus? status,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (DisabledResult() is { } off) return off;
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        var query = _db.CashShifts.AsNoTracking().AsQueryable();
        if (!await HasAsync(Permissions.CashShiftsViewAll))
        {
            var me = CurrentUserId;
            query = query.Where(s => s.CashierUserId == me);
        }

        if (status.HasValue) query = query.Where(s => s.Status == status.Value);
        if (fromDate.HasValue) query = query.Where(s => s.OpenedAt >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(s => s.OpenedAt <= toDate.Value);
        query = query.OrderByDescending(s => s.OpenedAt);

        var totalItems = await query.CountAsync();
        var shifts = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var names = await NamesAsync(shifts.Select(s => s.CashierUserId).ToArray());

        var items = shifts.Select(s => new
        {
            id = s.Id,
            shiftNumber = s.ShiftNumber,
            status = s.Status.ToString(),
            cashierName = Name(names, s.CashierUserId),
            openedAt = s.OpenedAt,
            closedAt = s.ClosedAt,
            openingCash = s.OpeningCash,
            closingCash = s.ClosingCash,
            expectedCash = s.ExpectedCashAtClose,
            cashDifference = s.CashDifference,
            requiresReview = s.RequiresReview,
            reviewed = s.ReviewedAt != null
        }).ToList();

        return Ok(new PagedResult<object>
        {
            Items = items.Cast<object>().ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
        });
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = Permissions.CashShiftsView)]
    public async Task<IActionResult> GetById(int id)
    {
        if (DisabledResult() is { } off) return off;

        var shift = await _db.CashShifts.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (shift is null) return NotFound(new { message = "Cash shift not found." });
        if (shift.CashierUserId != CurrentUserId && !await HasAsync(Permissions.CashShiftsViewAll))
        {
            return NotFound(new { message = "Cash shift not found." });
        }

        var movements = await _db.CashMovements.AsNoTracking()
            .Where(m => m.CashShiftId == id).OrderBy(m => m.Id).ToListAsync();
        var movementResponses = new List<object>();
        foreach (var m in movements) movementResponses.Add(await MovementResponseAsync(m));

        return Ok(new { shift = await ToResponseAsync(shift), movements = movementResponses });
    }

    // ------------------------------------------------------------------ open / close

    [HttpPost("open")]
    [Authorize(Policy = Permissions.CashShiftsOperate)]
    public async Task<IActionResult> Open([FromBody] OpenCashShiftRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (DisabledResult() is { } off) return off;

        try
        {
            var shift = await _service.OpenAsync(request.CashRegisterId, request.OpeningCash!.Value, CurrentUserId);
            return CreatedAtAction(nameof(GetById), new { id = shift.Id }, await ToResponseAsync(shift));
        }
        catch (CashShiftAlreadyOpenException ex)
        {
            return Conflict(new { code = "CASH_SHIFT_ALREADY_OPEN", message = ex.Message, shiftId = ex.ShiftId });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/close")]
    [Authorize(Policy = Permissions.CashShiftsOperate)]
    public async Task<IActionResult> Close(int id, [FromBody] CloseCashShiftRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (DisabledResult() is { } off) return off;

        try
        {
            var privileged = await HasAsync(Permissions.CashShiftsReview);
            var (shift, breakdown) = await _service.CloseAsync(
                id, request.ActualCash!.Value, request.Note, CurrentUserId, privileged);
            return Ok(await ToResponseAsync(shift, breakdown));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Cash shift not found." });
        }
        catch (CashShiftNotOpenException ex)
        {
            return Conflict(new { code = "CASH_SHIFT_NOT_OPEN", message = ex.Message });
        }
        catch (CashShiftForbiddenException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "CASH_SHIFT_NOT_YOURS", message = ex.Message });
        }
        catch (CashDifferenceNeedsNoteException ex)
        {
            return BadRequest(new
            {
                code = "CASH_DIFFERENCE_NOTE_REQUIRED",
                message = ex.Message,
                expectedCash = ex.Expected,
                cashDifference = ex.Difference
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ------------------------------------------------------------------ cash in / out

    [HttpPost("{id:int}/movements")]
    [Authorize(Policy = Permissions.CashShiftsOperate)]
    public async Task<IActionResult> AddMovement(int id, [FromBody] CashMovementRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (DisabledResult() is { } off) return off;

        var key = Request.Headers[IdempotencyKeyHeader].ToString().Trim();
        if (string.IsNullOrEmpty(key))
        {
            return BadRequest(new { code = "IDEMPOTENCY_KEY_REQUIRED", message = "The Idempotency-Key header is required for cash movements." });
        }

        if (!IdempotencyKeyFormat.IsMatch(key))
        {
            return BadRequest(new { code = "IDEMPOTENCY_KEY_INVALID", message = "The Idempotency-Key must be 16-64 characters: letters, digits, '-' or '_'." });
        }

        try
        {
            var privileged = await HasAsync(Permissions.CashShiftsReview);
            var (movement, replayed) = await _service.AddMovementAsync(
                id, request.Type!.Value, request.Amount!.Value, request.Reason, request.ExpenseId,
                CurrentUserId, privileged, key);

            if (replayed) Response.Headers[ReplayHeader] = "true";
            return StatusCode(StatusCodes.Status201Created, await MovementResponseAsync(movement));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Cash shift not found." });
        }
        catch (OperationKeyReusedException ex)
        {
            return Conflict(new { code = "IDEMPOTENCY_KEY_REUSED", message = ex.Message });
        }
        catch (CashShiftNotOpenException ex)
        {
            return Conflict(new { code = "CASH_SHIFT_NOT_OPEN", message = ex.Message });
        }
        catch (CashShiftForbiddenException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "CASH_SHIFT_NOT_YOURS", message = ex.Message });
        }
        catch (CashMovementLimitException ex)
        {
            return Conflict(new { code = "CASH_MOVEMENT_LIMIT", message = ex.Message, limit = ex.Limit });
        }
        catch (CashOutExceedsDrawerException)
        {
            // The numbers are left out on purpose: the cashier must not learn the expected cash before counting.
            return Conflict(new { code = "CASH_OUT_EXCEEDS_DRAWER", message = "There is not enough cash in the drawer for this cash-out." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("movements/{movementId:int}/void")]
    [Authorize(Policy = Permissions.CashShiftsReview)]
    public async Task<IActionResult> VoidMovement(int movementId, [FromBody] VoidCashMovementRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (DisabledResult() is { } off) return off;

        try
        {
            var movement = await _service.VoidMovementAsync(movementId, request.Reason, CurrentUserId);
            return Ok(await MovementResponseAsync(movement));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Cash movement not found." });
        }
        catch (CashMovementAlreadyVoidedException ex)
        {
            return Conflict(new { code = "CASH_MOVEMENT_ALREADY_VOIDED", message = ex.Message });
        }
        catch (CashShiftNotOpenException ex)
        {
            return Conflict(new { code = "CASH_SHIFT_NOT_OPEN", message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ------------------------------------------------------------------ review

    [HttpPost("{id:int}/review")]
    [Authorize(Policy = Permissions.CashShiftsReview)]
    public async Task<IActionResult> Review(int id, [FromBody] ReviewCashShiftRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (DisabledResult() is { } off) return off;

        try
        {
            var shift = await _service.ReviewAsync(id, request.Note, CurrentUserId);
            return Ok(await ToResponseAsync(shift));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Cash shift not found." });
        }
        catch (CashShiftNotReviewableException ex)
        {
            return Conflict(new { code = "CASH_SHIFT_NOT_REVIEWABLE", message = ex.Message });
        }
    }
}
