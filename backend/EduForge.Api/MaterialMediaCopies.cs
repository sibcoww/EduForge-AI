using System.Text.Json;
using System.Text.RegularExpressions;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace EduForge.Api;
public static class MaterialMediaCopies {
 public static async Task<string> Clone(string content,Guid owner,MaterialDbContext db,CancellationToken ct){
  foreach(var match in Regex.Matches(content,@"/api/media/([0-9a-fA-F-]{36})").Cast<Match>().DistinctBy(x=>x.Value)){
   if(!Guid.TryParse(match.Groups[1].Value,out var id))continue;
   var source=await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);if(source is null)continue;
   var copy=new MediaAsset{TeacherId=owner,ContentType=source.ContentType,Data=source.Data};db.MediaAssets.Add(copy);content=content.Replace(match.Value,$"/api/media/{copy.Id}");
  }return content;
 }
 public static string Baseline(PublishedMaterial publication,string content){var snapshot=JsonSerializer.Deserialize<PublishedMaterial>(JsonSerializer.Serialize(publication))!;snapshot.Content=content;return JsonSerializer.Serialize(snapshot);}
}
