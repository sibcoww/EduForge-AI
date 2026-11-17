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
        var own=app.MapGroup("/api/materials/{id:guid}/publication").RequireAuthorization();
        own.MapGet("/",(Guid id,ClaimsPrincipal user,ContentSharing sharing,CancellationToken ct)=>sharing.State("material",id,Owner(user),ct));
        own.MapPost("/",(Guid id,ClaimsPrincipal user,ContentSharing sharing,CancellationToken ct)=>sharing.Set("material",id,Owner(user),new("Public"),ct,false));
        own.MapDelete("/",async(Guid id,ClaimsPrincipal user,ContentSharing sharing,CancellationToken ct)=>{var result=await sharing.Set("material",id,Owner(user),new("Private"),ct,false);return result is IStatusCodeHttpResult {StatusCode:>=400}?result:Results.NoContent();});
        var library = app.MapGroup("/api/library").RequireAuthorization();
        library.MapGet("/", async (string? q, string? subject, string? language, bool? mine, bool? recommended, int? page, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var number = page ?? 1;
            if (number is < 1 or > 10000 || q?.Length > 200 || subject?.Length > 100 || language?.Length > 50) return Results.BadRequest();
            var query = ContentSharing.PublicMaterials(db).AsNoTracking(); var owner = Owner(user);
            if (!string.IsNullOrWhiteSpace(q)) { var search = q.Trim().ToLowerInvariant(); query = query.Where(p => p.SearchText.Contains(search)); }
            if (!string.IsNullOrWhiteSpace(subject)) query = query.Where(p => p.Subject == subject);
            if (!string.IsNullOrWhiteSpace(language)) query = query.Where(p => p.Language == language);
            if (mine == true) query = query.Where(p => p.TeacherId == owner);
            if(recommended==true){var disciplines=System.Text.Json.JsonSerializer.Deserialize<List<string>>(await db.Teachers.Where(t=>t.Id==owner).Select(t=>t.Disciplines).SingleAsync(ct))!;var subjects=await query.Select(p=>p.Subject).Distinct().ToListAsync(ct);var matches=subjects.Where(subject=>disciplines.Any(d=>subject.Contains(d,StringComparison.OrdinalIgnoreCase)||d.Contains(subject,StringComparison.OrdinalIgnoreCase)&&subject.Length>0)).ToArray();query=query.Where(p=>matches.Contains(p.Subject));}

            var count = await query.CountAsync(ct);
            var items = await query.OrderByDescending(p => p.PublishedAt).ThenBy(p => p.Id).Skip((number - 1) * 20).Take(20).Select(p => new { p.Id, p.Title, p.Subject, p.Level, p.Language, p.AuthorName, p.PublishedAt, IsOwn = p.TeacherId == owner, AlreadyAdded=db.CopyReferences.Any(c=>c.TeacherId==owner&&c.SourceId==p.Id&&c.Kind=="material"), HasUpdate=db.CopyReferences.Any(c=>c.TeacherId==owner&&c.SourceId==p.Id&&c.Kind=="material"&&c.SourceRevision!=p.Revision) }).ToListAsync(ct);
            return Results.Ok(new { items, total = count, page = number, pageSize = 20 });
        });
        library.MapGet("/filters", async (MaterialDbContext db, CancellationToken ct) => Results.Ok(new {
            Subjects = await ContentSharing.PublicMaterials(db).AsNoTracking().Select(p => p.Subject).Distinct().OrderBy(s => s).Take(200).ToListAsync(ct),
            Languages = await ContentSharing.PublicMaterials(db).AsNoTracking().Select(p => p.Language).Distinct().OrderBy(s => s).Take(100).ToListAsync(ct)
        }));
        library.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var publication = await ContentSharing.PublicMaterials(db).AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
            return publication is null ? Results.NotFound() : Results.Ok(new { publication.Id, publication.Title, publication.Subject, publication.Level, publication.Language, publication.AuthorName, publication.PublishedAt, Content = MaterialText.Extract(publication.Content), RichContent = publication.Content, IsOwn = publication.TeacherId == Owner(user) });
        });
        library.MapPost("/{id:guid}/copy", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var publication = await ContentSharing.PublicMaterials(db).AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
            if (publication is null) return Results.NotFound();
            var copy = new Material { TeacherId = Owner(user), Title = publication.Title, Subject = publication.Subject, Level = publication.Level, Language = publication.Language, Content = await MaterialMediaCopies.Clone(publication.Content,Owner(user),db,ct) };
            db.Materials.Add(copy);
            db.CopyReferences.Add(new CopyReference{TeacherId=copy.TeacherId,CopyId=copy.Id,SourceId=id,SourceRevision=publication.Revision,Baseline=MaterialMediaCopies.Baseline(publication,copy.Content)});
            var tests = await ContentSharing.PublicTests(db).AsNoTracking().Where(p => p.MaterialId == id && p.TeacherId == publication.TeacherId).ToListAsync(ct);
            foreach (var test in tests) { var quizCopy=TestLibraryEndpoints.Copy(test,Owner(user),copy.Id); db.Quizzes.Add(quizCopy);db.CopyReferences.Add(new CopyReference{TeacherId=Owner(user),CopyId=quizCopy.Id,SourceId=test.Id,Kind="test",SourceRevision=test.Revision,Baseline=test.Snapshot}); }
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/materials/{copy.Id}", new { copy.Id });
        });
    }
}
