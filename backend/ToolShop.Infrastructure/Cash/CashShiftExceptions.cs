namespace ToolShop.Infrastructure.Cash;

public class CashShiftsDisabledException() : Exception("Cash shifts are not enabled.");

public class CashShiftAlreadyOpenException(int shiftId, string shiftNumber)
    : Exception($"Cash register already has an open shift ({shiftNumber}).")
{
    public int ShiftId { get; } = shiftId;
    public string ShiftNumber { get; } = shiftNumber;
}

/// <summary>A payment / refund needs an open shift and there is none.</summary>
public class NoOpenCashShiftException() : Exception("There is no open cash shift. Open a shift first.");

public class CashShiftNotOpenException() : Exception("The cash shift is closed.");

public class CashShiftForbiddenException() : Exception("This shift belongs to another cashier.");

public class CashDifferenceNeedsNoteException(decimal expected, decimal difference)
    : Exception("The counted cash differs from the expected cash: a note explaining the difference is required.")
{
    public decimal Expected { get; } = expected;
    public decimal Difference { get; } = difference;
}

public class CashMovementLimitException(decimal limit)
    : Exception($"A cashier can move at most {limit} at once. Ask a manager.")
{
    public decimal Limit { get; } = limit;
}

public class CashOutExceedsDrawerException(decimal available, decimal requested)
    : Exception($"Cannot take out {requested}: only {available} is expected in the drawer.")
{
    public decimal Available { get; } = available;
    public decimal Requested { get; } = requested;
}

public class CashMovementAlreadyVoidedException() : Exception("This movement is already voided.");

public class CashShiftNotReviewableException() : Exception("Only a closed shift with a difference that has not been reviewed yet can be reviewed.");
