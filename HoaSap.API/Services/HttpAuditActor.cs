using System.Security.Claims;
using HoaSap.DAL.Auditing;
using Microsoft.AspNetCore.Http;

namespace HoaSap.API.Services;

public sealed class HttpAuditActor(IHttpContextAccessor accessor) : IAuditActor
{
    private HttpContext? Context => accessor.HttpContext;
    public Guid? ActorId => Guid.TryParse(Context?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public string? Role => Context?.User.FindFirstValue(ClaimTypes.Role);
    public string? TraceId => Context?.TraceIdentifier;
}
