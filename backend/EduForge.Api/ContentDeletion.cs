using System.Security.Claims;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace EduForge.Api;
public static class ContentDeletion
{
    public static void Map(WebApplication app){
        app.MapDelete("/api/materials/{id:guid}",async(Guid id,ClaimsPrincipal user,MaterialDbContext db,CancellationToken ct)=>{
            var owner=Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var material=await db.Materials.SingleOrDefaultAsync(m=>m.Id==id&&m.TeacherId==owner,ct);if(material is null)return Results.NotFound();
            await using var tx=await db.Database.BeginTransactionAsync(ct);
            // Preserve tests and immutable sessions, but remove a now-unverifiable source binding.
            var tests=await db.Quizzes.Where(q=>q.MaterialId==id&&q.TeacherId==owner).Select(q=>q.Id).ToListAsync(ct);
            await db.Questions.Where(q=>tests.Contains(q.QuizId)).ExecuteUpdateAsync(s=>s.SetProperty(q=>q.Grounding,"").SetProperty(q=>q.ReviewDigest,""),ct);
            await db.Quizzes.Where(q=>q.MaterialId==id&&q.TeacherId==owner).ExecuteUpdateAsync(s=>s.SetProperty(q=>q.MaterialId,(Guid?)null).SetProperty(q=>q.Controlled,false).SetProperty(q=>q.UpdatedAt,DateTimeOffset.UtcNow),ct);
            await db.QuizPublications.Where(q=>q.MaterialId==id&&q.TeacherId==owner).ExecuteUpdateAsync(s=>s.SetProperty(q=>q.MaterialId,(Guid?)null),ct);
            await db.PublicationReviews.Where(r=>r.SourceId==id&&r.Kind=="material").ExecuteDeleteAsync(ct);
            await db.ContentAccesses.Where(a=>a.SourceId==id&&a.Kind=="material"&&a.TeacherId==owner).ExecuteDeleteAsync(ct);
            await db.CopyReferences.Where(r=>r.CopyId==id&&r.Kind=="material"&&r.TeacherId==owner).ExecuteDeleteAsync(ct);
            db.Materials.Remove(material);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Results.NoContent();
        }).RequireAuthorization();
    }
}
