using System.Security.Claims;
using EduForge.Application;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EduForge.Api;
public static class LibraryEndpoints
{
    static Guid Owner(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    static IResult Conflict() => Results.Conflict(new { message = "Публикация изменилась. Обновите страницу и повторите действие." });
    public static void MapLibrary(this WebApplication app)
    {
        var own = app.MapGroup("/api/materials/{id:guid}/publication").RequireAuthorization();
        own.MapGet("/", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            if (!await db.Materials.AnyAsync(m => m.Id == id && m.TeacherId == Owner(user), ct)) return Results.NotFound();
            var publication = await db.Publications.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
            return Results.Ok(new { Published = publication is not null, publication?.PublishedAt });
        });
        own.MapPost("/", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var source = await db.Materials.SingleOrDefaultAsync(m => m.Id == id && m.TeacherId == Owner(user), ct);
            if (source is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(MaterialText.Extract(source.Content))) return Results.BadRequest(new { message = "Добавьте содержание перед публикацией." });
            var publication = await db.Publications.SingleOrDefaultAsync(p => p.Id == id, ct);
            if (publication is null) { publication = new PublishedMaterial { Id = id, TeacherId = source.TeacherId }; db.Publications.Add(publication); }
            publication.AuthorName = await db.Teachers.Where(t => t.Id == source.TeacherId).Select(t => t.Name).SingleAsync(ct);
            publication.Title = source.Title; publication.Subject = source.Subject; publication.Level = source.Level; publication.Language = source.Language; publication.Content = source.Content;
            publication.SearchText = $"{source.Title} {source.Subject} {publication.AuthorName}".ToLowerInvariant();
            publication.PublishedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); publication.Revision = Guid.NewGuid();
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Conflict(); }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" }) { return Conflict(); }
            return Results.Ok(new { Published = true, publication.PublishedAt });
        });
        own.MapDelete("/", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            if (!await db.Materials.AnyAsync(m => m.Id == id && m.TeacherId == Owner(user), ct)) return Results.NotFound();
            var publication = await db.Publications.SingleOrDefaultAsync(p => p.Id == id && p.TeacherId == Owner(user), ct);
            if (publication is null) return Results.NoContent();
            db.Publications.Remove(publication);
            try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Conflict(); }
            return Results.NoContent();
        });
        var library = app.MapGroup("/api/library").RequireAuthorization();
        library.MapGet("/", async (string? q, string? subject, string? language, bool? mine, int? page, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var number = page ?? 1;
            if (number is < 1 or > 10000 || q?.Length > 200 || subject?.Length > 100 || language?.Length > 50) return Results.BadRequest();
            var query = db.Publications.AsNoTracking(); var owner = Owner(user);
            if (!string.IsNullOrWhiteSpace(q)) { var search = q.Trim().ToLowerInvariant(); query = query.Where(p => p.SearchText.Contains(search)); }
            if (!string.IsNullOrWhiteSpace(subject)) query = query.Where(p => p.Subject == subject);
            if (!string.IsNullOrWhiteSpace(language)) query = query.Where(p => p.Language == language);
            if (mine == true) query = query.Where(p => p.TeacherId == owner);
            var count = await query.CountAsync(ct);
            var items = await query.OrderByDescending(p => p.PublishedAt).ThenBy(p => p.Id).Skip((number - 1) * 20).Take(20).Select(p => new { p.Id, p.Title, p.Subject, p.Level, p.Language, p.AuthorName, p.PublishedAt, IsOwn = p.TeacherId == owner }).ToListAsync(ct);
            return Results.Ok(new { items, total = count, page = number, pageSize = 20 });
        });
        library.MapGet("/filters", async (MaterialDbContext db, CancellationToken ct) => Results.Ok(new {
            Subjects = await db.Publications.AsNoTracking().Select(p => p.Subject).Distinct().OrderBy(s => s).Take(200).ToListAsync(ct),
            Languages = await db.Publications.AsNoTracking().Select(p => p.Language).Distinct().OrderBy(s => s).Take(100).ToListAsync(ct)
        }));
        library.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var publication = await db.Publications.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
            return publication is null ? Results.NotFound() : Results.Ok(new { publication.Id, publication.Title, publication.Subject, publication.Level, publication.Language, publication.AuthorName, publication.PublishedAt, Content = MaterialText.Extract(publication.Content), IsOwn = publication.TeacherId == Owner(user) });
        });
        library.MapPost("/{id:guid}/copy", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var publication = await db.Publications.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
            if (publication is null) return Results.NotFound();
            var copy = new Material { TeacherId = Owner(user), Title = publication.Title, Subject = publication.Subject, Level = publication.Level, Language = publication.Language, Content = publication.Content };
            db.Materials.Add(copy);
            var tests = await db.QuizPublications.AsNoTracking().Where(p => p.MaterialId == id && p.TeacherId == publication.TeacherId).ToListAsync(ct);
            foreach (var test in tests) db.Quizzes.Add(TestLibraryEndpoints.Copy(test, Owner(user), copy.Id));
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/materials/{copy.Id}", new { copy.Id });
        });
    }
}
