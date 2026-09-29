namespace ToolShop.Domain.Entities.Inventory;

public enum InventoryMovementType
{
    StockIn = 1,
    StockOut = 2,
    AdjustmentIncrease = 3,
    AdjustmentDecrease = 4,
    Sale = 5,
    SaleReturn = 6,
    Purchase = 7,
    PurchaseReturn = 8
}