using HoaSap.BLL.Workshops;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace HoaSap.API.Controllers;

[ApiController, Route("api/workshops")]
public sealed class WorkshopsController(WorkshopService service) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<WorkshopListDto>> List(CancellationToken ct) => service.ListAsync(User.IsInRole("Admin") || User.IsInRole("Staff"), ct);
    [HttpGet("{id:guid}")] public Task<WorkshopDto> Get(Guid id, CancellationToken ct) => service.GetAsync(id, User.IsInRole("Admin") || User.IsInRole("Staff"), ct);
    [HttpPost, Authorize(Roles = "Admin,Staff")] public Task<WorkshopDto> Create(WorkshopUpsertDto r, CancellationToken ct) => service.CreateAsync(r, ct);
    [HttpPut("{id:guid}"), Authorize(Roles = "Admin,Staff")] public Task<WorkshopDto> Update(Guid id, WorkshopUpsertDto r, CancellationToken ct) => service.UpdateAsync(id, r, ct);
    [HttpDelete("{id:guid}"), Authorize(Roles = "Admin,Staff")] public Task Delete(Guid id, CancellationToken ct) => service.DeleteAsync(id, ct);
    [HttpGet("{id:guid}/sessions")] public Task<IReadOnlyList<SessionDto>> Sessions(Guid id, CancellationToken ct) => service.SessionsAsync(id, User.IsInRole("Admin") || User.IsInRole("Staff"), ct);
    [HttpGet("sessions/{id:guid}/roster"), Authorize(Roles = "Admin,Staff")] public Task<IReadOnlyList<BookingDto>> Roster(Guid id, CancellationToken ct) => service.SessionRosterAsync(id, ct);
    [HttpPost("{id:guid}/sessions"), Authorize(Roles = "Admin,Staff")] public Task<SessionDto> CreateSession(Guid id, SessionUpsertDto r, CancellationToken ct) => service.CreateSessionAsync(id, r, ct);
    [HttpPut("sessions/{id:guid}"), Authorize(Roles = "Admin,Staff")] public Task<SessionDto> UpdateSession(Guid id, SessionUpsertDto r, CancellationToken ct) => service.UpdateSessionAsync(id, r, ct);
    [HttpDelete("sessions/{id:guid}"), Authorize(Roles = "Admin,Staff")] public Task CancelSession(Guid id, CancellationToken ct) => service.CancelSessionAsync(id, ct);
    [HttpPost("sessions/{id:guid}/close"), Authorize(Roles = "Admin,Staff")] public Task CloseSession(Guid id, CancellationToken ct) => service.CloseSessionAsync(id, ct);
}
