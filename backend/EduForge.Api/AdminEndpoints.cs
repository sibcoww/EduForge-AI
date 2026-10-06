using System.Security.Claims;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace EduForge.Api;
public static class AdminEndpoints
{
    public static bool IsAdmin(string email, IConfiguration config) => !string.IsNullOrWhiteSpace(config["Admin:Email"]) && string.Equals(email.Trim(), config["Admin:Email"]!.Trim(), StringComparison.OrdinalIgnoreCase);
    public static void MapAdmin(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) => {
            var db = context.HttpContext.RequestServices.GetRequiredService<MaterialDbContext>();
            var id = Guid.Parse(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var teacher = await db.Teachers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, context.HttpContext.RequestAborted);
            return teacher is not null && IsAdmin(teacher.Email, app.Configuration) ? await next(context) : Results.Forbid();
        });
        group.MapGet("/users", async (MaterialDbContext db, CancellationToken ct) => Results.Ok(await db.Teachers.OrderBy(x => x.Email).Select(x => new {
            x.Id, x.Name, x.Email,
            Materials = db.Materials.Count(m => m.TeacherId == x.Id), Tests = db.Quizzes.Count(q => q.TeacherId == x.Id),
            Groups = db.Groups.Count(g => g.TeacherId == x.Id), Sessions = db.Sessions.Count(s => s.TeacherId == x.Id)
        }).ToListAsync(ct)));
        group.MapDelete("/users/{id:guid}", async (Guid id, bool? deleteData, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var teacher = await db.Teachers.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (teacher is null) return Results.NotFound();
            if (id.ToString() == user.FindFirstValue(ClaimTypes.NameIdentifier) || IsAdmin(teacher.Email, app.Configuration)) return Results.Problem("Нельзя удалить аккаунт администратора.", statusCode: 409);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var hasData = await db.Materials.AnyAsync(x => x.TeacherId == id, ct) || await db.Quizzes.AnyAsync(x => x.TeacherId == id, ct) || await db.Groups.AnyAsync(x => x.TeacherId == id, ct) || await db.Sessions.AnyAsync(x => x.TeacherId == id, ct);
            if (hasData && deleteData != true) return Results.Problem("У аккаунта есть данные. Подтвердите удаление вместе с данными.", statusCode: 409);
            await db.Sessions.Where(x => x.TeacherId == id).ExecuteDeleteAsync(ct);
            await db.Publications.Where(x => x.TeacherId == id).ExecuteDeleteAsync(ct);
            await db.QuizPublications.Where(x => x.TeacherId == id).ExecuteDeleteAsync(ct);
            await db.Quizzes.Where(x => x.TeacherId == id).ExecuteDeleteAsync(ct);
            await db.Groups.Where(x => x.TeacherId == id).ExecuteDeleteAsync(ct);
            await db.Materials.Where(x => x.TeacherId == id).ExecuteDeleteAsync(ct);
            db.Teachers.Remove(teacher); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Results.NoContent();
        });
    }
}
