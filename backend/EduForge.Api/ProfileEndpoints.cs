using System.Security.Claims;
using System.Text.Json;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace EduForge.Api;
public record ProfileInput(string Name,string Bio,int? Age,string Country,string City,string AvatarUrl,List<string> Disciplines);
public static class ProfileEndpoints {
 public static void MapProfile(this WebApplication app){var routes=app.MapGroup("/api/profile").RequireAuthorization();
 routes.MapGet("/",async(ClaimsPrincipal user,MaterialDbContext db,CancellationToken ct)=>{var id=Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);var t=await db.Teachers.SingleAsync(x=>x.Id==id,ct);return Results.Ok(new{t.Name,t.Email,t.Bio,t.Age,t.Country,t.City,t.AvatarUrl,Disciplines=JsonSerializer.Deserialize<List<string>>(t.Disciplines)});});
 routes.MapPut("/",async(ProfileInput input,ClaimsPrincipal user,MaterialDbContext db,CancellationToken ct)=>{
 if(string.IsNullOrWhiteSpace(input.Name)||input.Name.Length>100||input.Bio is null||input.Bio.Length>2000||input.Country is null||input.Country.Length>100||input.City is null||input.City.Length>100||input.Age is < 18 or > 120||input.Disciplines is null||input.Disciplines.Count>30||input.Disciplines.Any(x=>string.IsNullOrWhiteSpace(x)||x.Length>100)||input.AvatarUrl is null)return Results.BadRequest(new{message="Проверьте профиль: имя до 100 символов, описание до 2000, возраст от 18 до 120, до 30 дисциплин."});
 var id=Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);if(input.AvatarUrl!=""){if(!Guid.TryParse(input.AvatarUrl.Split('/').Last(),out var mediaId)||input.AvatarUrl!=$"/api/media/{mediaId}"||!await db.MediaAssets.AnyAsync(x=>x.Id==mediaId&&x.TeacherId==id,ct))return Results.BadRequest(new{message="Загрузите собственную аватарку."});}
 var t=await db.Teachers.SingleAsync(x=>x.Id==id,ct);t.Name=input.Name.Trim();t.Bio=input.Bio.Trim();t.Country=input.Country.Trim();t.City=input.City.Trim();t.Age=input.Age;t.AvatarUrl=input.AvatarUrl;t.Disciplines=JsonSerializer.Serialize(input.Disciplines.Select(x=>x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList());await db.SaveChangesAsync(ct);return Results.NoContent();});
 }
}
