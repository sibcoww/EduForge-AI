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
 own.MapGet("/",async(Guid id,ClaimsPrincipal user,MaterialDbContext db,CancellationToken ct)=>{
 if(!await db.Quizzes.AnyAsync(x=>x.Id==id&&x.TeacherId==Owner(user),ct))return Results.NotFound();
 var p=await db.QuizPublications.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);return Results.Ok(new{Published=p!=null,p?.PublishedAt});});
 own.MapPost("/",async(Guid id,ClaimsPrincipal user,MaterialDbContext db,CancellationToken ct)=>{
 var quiz=await db.Quizzes.Include(x=>x.Questions).ThenInclude(x=>x.Options).SingleOrDefaultAsync(x=>x.Id==id&&x.TeacherId==Owner(user),ct);if(quiz==null)return Results.NotFound();
 var data=new SaveQuiz(quiz.Title,quiz.MaterialId,quiz.Questions.OrderBy(x=>x.Position).Select(x=>new QuestionInput(x.Type,x.Text,x.Points,x.ModelAnswer,x.Options.OrderBy(o=>o.Position).Select(o=>new OptionInput(o.Text,o.IsCorrect)).ToList())).ToList());
 if(TeachingValidation.Quiz(data)!=null)return Results.BadRequest(new{message="Проверьте вопросы теста перед публикацией."});
 var p=await db.QuizPublications.SingleOrDefaultAsync(x=>x.Id==id,ct);if(p==null){p=new PublishedQuiz{Id=id,TeacherId=quiz.TeacherId};db.QuizPublications.Add(p);}
 var source=await db.Materials.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==quiz.MaterialId&&x.TeacherId==quiz.TeacherId,ct);
 p.Title=quiz.Title;p.MaterialId=quiz.MaterialId;p.AuthorName=await db.Teachers.Where(x=>x.Id==quiz.TeacherId).Select(x=>x.Name).SingleAsync(ct);p.Subject=source?.Subject??"";p.Language=source?.Language??"";p.Level=source?.Level??"";p.Snapshot=JsonSerializer.Serialize(data);p.SearchText=$"{p.Title} {p.Subject} {p.AuthorName}".ToLowerInvariant();p.PublishedAt=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();p.Revision=Guid.NewGuid();
 try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){return Results.Conflict(new{message="Публикация изменилась. Повторите действие."});}catch(DbUpdateException ex)when(ex.InnerException is Npgsql.PostgresException{SqlState:"23505"}){return Results.Conflict(new{message="Публикация изменилась. Повторите действие."});}
 return Results.Ok(new{Published=true,p.PublishedAt});});
 own.MapDelete("/",async(Guid id,ClaimsPrincipal user,MaterialDbContext db,CancellationToken ct)=>{if(!await db.Quizzes.AnyAsync(x=>x.Id==id&&x.TeacherId==Owner(user),ct))return Results.NotFound();var p=await db.QuizPublications.SingleOrDefaultAsync(x=>x.Id==id,ct);if(p!=null){db.QuizPublications.Remove(p);try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){return Results.Conflict();}}return Results.NoContent();});
 var library=app.MapGroup("/api/library/tests").RequireAuthorization();
 library.MapGet("/",async(string? q,string? subject,string? language,bool? mine,int? page,ClaimsPrincipal user,MaterialDbContext db,CancellationToken ct)=>{var number=page??1;if(number<1||number>10000||q?.Length>200||subject?.Length>100||language?.Length>50)return Results.BadRequest();var query=db.QuizPublications.AsNoTracking();var owner=Owner(user);if(!string.IsNullOrWhiteSpace(q)){var search=q.Trim().ToLowerInvariant();query=query.Where(x=>x.SearchText.Contains(search));}if(!string.IsNullOrWhiteSpace(subject))query=query.Where(x=>x.Subject==subject);if(!string.IsNullOrWhiteSpace(language))query=query.Where(x=>x.Language==language);if(mine==true)query=query.Where(x=>x.TeacherId==owner);var total=await query.CountAsync(ct);var items=await query.OrderByDescending(x=>x.PublishedAt).ThenBy(x=>x.Id).Skip((number-1)*20).Take(20).Select(x=>new{x.Id,x.Title,x.Subject,x.Level,x.Language,x.AuthorName,x.PublishedAt,IsOwn=x.TeacherId==owner}).ToListAsync(ct);return Results.Ok(new{items,total,page=number,pageSize=20});});
 library.MapGet("/filters",async(MaterialDbContext db,CancellationToken ct)=>Results.Ok(new{Subjects=await db.QuizPublications.Select(x=>x.Subject).Distinct().OrderBy(x=>x).Take(200).ToListAsync(ct),Languages=await db.QuizPublications.Select(x=>x.Language).Distinct().OrderBy(x=>x).Take(100).ToListAsync(ct)}));
 library.MapGet("/{id:guid}",async(Guid id,ClaimsPrincipal user,MaterialDbContext db,CancellationToken ct)=>{var p=await db.QuizPublications.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);if(p==null)return Results.NotFound();var data=JsonSerializer.Deserialize<SaveQuiz>(p.Snapshot)!;var content=string.Join("\n\n",data.Questions.Select((x,i)=>$"{i+1}. {x.Text} ({x.Points} баллов)\n"+string.Join("\n",x.Options.Select(o=>$"{(o.IsCorrect?"✓":"○")} {o.Text}"))+(string.IsNullOrEmpty(x.ModelAnswer)?"":"\nОтвет: "+x.ModelAnswer)));return Results.Ok(new{p.Id,p.Title,p.Subject,p.Level,p.Language,p.AuthorName,p.PublishedAt,Content=content,IsOwn=p.TeacherId==Owner(user)});});
 library.MapPost("/{id:guid}/copy",async(Guid id,ClaimsPrincipal user,MaterialDbContext db,CancellationToken ct)=>{var p=await db.QuizPublications.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);if(p==null)return Results.NotFound();var copy=Copy(p,Owner(user));db.Quizzes.Add(copy);await db.SaveChangesAsync(ct);return Results.Created($"/api/tests/{copy.Id}",new{copy.Id});});
 }
}
