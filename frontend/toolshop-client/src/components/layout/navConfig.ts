import {
  LayoutDashboard,
  ShoppingCart,
  ClipboardList,
  Users,
  CreditCard,
  Wallet,
  Landmark,
  Package,
  Layers,
  Tag,
  Warehouse,
  ArrowLeftRight,
  Truck,
  ShoppingBag,
  Receipt,
  TrendingUp,
  BarChart3,
  UserCog,
  type LucideIcon,
} from "lucide-react";

export interface NavItem {
  to: string;
  labelKey: string;
  icon: LucideIcon;
  /** Permission needed to SEE this link (UI only; backend is authoritative). */
  permission: string;
}

export interface NavGroup {
  labelKey: string;
  items: NavItem[];
}

export const navGroups: NavGroup[] = [
  {
    labelKey: "nav.group.main",
    items: [
      { to: "/dashboard", labelKey: "nav.dashboard", icon: LayoutDashboard, permission: "Finance.Profit.View" },
      { to: "/pos", labelKey: "nav.pos", icon: ShoppingCart, permission: "Sales.Create" },
    ],
  },
  {
    labelKey: "nav.group.sales",
    items: [
      { to: "/orders", labelKey: "nav.orders", icon: ClipboardList, permission: "Sales.View" },
      { to: "/customers", labelKey: "nav.customers", icon: Users, permission: "Sales.View" },
      { to: "/payments", labelKey: "nav.payments", icon: CreditCard, permission: "Sales.View" },
      { to: "/debts", labelKey: "nav.debts", icon: Wallet, permission: "Sales.View" },
      { to: "/cash-shift", labelKey: "nav.cash", icon: Landmark, permission: "CashShifts.View" },
    ],
  },
  {
    labelKey: "nav.group.catalog",
    items: [
      { to: "/products", labelKey: "nav.products", icon: Package, permission: "Products.View" },
      { to: "/categories", labelKey: "nav.categories", icon: Layers, permission: "Products.View" },
      { to: "/brands", labelKey: "nav.brands", icon: Tag, permission: "Products.View" },
    ],
  },
  {
    labelKey: "nav.group.warehouse",
    items: [
      { to: "/inventory", labelKey: "nav.inventory", icon: Warehouse, permission: "Inventory.View" },
      { to: "/inventory/movements", labelKey: "nav.movements", icon: ArrowLeftRight, permission: "Inventory.View" },
      { to: "/suppliers", labelKey: "nav.suppliers", icon: Truck, permission: "Inventory.View" },
      { to: "/purchases", labelKey: "nav.purchases", icon: ShoppingBag, permission: "Inventory.View" },
    ],
  },
  {
    labelKey: "nav.group.finance",
    items: [
      { to: "/expenses", labelKey: "nav.expenses", icon: Receipt, permission: "Expenses.View" },
      { to: "/finance", labelKey: "nav.finance", icon: TrendingUp, permission: "Finance.Profit.View" },
      { to: "/reports", labelKey: "nav.reports", icon: BarChart3, permission: "Reports.View" },
    ],
  },
  {
    labelKey: "nav.group.admin",
    items: [{ to: "/users", labelKey: "nav.users", icon: UserCog, permission: "Users.Manage" }],
  },
];

/** First page a user can open after login. */
export function homePathFor(has: (p: string) => boolean): string {
  for (const g of navGroups) for (const i of g.items) if (has(i.permission)) return i.to;
  return "/login";
}
