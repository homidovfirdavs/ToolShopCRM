using ToolShop.Domain.Entities;
using ToolShop.Infrastructure.Persistence;

namespace ToolShop.Infrastructure.Audit;

public class AuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(
        string? userId,
        string action,
        string? entityName = null,
        string? entityId = null,
        string? description = null,
        string? ipAddress = null)
    {
        var entry = new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Description = description,
            IpAddress = ipAddress
        };

        _db.AuditLogs.Add(entry);
        await _db.SaveChangesAsync();
    }
}