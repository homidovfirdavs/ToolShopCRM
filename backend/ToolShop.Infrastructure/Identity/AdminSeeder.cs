using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ToolShop.Domain.Entities;

namespace ToolShop.Infrastructure.Identity;

public static class AdminSeeder
{
    public static async Task SeedAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ILogger logger)
    {
        var username = configuration["AdminUser:Username"];
        var email = configuration["AdminUser:Email"];
        var password = configuration["AdminUser:Password"];

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "AdminUser credentials not found in configuration (User Secrets). Skipping admin seed.");
            return;
        }

        var existingAdmin = await userManager.FindByNameAsync(username);
        if (existingAdmin is not null)
        {
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = username,
            Email = email,
            FullName = "System Administrator",
            IsActive = true,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(admin, password);

        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            logger.LogError("Failed to create admin user: {Errors}", errors);
            return;
        }

        await userManager.AddToRoleAsync(admin, "Admin");
        logger.LogInformation("Admin user '{Username}' created successfully.", username);
    }
}