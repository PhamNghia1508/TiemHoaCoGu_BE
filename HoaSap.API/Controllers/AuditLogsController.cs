using HoaSap.DAL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HoaSap.API.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/management/audit-logs")]
public sealed class AuditLogsController(HoaSapDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AuditLogPage>> Get(
        [FromQuery] string? entityType, [FromQuery] string? entityId, [FromQuery] Guid? actorId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return BadRequest("page/pageSize is out of bounds");
        if (from.HasValue && to.HasValue && from.Value.ToUniversalTime() > to.Value.ToUniversalTime())
            return BadRequest("from must not be later than to");
        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(x => x.EntityType == entityType.Trim());
        if (!string.IsNullOrWhiteSpace(entityId)) query = query.Where(x => x.EntityId == entityId.Trim());
        if (actorId.HasValue) query = query.Where(x => x.ActorId == actorId);
        if (from.HasValue) query = query.Where(x => x.OccurredAtUtc >= from.Value.ToUniversalTime());
        if (to.HasValue) query = query.Where(x => x.OccurredAtUtc <= to.Value.ToUniversalTime());
        var total = await query.LongCountAsync(ct);
        var skip = checked((page - 1) * pageSize);
        var items = await query.OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
            .Skip(skip).Take(pageSize).ToListAsync(ct);
        return Ok(new AuditLogPage(items, total, page, pageSize));
    }
}

public sealed record AuditLogPage(IReadOnlyList<HoaSap.Domain.AuditLog> Items, long Total, int Page, int PageSize);
