using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using EduForge.Application;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EduForge.Api;

public static class SourceDocumentEndpoints
{
    private static Guid Owner(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public static void MapSources(this WebApplication app)
    {
        var routes = app.MapGroup("/api/sources").RequireAuthorization();
        routes.MapGet("/", async (ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => Results.Ok(await db.SourceDocuments.AsNoTracking()
            .Where(s => s.TeacherId == Owner(user)).OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.Id, s.Name, s.Format, s.CharacterCount, s.CreatedAt }).ToListAsync(ct)));
        routes.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var doc = await db.SourceDocuments.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.TeacherId == Owner(user), ct);
            return doc is null ? Results.NotFound() : Results.Ok(new { doc.Id, doc.Name, doc.Format, doc.Sha256, Sections = JsonSerializer.Deserialize<List<SourceSection>>(doc.Sections, ControlledLearning.Json) });
        });
        routes.MapPost("/", async (HttpRequest request, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            if (!request.HasFormContentType || request.ContentLength > 11 * 1024 * 1024) return Results.BadRequest(new { message = "Загрузите источник до 10 МБ." });
            var form = await request.ReadFormAsync(ct); var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0 || file.Length > 10 * 1024 * 1024) return Results.BadRequest(new { message = "Загрузите PDF, DOCX, TXT или Markdown до 10 МБ." });
            if (await db.SourceDocuments.CountAsync(s => s.TeacherId == Owner(user), ct) >= 100) return Results.BadRequest(new { message = "Достигнут лимит 100 источников." });
            var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
            name = new string(name.Where(c => !char.IsControl(c)).Take(200).ToArray());
            using var buffer = new MemoryStream(); await file.CopyToAsync(buffer, ct); var bytes = buffer.ToArray();
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var duplicate = await db.SourceDocuments.AsNoTracking().SingleOrDefaultAsync(s => s.TeacherId == Owner(user) && s.Sha256 == hash, ct);
            if (duplicate is not null) return Results.Ok(new { duplicate.Id, duplicate.Name, duplicate.Format, duplicate.CharacterCount, duplicate.CreatedAt });
            List<SourceSection> sections;
            try { sections = SourceExtraction.Extract(name, bytes, ct); }
            catch (InvalidDataException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Results.BadRequest(new { message = "Не удалось прочитать источник. Проверьте формат, кодировку UTF-8 и отсутствие пароля у PDF." }); }
            var source = new SourceDocument { TeacherId = Owner(user), Name = name, Format = Path.GetExtension(name).ToLowerInvariant(), Sha256 = hash,
                Sections = JsonSerializer.Serialize(sections, ControlledLearning.Json), CharacterCount = sections.Sum(s => s.Text.Length) };
            db.SourceDocuments.Add(source);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Results.Conflict(new { message = "Источник уже загружается в другом окне. Обновите список и повторите выбор." }); }
            return Results.Created($"/api/sources/{source.Id}", new { source.Id, source.Name, source.Format, source.CharacterCount, source.CreatedAt });
        }).RequireRateLimiting("ai");

        app.MapPost("/api/ai/material/from-sources", async (SourceMaterialRequest input, ClaimsPrincipal user, MaterialDbContext db,
            ISourceMaterialAiService ai, GenerationRecorder recorder, CancellationToken ct) => {
            var p = input.Material;
            if (p is null || !TeachingValidation.Text(p.Topic, 200) || !TeachingValidation.Text(p.Subject, 100) || !TeachingValidation.Text(p.Level, 50) || !TeachingValidation.Text(p.Language, 50) || p.Requirements is null || p.Requirements.Length > 4000 ||
                input.Sources is null || input.Sources.Count is < 1 or > 8 || input.Sources.Any(s => s is null || s.SectionIds is null || s.SectionIds.Count == 0 || s.SectionIds.Count > 60 || s.SectionIds.Distinct().Count() != s.SectionIds.Count) ||
                input.Sources.Select(s => s.DocumentId).Distinct().Count() != input.Sources.Count)
                return Results.BadRequest(new { message = "Укажите тему, предмет и фрагменты от 1 до 8 источников." });
            var excerpts = new List<SourceExcerpt>();
            foreach (var selection in input.Sources) {
                var source = await db.SourceDocuments.AsNoTracking().SingleOrDefaultAsync(s => s.Id == selection.DocumentId && s.TeacherId == Owner(user), ct);
                if (source is null) return Results.NotFound();
                var sections = JsonSerializer.Deserialize<List<SourceSection>>(source.Sections, ControlledLearning.Json)!;
                if (selection.SectionIds.Any(id => sections.All(s => s.Id != id))) return Results.BadRequest(new { message = "Один из выбранных фрагментов не найден." });
                excerpts.AddRange(sections.Where(s => selection.SectionIds.Contains(s.Id)).Select(s => new SourceExcerpt(source.Id, s.Id, source.Name, s.Label, s.Text)));
            }
            if (excerpts.Count > 60 || excerpts.Sum(s => s.Text.Length) > 50000) return Results.BadRequest(new { message = "Выберите до 60 фрагментов общим объёмом до 50 000 символов." });
            try {
                var (run, lesson) = await recorder.Record(Owner(user), "SourceMaterial", "source-lesson-v2", p, excerpts, ai, async () => {
                    var generated = await ai.GenerateFromSourcesAsync(p, excerpts, ct);
                    Validate(generated, excerpts);
                    return generated with { Blocks = generated.Blocks.Select(b => b with { Id = Guid.NewGuid() }).ToList() };
                }, ct);
                return Results.Ok(new { Content = BuildDocument(lesson), GenerationRunId = run.Id });
            } catch (AiUnavailableException ex) { return Results.Problem(ex.Message, statusCode: 503); }
            catch (HttpRequestException) { return Results.Problem("Не удалось связаться с AI-провайдером.", statusCode: 503); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Results.Problem("AI не ответил вовремя.", statusCode: 504); }
        }).RequireAuthorization().RequireRateLimiting("ai");
    }

    internal static void Validate(GeneratedSourceLesson lesson, List<SourceExcerpt> sources)
    {
        if (lesson.Blocks is null || lesson.Blocks.Count is < 1 or > 50 || lesson.Blocks.Sum(b => b?.Text?.Length ?? 0) > 100000)
            throw new AiUnavailableException("AI вернул некорректную структуру материала.");
        foreach (var b in lesson.Blocks) {
            if (b is null || b.Kind is not ("heading" or "paragraph" or "codeBlock" or "bulletList" or "screenshotPlaceholder") || !TeachingValidation.Text(b.Text, 12000) || b.Language is null || b.Language.Length > 30 ||
                b.Evidence is null || b.Evidence.Count > 5 || b.Kind is not ("heading" or "screenshotPlaceholder") && b.Evidence.Count == 0)
                throw new AiUnavailableException("AI не предоставил основания для учебного блока. Повторите запрос.");
            foreach (var evidence in b.Evidence) {
                var section = evidence is null ? null : sources.SingleOrDefault(s => s.DocumentId == evidence.DocumentId && s.SectionId == evidence.SectionId);
                if (section is null || !TeachingValidation.Text(evidence!.Quote, 4000) || !MaterialStructure.NormalizeText(section.Text).Contains(MaterialStructure.NormalizeText(evidence.Quote), StringComparison.Ordinal))
                    throw new AiUnavailableException("AI указал неподтверждённую цитату. Материал не принят; уточните исходные фрагменты и повторите запрос.");
            }
        }
    }

    internal static string BuildDocument(GeneratedSourceLesson lesson)
    {
        object Text(string text) => new { type = "text", text };
        var nodes = lesson.Blocks.Select(b => b.Kind switch {
            "screenshotPlaceholder" => (object)new { type = "screenshotPlaceholder", attrs = new { blockId = b.Id, description = b.Text } },
            "heading" => (object)new { type = "heading", attrs = new { blockId = b.Id, level = 2 }, content = new[] { Text(b.Text) } },
            "codeBlock" => new { type = "codeBlock", attrs = new { blockId = b.Id, language = b.Language }, content = new[] { Text(b.Text) } },
            "bulletList" => new { type = "bulletList", attrs = new { blockId = b.Id }, content = b.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(t => new { type = "listItem", content = new[] { new { type = "paragraph", content = new[] { Text(t.TrimStart('-', '*', ' ')) } } } }) },
            _ => new { type = "paragraph", attrs = new { blockId = b.Id }, content = new[] { Text(b.Text) } }
        });
        return JsonSerializer.Serialize(new { type = "doc", content = nodes }, ControlledLearning.Json);
    }
}
