using System.Security.Claims;
using EduForge.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace EduForge.Api;
public sealed class SessionHub(MaterialDbContext db) : Hub
{
    public async Task Watch(Guid sessionId, string? attemptToken)
    {
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId);
        var teacher = Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && session?.TeacherId == id;
        if (session is null || (!teacher && SessionService.Participant(SessionService.Read(session), attemptToken) is null))
            throw new HubException("Доступ к сессии запрещён.");
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId.ToString());
    }
}
