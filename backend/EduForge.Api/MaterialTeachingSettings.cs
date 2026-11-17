using System.Text.Json;
using EduForge.Application;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EduForge.Api;

public static class MaterialTeachingSettings
{
    public static async Task<MaterialTeachingContext> Read(Material material, MaterialDbContext db, CancellationToken ct)
    {
        var requirements = "";
        if (material.GenerationRunId is { } id) {
            var parameters = await db.GenerationRuns.AsNoTracking()
                .Where(r => r.Id == id && r.TeacherId == material.TeacherId && (r.Kind == "Material" || r.Kind == "SourceMaterial") && r.Status == "Completed")
                .Select(r => r.Parameters).SingleOrDefaultAsync(ct);
            if (parameters is not null) {
                try {
                    using var json = JsonDocument.Parse(parameters);
                    var property = json.RootElement.EnumerateObject().FirstOrDefault(p => p.Name.Equals("requirements", StringComparison.OrdinalIgnoreCase));
                    if (property.Value.ValueKind == JsonValueKind.String) requirements = property.Value.GetString() ?? "";
                } catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
            }
        }
        return new(material.Level, material.Language, requirements.Length > 4000 ? requirements[..4000] : requirements);
    }
}
