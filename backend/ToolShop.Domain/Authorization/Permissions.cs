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

    // T2b: permissions that replace the old [Authorize(Roles = ...)] checks (same roles as before).
    public const string BrandsManage = "Brands.Manage";
    public const string CategoriesManage = "Categories.Manage";
    public const string CustomersView = "Customers.View";
    public const string CustomersManage = "Customers.Manage";
    public const string CustomersDeactivate = "Customers.Deactivate";
    public const string PaymentsVoid = "Payments.Void";
    public const string DebtsWalkInExceptions = "Debts.WalkInExceptions";
    public const string InventoryStockLookup = "Inventory.StockLookup";
    public const string PurchasesManage = "Purchases.Manage";
    public const string PurchasesConfirm = "Purchases.Confirm";
    public const string SuppliersManage = "Suppliers.Manage";
    public const string SuppliersDeactivate = "Suppliers.Deactivate";

    public static readonly Dictionary<string, string[]> RolePermissions = new()
    {
        ["Admin"] =
        [
            ProductsView, ProductsCreate, ProductsUpdate, ProductsDelete,
            SalesView, SalesCreate, SalesCancel, SalesReturn,
            InventoryView, InventoryAdjust,
            FinanceView, UsersManage, ReportsView,
            FinanceProfitView, ExpensesView, ExpensesCreate, ExpensesUpdate, ExpensesVoid,
            BrandsManage, CategoriesManage, CustomersView, CustomersManage, CustomersDeactivate,
            PaymentsVoid, DebtsWalkInExceptions, InventoryStockLookup,
            PurchasesManage, PurchasesConfirm, SuppliersManage, SuppliersDeactivate
        ],
        ["Manager"] =
        [
            ProductsView, ProductsCreate, ProductsUpdate, ProductsDelete,
            SalesView, SalesCreate, SalesCancel, SalesReturn,
            InventoryView, InventoryAdjust,
            FinanceView, ReportsView,
            FinanceProfitView, ExpensesView, ExpensesCreate, ExpensesUpdate,
            BrandsManage, CategoriesManage, CustomersView, CustomersManage, CustomersDeactivate,
            PaymentsVoid, DebtsWalkInExceptions, InventoryStockLookup,
            PurchasesManage, PurchasesConfirm, SuppliersManage, SuppliersDeactivate
        ],
        ["Cashier"] =
        [
            ProductsView,
            SalesView, SalesCreate, SalesCancel,
            CustomersView, CustomersManage, InventoryStockLookup
        ],
        ["Warehouse"] =
        [
            ProductsView,
            InventoryView, InventoryAdjust,
            InventoryStockLookup, PurchasesManage, SuppliersManage
        ]
    };
}