using System.Security.Claims;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace EduForge.Api;
public static class MediaEndpoints {
 public static void MapMedia(this WebApplication app){
 app.MapPost("/api/media",async(HttpRequest request,ClaimsPrincipal user,MaterialDbContext db,CancellationToken ct)=>{
 if(!request.HasFormContentType)return Results.BadRequest();var form=await request.ReadFormAsync(ct);var file=form.Files.GetFile("file");if(file is null||file.Length==0||file.Length>5*1024*1024)return Results.BadRequest(new{message="Загрузите PNG, JPEG или WebP размером до 5 МБ."});
 using var stream=new MemoryStream();await file.CopyToAsync(stream,ct);var bytes=stream.ToArray();string? type=bytes.Length>=8&&bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})?"image/png":bytes.Length>=3&&bytes[0]==255&&bytes[1]==216&&bytes[2]==255?"image/jpeg":bytes.Length>=12&&System.Text.Encoding.ASCII.GetString(bytes,0,4)=="RIFF"&&System.Text.Encoding.ASCII.GetString(bytes,8,4)=="WEBP"?"image/webp":null;
 if(type is null)return Results.BadRequest(new{message="Неподдерживаемый формат изображения."});var asset=new MediaAsset{TeacherId=Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),ContentType=type,Data=bytes};db.MediaAssets.Add(asset);await db.SaveChangesAsync(ct);return Results.Ok(new{url=$"/api/media/{asset.Id}"});}).RequireAuthorization().RequireRateLimiting("ai");
 app.MapGet("/api/media/{id:guid}",async(Guid id,string? share,ClaimsPrincipal user,MaterialDbContext db,HttpResponse response,IConfiguration configuration,CancellationToken ct)=>{
  var owner=Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
  var asset=await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);if(asset is null)return Results.NotFound();
  var reference=$"/api/media/{id}";var allowed=asset.TeacherId==owner;
  if(!allowed){
   var publicContents=await ContentSharing.PublicMaterials(db).Where(p=>p.TeacherId==asset.TeacherId&&p.Content.Contains(reference)).Select(p=>p.Content).ToListAsync(ct);
   allowed=publicContents.Any(content=>ContentSharing.SharedMaterial(content).Contains(reference,StringComparison.Ordinal));
  }
  if(!allowed&&share is {Length:64})allowed=await db.ContentAccesses.AnyAsync(a=>a.Token==share&&a.TeacherId==asset.TeacherId&&a.Kind=="material"&&a.Visibility=="Unlisted"&&a.Snapshot.Contains(reference)&&db.Materials.Any(m=>m.Id==a.SourceId&&m.TeacherId==a.TeacherId),ct);
  if(!allowed){
   var email=await db.Teachers.Where(t=>t.Id==owner).Select(t=>t.Email).SingleAsync(ct);
   allowed=AdminEndpoints.IsAdmin(email,configuration)&&await db.PublicationReviews.AnyAsync(r=>r.TeacherId==asset.TeacherId&&r.Status=="Pending"&&r.Payload.Contains(reference),ct);
  }
  if(!allowed)return Results.NotFound();response.Headers["X-Content-Type-Options"]="nosniff";response.Headers.CacheControl=share is null?"private, max-age=3600":"private, no-store";return Results.File(asset.Data,asset.ContentType);
 }).RequireAuthorization();
 }
}
