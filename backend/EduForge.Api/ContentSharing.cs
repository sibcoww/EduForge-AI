using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using EduForge.Application;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EduForge.Api;
public record ChangeAccess(string Visibility, Guid? Version = null);
public record SharedQuestion(QuestionType Type,string Text,int Points,List<string> Options);
public record SharedContent(string Kind,string Title,string AuthorName,string Subject,string Level,string Language,string Content,List<SharedQuestion> Questions);

public sealed class ContentSharing(MaterialDbContext db,IConfiguration configuration,ControlledLearning learning)
{
    public static IQueryable<PublishedMaterial> PublicMaterials(MaterialDbContext db) => db.Publications.Where(p=>!db.ContentAccesses.Any(a=>a.SourceId==p.Id&&a.Kind=="material"&&a.Visibility!="Public"));
    public static IQueryable<PublishedQuiz> PublicTests(MaterialDbContext db) => db.QuizPublications.Where(p=>!db.ContentAccesses.Any(a=>a.SourceId==p.Id&&a.Kind=="test"&&a.Visibility!="Public"));
    private Task<bool> Owned(string kind,Guid id,Guid owner,CancellationToken ct)=>kind=="material"?db.Materials.AnyAsync(m=>m.Id==id&&m.TeacherId==owner,ct):kind=="test"?db.Quizzes.AnyAsync(q=>q.Id==id&&q.TeacherId==owner,ct):Task.FromResult(false);
    public async Task<IResult> State(string kind,Guid id,Guid owner,CancellationToken ct){
        if(!await Owned(kind,id,owner,ct))return Results.NotFound();
        var access=await db.ContentAccesses.AsNoTracking().SingleOrDefaultAsync(a=>a.Kind==kind&&a.SourceId==id&&a.TeacherId==owner,ct);
        long? publishedAt=kind=="material"?await db.Publications.Where(p=>p.Id==id).Select(p=>(long?)p.PublishedAt).SingleOrDefaultAsync(ct):await db.QuizPublications.Where(p=>p.Id==id).Select(p=>(long?)p.PublishedAt).SingleOrDefaultAsync(ct);
        var visibility=access?.Visibility??(publishedAt.HasValue?"Public":"Private");
        var review=await db.PublicationReviews.AsNoTracking().Where(r=>r.SourceId==id&&r.Kind==kind).OrderByDescending(r=>r.CreatedAt).FirstOrDefaultAsync(ct);
        var published=visibility=="Public"&&publishedAt.HasValue;
        return Results.Ok(new{Visibility=visibility,access?.Version,Published=published,PublishedAt=published?publishedAt:null,Status=review?.Status,Reason=review?.Reason,
            SharePath=access?.Token is { } token&&(visibility=="Unlisted"||published)?$"/shared/{token}":null});
    }
    public async Task<IResult> Set(string kind,Guid id,Guid owner,ChangeAccess input,CancellationToken ct,bool enforceVersion=true){
        if(input.Visibility is not ("Private" or "Unlisted" or "Public"))return Results.BadRequest(new{message="Выберите частный доступ, доступ по ссылке или публичный доступ."});
        if(!await Owned(kind,id,owner,ct))return Results.NotFound();
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
        var access=await db.ContentAccesses.SingleOrDefaultAsync(a=>a.Kind==kind&&a.SourceId==id&&a.TeacherId==owner,ct);
        if(enforceVersion&&input.Version!=access?.Version)return Results.Conflict(new{message="Настройки доступа изменились в другом окне. Обновите их перед сохранением."});
        PublishedMaterial? material=null;PublishedQuiz? test=null;SharedContent? preview=null;
        if(input.Visibility!="Private"){
            var author=await db.Teachers.Where(t=>t.Id==owner).Select(t=>t.Name).SingleAsync(ct);
            if(kind=="material"){
                var source=await db.Materials.AsNoTracking().SingleAsync(m=>m.Id==id,ct);
                if(string.IsNullOrWhiteSpace(MaterialText.Extract(source.Content)))return Results.BadRequest(new{message="Добавьте содержание перед открытием доступа."});
                material=new(){Id=id,TeacherId=owner,AuthorName=author,Title=source.Title,Subject=source.Subject,Level=source.Level,Language=source.Language,Content=SharedMaterial(source.Content),PublishedAt=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),Revision=Guid.NewGuid()};
                material.SearchText=$"{material.Title} {material.Subject} {author}".ToLowerInvariant();preview=Preview(material);
            }else{
                var source=await db.Quizzes.Include(q=>q.Questions).ThenInclude(q=>q.Options).SingleAsync(q=>q.Id==id,ct);
                if(!await learning.CanUse(source,owner,ct))return Results.Conflict(new{message="Перед открытием доступа подтвердите цели, основания ответов и проверьте покрытие теста."});
                var data=new SaveQuiz(source.Title,source.MaterialId,source.Questions.OrderBy(q=>q.Position).Select(q=>new QuestionInput(q.Type,q.Text,q.Points,q.ModelAnswer,q.Options.OrderBy(o=>o.Position).Select(o=>new OptionInput(o.Text,o.IsCorrect)).ToList())).ToList());
                if(TeachingValidation.Quiz(data)!=null)return Results.BadRequest(new{message="Проверьте вопросы теста перед открытием доступа."});
                var lesson=await db.Materials.AsNoTracking().SingleOrDefaultAsync(m=>m.Id==source.MaterialId&&m.TeacherId==owner,ct);
                test=new(){Id=id,TeacherId=owner,MaterialId=source.MaterialId,Title=source.Title,AuthorName=author,Subject=lesson?.Subject??"",Level=lesson?.Level??"",Language=lesson?.Language??"",Snapshot=JsonSerializer.Serialize(data),PublishedAt=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),Revision=Guid.NewGuid()};
                test.SearchText=$"{test.Title} {test.Subject} {author}".ToLowerInvariant();preview=Preview(test);
            }
        }
        if(access is null){access=new(){Kind=kind,SourceId=id,TeacherId=owner};db.ContentAccesses.Add(access);}
        access.Version=Guid.NewGuid();access.Visibility=input.Visibility;
        access.Token=input.Visibility=="Private"?null:access.Token??Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        access.Snapshot=preview is null?"":JsonSerializer.Serialize(preview,ControlledLearning.Json);
        await db.PublicationReviews.Where(r=>r.SourceId==id&&r.Kind==kind&&(r.Status=="Pending"||r.Status=="Processing")).ExecuteUpdateAsync(s=>s.SetProperty(r=>r.Status,"Superseded"),ct);
        if(input.Visibility!="Public"){
            if(kind=="material")await db.Publications.Where(p=>p.Id==id).ExecuteDeleteAsync(ct);else await db.QuizPublications.Where(p=>p.Id==id).ExecuteDeleteAsync(ct);
        }else if(configuration.GetValue("Moderation:Enabled",true)){
            var payload=material is not null?JsonSerializer.SerializeToNode(material)!:JsonSerializer.SerializeToNode(test)!;
            payload["AccessVersion"]=access.Version.ToString();
            db.PublicationReviews.Add(new(){TeacherId=owner,SourceId=id,Kind=kind,Title=preview!.Title,Payload=payload.ToJsonString()});
        }else if(material is not null){
            var old=await db.Publications.SingleOrDefaultAsync(p=>p.Id==id,ct);if(old is null)db.Publications.Add(material);else db.Entry(old).CurrentValues.SetValues(material);
        }else if(test is not null){
            var old=await db.QuizPublications.SingleOrDefaultAsync(p=>p.Id==id,ct);if(old is null)db.QuizPublications.Add(test);else db.Entry(old).CurrentValues.SetValues(test);
        }
        try{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
        catch(DbUpdateException){return Results.Conflict(new{message="Доступ изменился одновременно с другим запросом. Обновите настройки и повторите действие."});}
        return await State(kind,id,owner,ct);
    }
    public static SharedContent Preview(PublishedMaterial p)=>new("material",p.Title,p.AuthorName,p.Subject,p.Level,p.Language,SharedMaterial(p.Content),[]);
    public static SharedContent Preview(PublishedQuiz p){var data=JsonSerializer.Deserialize<SaveQuiz>(p.Snapshot)!;return new("test",p.Title,p.AuthorName,p.Subject,p.Level,p.Language,"",data.Questions.Select(q=>new SharedQuestion(q.Type,AiText.Plain(q.Text),q.Points,q.Options.Select(o=>AiText.Plain(o.Text)).ToList())).ToList());}
    public static string SharedMaterial(string content){
        try{var json=JsonNode.Parse(content);void Strip(JsonNode? node){if(node is JsonObject obj){if(obj["attrs"] is JsonObject attrs){attrs.Remove("originalSrc");attrs.Remove("annotations");}foreach(var child in obj.ToList())Strip(child.Value);}else if(node is JsonArray items)foreach(var child in items)Strip(child);}Strip(json);return json?.ToJsonString()??content;}catch(JsonException){return content;}
    }
    public static void Map(WebApplication app){
        static Guid Owner(ClaimsPrincipal user)=>Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        app.MapGet("/api/access/{kind}/{id:guid}",(string kind,Guid id,ClaimsPrincipal user,ContentSharing sharing,CancellationToken ct)=>sharing.State(kind,id,Owner(user),ct)).RequireAuthorization();
        app.MapPut("/api/access/{kind}/{id:guid}",(string kind,Guid id,ChangeAccess input,ClaimsPrincipal user,ContentSharing sharing,CancellationToken ct)=>sharing.Set(kind,id,Owner(user),input,ct)).RequireAuthorization();
        app.MapGet("/api/shared/{token}",async(string token,MaterialDbContext db,HttpResponse response,CancellationToken ct)=>{
            response.Headers.CacheControl="private, no-store";
            if(token.Length!=64||token.Any(c=>!char.IsAsciiHexDigit(c)))return Results.NotFound();
            var access=await db.ContentAccesses.AsNoTracking().SingleOrDefaultAsync(a=>a.Token==token&&a.Visibility!="Private",ct);if(access is null)return Results.NotFound();
            if(!await (access.Kind=="material"?db.Materials.AnyAsync(m=>m.Id==access.SourceId&&m.TeacherId==access.TeacherId,ct):db.Quizzes.AnyAsync(q=>q.Id==access.SourceId&&q.TeacherId==access.TeacherId,ct)))return Results.NotFound();
            if(access.Visibility=="Unlisted")return Results.Text(access.Snapshot,"application/json");
            if(access.Kind=="material"){var p=await PublicMaterials(db).AsNoTracking().SingleOrDefaultAsync(p=>p.Id==access.SourceId,ct);return p is null?Results.NotFound():Results.Ok(Preview(p));}
            var test=await PublicTests(db).AsNoTracking().SingleOrDefaultAsync(p=>p.Id==access.SourceId,ct);return test is null?Results.NotFound():Results.Ok(Preview(test));
        }).RequireAuthorization().RequireRateLimiting("code");
    }
}
