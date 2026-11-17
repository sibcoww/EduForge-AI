using System.Security.Claims;
using EduForge.Application;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EduForge.Api;

public static class ImageCaptionEndpoints
{
    public static void MapImageCaptions(this WebApplication app)
    {
        app.MapPost("/api/ai/image-caption", async (ImageCaptionRequest input, ClaimsPrincipal user, MaterialDbContext db, IImageCaptionAiService ai, CancellationToken ct) => {
            const string prefix = "/api/media/";
            if (input.ImageUrl is null || !input.ImageUrl.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(input.ImageUrl[prefix.Length..], "D", out var id) ||
                input.Context is null || input.Context.Length > 1500 || input.Annotations is null || input.Annotations.Count > 30 ||
                input.Annotations.Any(a => a is null || a.Number is < 1 or > 30 || a.Kind is not ("rect" or "ellipse" or "arrow") ||
                    a.Color is not ("#dc2626" or "#2563eb" or "#16a34a" or "#d97706" or "#9333ea") || a.Label is null || a.Label.Length > 300) ||
                !input.Annotations.Select(a => a.Number).SequenceEqual(Enumerable.Range(1, input.Annotations.Count)))
                return Results.BadRequest(new { message = "Выберите загруженный скриншот и до 30 последовательно пронумерованных выделений." });
            var owner = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            // Never fetch a client-supplied URL or send another teacher's private image to AI.
            var asset = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.TeacherId == owner, ct);
            if (asset is null) return Results.NotFound();
            if (asset.Data.Length > 5 * 1024 * 1024 || asset.ContentType is not ("image/png" or "image/jpeg" or "image/webp"))
                return Results.BadRequest(new { message = "Используйте PNG, JPEG или WebP до 5 МБ." });
            try { return Results.Ok(new { caption = await ai.GenerateCaptionAsync(asset.Data, asset.ContentType, input, ct) }); }
            catch (AiUnavailableException ex) { return Results.Problem(ex.Message, statusCode: 503); }
            catch (HttpRequestException) { return Results.Problem("Не удалось связаться с AI-провайдером.", statusCode: 503); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Results.Problem("AI не ответил вовремя.", statusCode: 504); }
        }).RequireAuthorization().RequireRateLimiting("ai");
    }
}
