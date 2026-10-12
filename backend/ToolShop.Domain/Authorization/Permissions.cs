namespace ToolShop.Domain.Authorization;

public static class Permissions
{
    public const string ProductsView = "Products.View";
    public const string ProductsCreate = "Products.Create";
    public const string ProductsUpdate = "Products.Update";
    public const string ProductsDelete = "Products.Delete";

    public const string SalesView = "Sales.View";
    public const string SalesCreate = "Sales.Create";
    public const string SalesCancel = "Sales.Cancel";
    public const string SalesReturn = "Sales.Return";

    public const string InventoryView = "Inventory.View";
    public const string InventoryAdjust = "Inventory.Adjust";

    public const string FinanceView = "Finance.View";
    public const string UsersManage = "Users.Manage";
    public const string ReportsView = "Reports.View";

    // TASK 09
    public const string FinanceProfitView = "Finance.Profit.View";
    public const string ExpensesView = "Expenses.View";
    public const string ExpensesCreate = "Expenses.Create";
    public const string ExpensesUpdate = "Expenses.Update";
    public const string ExpensesVoid = "Expenses.Void";

    public static readonly Dictionary<string, string[]> RolePermissions = new()
    {
        ["Admin"] =
        [
            ProductsView, ProductsCreate, ProductsUpdate, ProductsDelete,
            SalesView, SalesCreate, SalesCancel, SalesReturn,
            InventoryView, InventoryAdjust,
            FinanceView, UsersManage, ReportsView,
            FinanceProfitView, ExpensesView, ExpensesCreate, ExpensesUpdate, ExpensesVoid
        ],
        ["Manager"] =
        [
            ProductsView, ProductsCreate, ProductsUpdate,
            SalesView, SalesCreate, SalesCancel, SalesReturn,
            InventoryView, InventoryAdjust,
            FinanceView, ReportsView,
            FinanceProfitView, ExpensesView, ExpensesCreate, ExpensesUpdate
        ],
        ["Cashier"] =
        [
            ProductsView,
            SalesView, SalesCreate
        ],
        ["Warehouse"] =
        [
            ProductsView,
            InventoryView, InventoryAdjust
        ]
    };
}