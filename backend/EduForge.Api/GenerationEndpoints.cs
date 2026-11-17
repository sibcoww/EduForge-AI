using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EduForge.Application;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EduForge.Api;

public record GenerationEvaluation(string Experiment, decimal? PreparationMinutes, int? Correctness, int? Clarity,
    int? Alignment, int? CorrectedQuestions, string Note);
public static class GenerationEndpoints
{
    private static Guid Owner(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public static void MapGenerationHistory(this WebApplication app)
    {
        var routes = app.MapGroup("/api/generations").RequireAuthorization();
        routes.MapGet("/", async (ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => Results.Ok(await db.GenerationRuns.AsNoTracking()
            .Where(r => r.TeacherId == Owner(user)).OrderByDescending(r => r.StartedAt).Take(200)
            .Select(r => new { r.Id, r.Kind, r.TemplateVersion, r.Model, r.Status, r.StartedAt, r.DurationMs, r.InputTokens, r.OutputTokens,
                r.Experiment, r.PreparationMinutes, r.Correctness, r.Clarity, r.Alignment, r.CorrectedQuestions }).ToListAsync(ct)));
        routes.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var run = await db.GenerationRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.TeacherId == Owner(user), ct);
            return run is null ? Results.NotFound() : Results.Ok(new { run.Id, run.Kind, run.TemplateVersion, run.Model, run.Status, run.StartedAt, run.FinishedAt,
                run.DurationMs, run.InputTokens, run.OutputTokens, run.Failure, Parameters = JsonSerializer.Deserialize<JsonElement>(run.Parameters),
                Sources = JsonSerializer.Deserialize<JsonElement>(run.SourceSnapshot), Result = run.Result.Length == 0 ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(run.Result),
                Evaluation = new GenerationEvaluation(run.Experiment, run.PreparationMinutes, run.Correctness, run.Clarity, run.Alignment, run.CorrectedQuestions, run.EvaluationNote) });
        });
        routes.MapPut("/{id:guid}/evaluation", async (Guid id, GenerationEvaluation input, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            if (input.Experiment is null || input.Experiment.Length > 100 || input.Note is null || input.Note.Length > 4000 ||
                input.PreparationMinutes is < 0 or > 1440 || input.Correctness is < 1 or > 5 || input.Clarity is < 1 or > 5 || input.Alignment is < 1 or > 5 || input.CorrectedQuestions is < 0 or > 1000)
                return Results.BadRequest(new { message = "Время: 0–1440 минут; оценки: 1–5; исправления: 0–1000. Неизмеренные показатели оставьте пустыми." });
            var run = await db.GenerationRuns.SingleOrDefaultAsync(r => r.Id == id && r.TeacherId == Owner(user), ct);
            if (run is null) return Results.NotFound();
            if (run.Status != "Completed") return Results.Conflict(new { message = "Оценка доступна для завершённой генерации." });
            run.Experiment = input.Experiment.Trim(); run.PreparationMinutes = input.PreparationMinutes; run.Correctness = input.Correctness;
            run.Clarity = input.Clarity; run.Alignment = input.Alignment; run.CorrectedQuestions = input.CorrectedQuestions; run.EvaluationNote = input.Note.Trim();
            await db.SaveChangesAsync(ct); return Results.NoContent();
        });
        routes.MapGet("/export.csv", async (ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var runs = await db.GenerationRuns.AsNoTracking().Where(r => r.TeacherId == Owner(user)).OrderByDescending(r => r.StartedAt).Take(1000).ToListAsync(ct);
            runs.Reverse();
            var csv = new StringBuilder("run_id;mode;template;model;status;started_at;duration_ms;input_tokens;output_tokens;experiment;preparation_minutes;correctness;clarity;alignment;corrected_questions;note\r\n");
            foreach (var run in runs) csv.AppendLine(string.Join(';', new object?[] { run.Id, run.Kind, run.TemplateVersion, run.Model, run.Status,
                DateTimeOffset.FromUnixTimeMilliseconds(run.StartedAt).ToString("O"), run.DurationMs, run.InputTokens, run.OutputTokens,
                run.Experiment, run.PreparationMinutes, run.Correctness, run.Clarity, run.Alignment, run.CorrectedQuestions, run.EvaluationNote }.Select(Csv)));
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", "eduforge-generation-experiment.csv");
        });
        app.MapGet("/api/materials/{id:guid}/source-evidence", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var material = await db.Materials.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id && m.TeacherId == Owner(user), ct);
            if (material is null) return Results.NotFound();
            var run = await db.GenerationRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == material.GenerationRunId && r.TeacherId == Owner(user) && r.Kind == "SourceMaterial", ct);
            if (run is null) return Results.Ok(new { Blocks = Array.Empty<object>() });
            var lesson = JsonSerializer.Deserialize<GeneratedSourceLesson>(run.Result, ControlledLearning.Json)!;
            var sources = JsonSerializer.Deserialize<List<SourceExcerpt>>(run.SourceSnapshot, ControlledLearning.Json)!;
            var expected = MaterialStructure.Read(material.Id, SourceDocumentEndpoints.BuildDocument(lesson));
            var current = MaterialStructure.Read(material.Id, material.Content);
            return Results.Ok(new { run.Id, Blocks = lesson.Blocks.Where(b => b.Kind != "screenshotPlaceholder").Select(b => {
                var now = current.SingleOrDefault(x => x.Id == b.Id); var before = expected.SingleOrDefault(x => x.Id == b.Id);
                return new { b.Id, OriginalText = before?.Text, CurrentText = now?.Text, Changed = now is null || now.Text != before?.Text,
                    Evidence = b.Evidence.Select(e => { var source = sources.Single(s => s.DocumentId == e.DocumentId && s.SectionId == e.SectionId); return new { source.DocumentName, source.Label, e.Quote, SourceText = source.Text }; }) };
            }) });
        }).RequireAuthorization();
    }
    private static string Csv(object? value) {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        var trimmed = text.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]) || text.Length > 0 && "\t\r\n".Contains(text[0])) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
