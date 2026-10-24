using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using ToolShop.Domain.Entities;
using ToolShop.Domain.Entities.Cash;
using ToolShop.Infrastructure.Idempotency;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Cash;

/// <summary>
/// Opening and closing a shift, hand-made cash movements, review of a difference. Every operation runs in ONE serializable
/// transaction (the project's pattern for money): it either happens completely, with its audit row, or not at all, and a
/// transaction that loses a race is retried. "Only one open shift per drawer" is enforced by the database itself.
/// </summary>
public class CashShiftService
{
    public const string OpenShiftIndex = "UX_CashShifts_OneOpenPerRegister";
    public const string MovementScope = "cash.movement";

    private readonly AppDbContext _db;
    private readonly CashShiftOptions _options;

    public CashShiftService(AppDbContext db, CashShiftOptions options)
    {
        _db = db;
        _options = options;
    }

    public CashShiftOptions Options => _options;

    // ------------------------------------------------------------------ open

    public Task<CashShift> OpenAsync(int? registerId, decimal openingCash, string userId, string? cashierUserId = null)
    {
        EnsureEnabled();
        CheckAmount(openingCash, allowZero: true);

        return RunAsync(async () =>
        {
            CashRegister? register;
            if (registerId.HasValue)
            {
                register = await _db.CashRegisters.FirstOrDefaultAsync(r => r.Id == registerId.Value && r.IsActive);
            }
            else
            {
                var active = await _db.CashRegisters.Where(r => r.IsActive).ToListAsync();
                if (active.Count != 1) throw new ArgumentException("Specify which cash register to open.");
                register = active[0];
            }

            if (register is null) throw new KeyNotFoundException("Cash register not found or inactive.");

            var open = await _db.CashShifts.AsNoTracking()
                .FirstOrDefaultAsync(s => s.CashRegisterId == register.Id && s.Status == CashShiftStatus.Open);
            if (open is not null) throw new CashShiftAlreadyOpenException(open.Id, open.ShiftNumber);

            var previousClosing = await _db.CashShifts.AsNoTracking()
                .Where(s => s.CashRegisterId == register.Id && s.Status == CashShiftStatus.Closed)
                .OrderByDescending(s => s.ClosedAt)
                .Select(s => s.ClosingCash)
                .FirstOrDefaultAsync();

            var shift = new CashShift
            {
                ShiftNumber = await CashShiftNumberGenerator.GenerateAsync(_db),
                CashRegisterId = register.Id,
                CashierUserId = cashierUserId ?? userId,
                OpenedByUserId = userId,
                OpenedAt = DateTime.UtcNow,
                OpeningCash = Round(openingCash),
                Status = CashShiftStatus.Open
            };
            _db.CashShifts.Add(shift);
            await _db.SaveChangesAsync();

            var handover = previousClosing.HasValue && previousClosing.Value != shift.OpeningCash
                ? $" Previous shift closed with {previousClosing.Value}, opening counted {shift.OpeningCash}."
                : string.Empty;
            await AuditAsync(userId, "CashShift.Open", shift.Id,
                $"Shift {shift.ShiftNumber} opened with {shift.OpeningCash}.{handover}");

            return shift;
        });
    }

    // ------------------------------------------------------------------ close

    /// <param name="isPrivileged">Manager / admin: may close any shift. A cashier only their own.</param>
    public Task<(CashShift Shift, CashBreakdown Breakdown)> CloseAsync(
        int shiftId, decimal actualCash, string? note, string userId, bool isPrivileged)
    {
        EnsureEnabled();
        CheckAmount(actualCash, allowZero: true);

        return RunAsync(async () =>
        {
            var shift = await _db.CashShifts.FirstOrDefaultAsync(s => s.Id == shiftId)
                ?? throw new KeyNotFoundException("Cash shift not found.");
            if (shift.Status != CashShiftStatus.Open) throw new CashShiftNotOpenException();
            if (!isPrivileged && shift.CashierUserId != userId) throw new CashShiftForbiddenException();

            var breakdown = await CashShiftCalculator.ComputeAsync(_db, shift);
            var actual = Round(actualCash);
            var difference = actual - Round(breakdown.ExpectedCash);

            if (difference != 0m && string.IsNullOrWhiteSpace(note))
            {
                throw new CashDifferenceNeedsNoteException(breakdown.ExpectedCash, difference);
            }

            shift.Status = CashShiftStatus.Closed;
            shift.ClosedAt = DateTime.UtcNow;
            shift.ClosedByUserId = userId;
            shift.ClosingCash = actual;
            shift.ExpectedCashAtClose = Round(breakdown.ExpectedCash);
            shift.CashDifference = difference;
            shift.CloseNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            shift.ClosedByOther = shift.CashierUserId != userId;
            shift.RequiresReview = difference != 0m;

            await AuditAsync(userId, "CashShift.Close", shift.Id,
                $"Shift {shift.ShiftNumber} closed. Expected {shift.ExpectedCashAtClose}, counted {actual}, difference {difference}."
                + (shift.ClosedByOther ? " Closed by someone other than the cashier." : string.Empty)
                + (shift.CloseNote is null ? string.Empty : $" Note: {shift.CloseNote}"));

            return (shift, breakdown);
        });
    }

    // ------------------------------------------------------------------ movements

    public Task<(CashMovement Movement, bool Replayed)> AddMovementAsync(
        int shiftId, CashMovementType type, decimal amount, string reason, int? expenseId,
        string userId, bool isPrivileged, string idempotencyKey)
    {
        EnsureEnabled();
        CheckAmount(amount, allowZero: false);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required.");
        reason = reason.Trim();
        if (reason.Length > 500) throw new ArgumentException("The reason is too long (500 characters at most).");

        var requestHash = Sha256($"{shiftId}|{type}|{Round(amount):0.00}|{reason}|{expenseId}");

        return RunAsync(async () =>
        {
            // The key is claimed first, inside the transaction (a rejected request never burns its key).
            var (idem, replayed) = await IdempotencyGate.BeginAsync(
                _db, MovementScope, userId, idempotencyKey, requestHash, nameof(CashMovement));

            if (replayed)
            {
                var stored = await _db.CashMovements.AsNoTracking().FirstAsync(m => m.Id == idem.ResultEntityId);
                return (stored, true);
            }

            var shift = await _db.CashShifts.FirstOrDefaultAsync(s => s.Id == shiftId)
                ?? throw new KeyNotFoundException("Cash shift not found.");
            if (shift.Status != CashShiftStatus.Open) throw new CashShiftNotOpenException();
            if (!isPrivileged && shift.CashierUserId != userId) throw new CashShiftForbiddenException();
            if (!isPrivileged && amount > _options.CashierMovementLimit)
            {
                throw new CashMovementLimitException(_options.CashierMovementLimit);
            }

            if (expenseId.HasValue)
            {
                var expenseOk = await _db.Expenses.AsNoTracking().AnyAsync(e => e.Id == expenseId.Value && !e.IsVoided);
                if (!expenseOk) throw new ArgumentException("The expense does not exist or is voided.");
            }

            var rounded = Round(amount);
            if (type == CashMovementType.CashOut)
            {
                var breakdown = await CashShiftCalculator.ComputeAsync(_db, shift);
                if (rounded > breakdown.ExpectedCash)
                {
                    throw new CashOutExceedsDrawerException(breakdown.ExpectedCash, rounded);
                }
            }

            var movement = new CashMovement
            {
                CashShiftId = shift.Id,
                Type = type,
                Amount = rounded,
                Reason = reason,
                ExpenseId = expenseId,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };
            _db.CashMovements.Add(movement);
            await _db.SaveChangesAsync();

            idem.ResultEntityId = movement.Id;
            await AuditAsync(userId, type == CashMovementType.CashIn ? "CashShift.CashIn" : "CashShift.CashOut", shift.Id,
                $"{type} {rounded} in shift {shift.ShiftNumber}. Reason: {reason}");

            return (movement, false);
        });
    }

    public Task<CashMovement> VoidMovementAsync(int movementId, string reason, string userId)
    {
        EnsureEnabled();
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A void reason is required.");
        reason = reason.Trim();

        return RunAsync(async () =>
        {
            var movement = await _db.CashMovements.Include(m => m.CashShift).FirstOrDefaultAsync(m => m.Id == movementId)
                ?? throw new KeyNotFoundException("Cash movement not found.");
            if (movement.IsVoided) throw new CashMovementAlreadyVoidedException();
            if (movement.CashShift.Status != CashShiftStatus.Open) throw new CashShiftNotOpenException();

            movement.IsVoided = true;
            movement.VoidedAt = DateTime.UtcNow;
            movement.VoidedByUserId = userId;
            movement.VoidReason = reason;

            await AuditAsync(userId, "CashShift.MovementVoid", movement.CashShiftId,
                $"{movement.Type} {movement.Amount} voided in shift {movement.CashShift.ShiftNumber}. Reason: {reason}");

            return movement;
        });
    }

    // ------------------------------------------------------------------ review

    public Task<CashShift> ReviewAsync(int shiftId, string? note, string userId)
    {
        EnsureEnabled();

        return RunAsync(async () =>
        {
            var shift = await _db.CashShifts.FirstOrDefaultAsync(s => s.Id == shiftId)
                ?? throw new KeyNotFoundException("Cash shift not found.");
            if (shift.Status != CashShiftStatus.Closed || !shift.RequiresReview || shift.ReviewedAt is not null)
            {
                throw new CashShiftNotReviewableException();
            }

            shift.ReviewedAt = DateTime.UtcNow;
            shift.ReviewedByUserId = userId;
            shift.ReviewNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

            await AuditAsync(userId, "CashShift.Review", shift.Id,
                $"Difference {shift.CashDifference} of shift {shift.ShiftNumber} reviewed.");

            return shift;
        });
    }

    // ------------------------------------------------------------------ plumbing

    private void EnsureEnabled()
    {
        if (!_options.Enabled) throw new CashShiftsDisabledException();
    }

    private void CheckAmount(decimal amount, bool allowZero)
    {
        if (amount < 0m || (!allowZero && amount <= 0m))
        {
            throw new ArgumentException(allowZero
                ? "The amount cannot be negative."
                : "The amount must be greater than zero.");
        }

        if (amount > _options.MaxAmount) throw new ArgumentException("The amount is too large.");
        if (Round(amount) != amount) throw new ArgumentException("The amount can have at most two decimal places.");
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>Adds the audit row (needs the entity id, so the entity is saved first) and saves.</summary>
    private async Task AuditAsync(string userId, string action, int shiftId, string description)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityName = "CashShift",
            EntityId = shiftId.ToString(),
            Description = description,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    private static bool IsOpenShiftConflict(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is PostgresException { SqlState: "23505" } pg && pg.ConstraintName == OpenShiftIndex) return true;
        }

        return false;
    }

    /// <summary>Runs <paramref name="work"/> in a serializable transaction and retries it when it loses a race.</summary>
    private async Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        const int maxAttempts = 6;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            IDbContextTransaction? transaction = null;
            try
            {
                transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var result = await work();
                await transaction.CommitAsync();
                return result;
            }
            catch (Exception ex) when ((IdempotencyGate.IsRetryable(ex) || IsOpenShiftConflict(ex)) && attempt < maxAttempts)
            {
                if (transaction is not null)
                {
                    try { await transaction.RollbackAsync(); } catch { /* already rolled back */ }
                }

                _db.ChangeTracker.Clear();
                await Task.Delay(Random.Shared.Next(10, 40) * attempt);
            }
            finally
            {
                if (transaction is not null) await transaction.DisposeAsync();
            }
        }

        throw new InvalidOperationException("Could not complete the operation due to concurrent updates. Please try again.");
    }
}
