using Microsoft.AspNetCore.Identity;

namespace ToolShop.Infrastructure.Identity;

public static class RoleSeeder
{
    public static readonly string[] Roles = ["Admin", "Manager", "Cashier", "Warehouse"];

    public static async Task SeedAsync(RoleManager<IdentityRole> roleManager)
    {
        foreach (var roleName in Roles)
        {
            var exists = await roleManager.RoleExistsAsync(roleName);
            if (!exists)
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }
    }
}