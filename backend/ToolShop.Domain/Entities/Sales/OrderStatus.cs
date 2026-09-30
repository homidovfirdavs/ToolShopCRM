namespace ToolShop.Domain.Entities.Sales;

public enum OrderStatus
{
    Draft = 1,
    Completed = 2,
    Cancelled = 3,
    Returned = 4,
    PartiallyReturned = 5
}