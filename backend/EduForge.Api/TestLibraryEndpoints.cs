using System.Security.Claims;
using System.Text.Json;
using EduForge.Application;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace EduForge.Api;
public static class TestLibraryEndpoints
{
 static Guid Owner(ClaimsPrincipal user)=>Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
 public static Quiz Copy(PublishedQuiz publication,Guid owner,Guid? materialId=null){var data=JsonSerializer.Deserialize<SaveQuiz>(publication.Snapshot)!;var copy=new Quiz{TeacherId=owner};TeachingEndpoints.Apply(copy,data with{MaterialId=materialId});return copy;}
 public static void MapTestLibrary(this WebApplication app){
 var own=app.MapGroup("/api/tests/{id:guid}/publication").RequireAuthorization();
 own.MapGet("/",(Guid id,ClaimsPrincipal user,ContentSharing sharing,CancellationToken ct)=>sharing.State("test",id,Owner(user),ct));
 own.MapPost("/",(Guid id,ClaimsPrincipal user,ContentSharing sharing,CancellationToken ct)=>sharing.Set("test",id,Owner(user),new("Public"),ct,false));
 own.MapDelete("/",async(Guid id,ClaimsPrincipal user,ContentSharing sharing,CancellationToken ct)=>{var result=await sharing.Set("test",id,Owner(user),new("Private"),ct,false);return result is IStatusCodeHttpResult {StatusCode:>=400}?result:Results.NoContent();});
 var library=app.MapGroup("/api/library/tests").RequireAuthorization();
 library.MapGet("/",async(string? q,string? subject,string? language,bool? mine,bool? recommended,int? page,ClaimsPrincipal user,MaterialDbContext db,CancellationToken ct)=>{var number=page??1;if(number<1||number>10000||q?.Length>200||subject?.Length>100||language?.Length>50)return Results.BadRequest();var query=ContentSharing.PublicTests(db).AsNoTracking();var owner=Owner(user);if(!string.IsNullOrWhiteSpace(q)){var search=q.Trim().ToLowerInvariant();query=query.Where(x=>x.SearchText.Contains(search));}if(!string.IsNullOrWhiteSpace(subject))query=query.Where(x=>x.Subject==subject);if(!string.IsNullOrWhiteSpace(language))query=query.Where(x=>x.Language==language);if(mine==true)query=query.Where(x=>x.TeacherId==owner);
            if(recommended==true){var disciplines=System.Text.Json.JsonSerializer.Deserialize<List<string>>(await db.Teachers.Where(t=>t.Id==owner).Select(t=>t.Disciplines).SingleAsync(ct))!;var subjects=await query.Select(p=>p.Subject).Distinct().ToListAsync(ct);var matches=subjects.Where(subject=>disciplines.Any(d=>subject.Contains(d,StringComparison.OrdinalIgnoreCase)||d.Contains(subject,StringComparison.OrdinalIgnoreCase)&&subject.Length>0)).ToArray();query=query.Where(x=>matches.Contains(x.Subject));}
var total=await query.CountAsync(ct);var items=await query.OrderByDescending(x=>x.PublishedAt).ThenBy(x=>x.Id).Skip((number-1)*20).Take(20).Select(x=>new{x.Id,x.Title,x.Subject,x.Level,x.Language,x.AuthorName,x.PublishedAt,IsOwn=x.TeacherId==owner,AlreadyAdded=db.CopyReferences.Any(c=>c.TeacherId==owner&&c.SourceId==x.Id&&c.Kind=="test"),HasUpdate=db.CopyReferences.Any(c=>c.TeacherId==owner&&c.SourceId==x.Id&&c.Kind=="test"&&c.SourceRevision!=x.Revision)}).ToListAsync(ct);return Results.Ok(new{items,total,page=number,pageSize=20});});
 library.MapGet("/filters",async(MaterialDbContext db,CancellationToken ct)=>Results.Ok(new{Subjects=await ContentSharing.PublicTests(db).Select(x=>x.Subject).Distinct().OrderBy(x=>x).Take(200).ToListAsync(ct),Languages=await ContentSharing.PublicTests(db).Select(x=>x.Language).Distinct().OrderBy(x=>x).Take(100).ToListAsync(ct)}));
 library.MapGet("/{id:guid}",async(Guid id,ClaimsPrincipal user,MaterialDbContext db,CancellationToken ct)=>{var p=await ContentSharing.PublicTests(db).AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);if(p==null)return Results.NotFound();var data=JsonSerializer.Deserialize<SaveQuiz>(p.Snapshot)!;var content=string.Join("\n\n",data.Questions.Select((x,i)=>$"{i+1}. {x.Text} ({x.Points} баллов)\n"+string.Join("\n",x.Options.Select(o=>$"{(o.IsCorrect?"✓":"○")} {o.Text}"))+(string.IsNullOrEmpty(x.ModelAnswer)?"":"\nОтвет: "+x.ModelAnswer)));return Results.Ok(new{p.Id,p.Title,p.Subject,p.Level,p.Language,p.AuthorName,p.PublishedAt,Content=content,IsOwn=p.TeacherId==Owner(user)});});
 library.MapPost("/{id:guid}/copy",async(Guid id,ClaimsPrincipal user,MaterialDbContext db,CancellationToken ct)=>{var p=await ContentSharing.PublicTests(db).AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);if(p==null)return Results.NotFound();var copy=Copy(p,Owner(user));db.Quizzes.Add(copy);db.CopyReferences.Add(new CopyReference{TeacherId=copy.TeacherId,CopyId=copy.Id,SourceId=id,Kind="test",SourceRevision=p.Revision,Baseline=p.Snapshot});await db.SaveChangesAsync(ct);return Results.Created($"/api/tests/{copy.Id}",new{copy.Id});});
 }
}
