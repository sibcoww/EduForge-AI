using System.Security.Claims;
using System.Text.Json;
using EduForge.Application;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EduForge.Api;

public record SaveLearningPlan(Guid RevisionId, Guid PlanVersion, List<LearningObjective> Objectives);
public record SuggestLearningPlan(Guid RevisionId, Guid PlanVersion);
public static class ControlledLearningEndpoints
{
    private static Guid Owner(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public static void MapControlledLearning(this WebApplication app)
    {
        app.MapGet("/api/materials/{id:guid}/teaching-context", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var material = await db.Materials.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id && m.TeacherId == Owner(user), ct);
            return material is null ? Results.NotFound() : Results.Ok(await MaterialTeachingSettings.Read(material, db, ct));
        }).RequireAuthorization();

        app.MapPost("/api/materials/{id:guid}/learning/suggest", async (Guid id, SuggestLearningPlan input, ClaimsPrincipal user,
            MaterialDbContext db, ControlledLearning learning, ILearningObjectivesAiService ai, GenerationRecorder recorder, CancellationToken ct) => {
            var material = await db.Materials.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id && m.TeacherId == Owner(user), ct);
            if (material is null) return Results.NotFound();
            var source = await learning.Context(material, ct);
            if (input.RevisionId != source.RevisionId || input.PlanVersion != source.PlanVersion)
                return Results.Conflict(new { message = "Материал или цели изменились. Обновите панель перед предложением целей." });
            if (source.Blocks.Count == 0 || source.Blocks.Sum(b => b.Text.Length) > 60000)
                return Results.BadRequest(new { message = "Для предложения целей нужен непустой материал до 60 000 символов." });
            var context = await MaterialTeachingSettings.Read(material, db, ct);
            try {
                var (run, objectives) = await recorder.Record(Owner(user), "LearningObjectives", "lesson-objectives-v1", new { material.Title, Context = context }, source, ai, async () => {
                    var result = await ai.SuggestObjectivesAsync(material.Title, source.Blocks, context, ct);
                    if (result is null || result.Count is < 1 or > 8 || result.Any(g => !TeachingValidation.Text(g, 500)) || result.Select(g => g.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
                        throw new AiUnavailableException("AI предложил некорректные цели. Повторите запрос или добавьте их вручную.");
                    var current = await db.Materials.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id && m.TeacherId == Owner(user), ct);
                    if (current is null || current.UpdatedAt != material.UpdatedAt)
                        throw new GenerationSourceChangedException("Материал изменился во время генерации. Обновите панель и повторите запрос.");
                    var latest = await learning.Context(current, ct);
                    if (latest.RevisionId != source.RevisionId || latest.PlanVersion != source.PlanVersion)
                        throw new GenerationSourceChangedException("Цели изменились в другом окне. Обновите панель перед повторным запросом.");
                    return result.Select(text => new LearningObjective(Guid.NewGuid(), text.Trim())).ToList();
                }, ct);
                return Results.Ok(new { source.RevisionId, source.PlanVersion, Objectives = objectives, GenerationRunId = run.Id });
            }
            catch (GenerationSourceChangedException ex) { return Results.Conflict(new { message = ex.Message }); }
            catch (AiUnavailableException ex) { return Results.Problem(ex.Message, statusCode: 503); }
            catch (HttpRequestException) { return Results.Problem("Не удалось связаться с AI-провайдером.", statusCode: 503); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Results.Problem("AI не ответил вовремя. Ваши текущие цели сохранены в редакторе.", statusCode: 504); }
        }).RequireAuthorization().RequireRateLimiting("ai");

        app.MapGet("/api/materials/{id:guid}/learning", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, ControlledLearning learning, CancellationToken ct) => {
            var material = await db.Materials.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.TeacherId == Owner(user), ct);
            return material is null ? Results.NotFound() : Results.Ok(await learning.Context(material, ct));
        }).RequireAuthorization();

        app.MapPut("/api/materials/{id:guid}/learning", async (Guid id, SaveLearningPlan input, ClaimsPrincipal user, MaterialDbContext db, ControlledLearning learning, CancellationToken ct) => {
            var material = await db.Materials.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.TeacherId == Owner(user), ct);
            if (material is null) return Results.NotFound();
            if (input.Objectives is null || input.Objectives.Count is < 1 or > 30 || input.Objectives.Any(o => o is null || o.Id == Guid.Empty || !TeachingValidation.Text(o.Text, 500)) ||
                input.Objectives.Select(o => o.Id).Distinct().Count() != input.Objectives.Count ||
                input.Objectives.Select(o => o.Text.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Objectives.Count)
                return Results.BadRequest(new { message = "Добавьте от 1 до 30 разных учебных целей, до 500 символов каждая." });
            var context = await learning.Context(material, ct);
            if (input.RevisionId != context.RevisionId || input.PlanVersion != context.PlanVersion) return Results.Conflict(new { message = "Материал или цели изменились. Обновите панель и повторите проверку." });
            var plan = await db.LearningPlans.SingleOrDefaultAsync(x => x.MaterialId == id, ct);
            if (plan is null) { plan = new() { MaterialId = id }; db.LearningPlans.Add(plan); }
            plan.ConfirmedRevision = context.RevisionId; plan.Version = Guid.NewGuid();
            plan.Objectives = JsonSerializer.Serialize(input.Objectives.Select(o => o with { Text = o.Text.Trim() }), ControlledLearning.Json);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Results.Conflict(new { message = "Цели изменились в другом окне. Обновите панель." }); }
            return Results.Ok(await learning.Context(material, ct));
        }).RequireAuthorization();

        app.MapPost("/api/tests/analyze", async (SaveQuiz draft, ClaimsPrincipal user, MaterialDbContext db, ControlledLearning learning, CancellationToken ct) => {
            if (draft.Questions is null || draft.Questions.Count > 100 || draft.Questions.Any(q => q is null || q.Text is null || q.Text.Length > 4000 || q.Options is null || q.Options.Count > 10 || q.Options.Any(o => o is null || o.Text is null || o.Text.Length > 1000)) || ControlledLearning.ValidateGrounding(draft) is not null)
                return Results.BadRequest(new { message = "Некорректный черновик теста." });
            if (draft.MaterialId is { } id && !await db.Materials.AnyAsync(m => m.Id == id && m.TeacherId == Owner(user), ct)) return Results.NotFound();
            return Results.Ok(await learning.Analyze(draft, Owner(user), ct));
        }).RequireAuthorization();

        app.MapPost("/api/ai/test/controlled", async (ControlledQuizRequest input, ClaimsPrincipal user, MaterialDbContext db, ControlledLearning learning, IControlledAiService ai, GenerationRecorder recorder, CancellationToken ct) => {
            var request = input.Quiz;
            if (request is null) return Results.BadRequest();
            var material = await db.Materials.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.MaterialId && x.TeacherId == Owner(user), ct);
            if (material is null) return Results.NotFound();
            if (request.Requirements is null || request.Requirements.Length > 2000 || request.QuestionCount is < 1 or > 30 || request.TotalPoints is int total && (total < request.QuestionCount || total > request.QuestionCount * 100) ||
                request.Difficulty is not ("Лёгкая" or "Средняя" or "Сложная") || request.Types is null || request.Types.Count is < 1 or > 5 || request.Types.Any(t => !Enum.IsDefined(t)) || request.Types.Distinct().Count() != request.Types.Count)
                return Results.BadRequest(new { message = "Проверьте количество, сложность, типы вопросов и общий балл." });
            var context = await learning.Context(material, ct);
            if (context.RevisionId != input.RevisionId || context.PlanVersion != input.PlanVersion || context.PlanNeedsReview)
                return Results.Conflict(new { message = "Обновите материал и подтвердите учебные цели перед генерацией." });
            if (context.Blocks.Count == 0 || context.Blocks.Sum(b => b.Text.Length) > 60000)
                return Results.BadRequest(new { message = "Нужен непустой материал до 60 000 символов." });
            request = request with { Context = await MaterialTeachingSettings.Read(material, db, ct) };
            if (input.ObjectiveIds is { } ids) {
                if (ids.Count == 0 || ids.Distinct().Count() != ids.Count || ids.Any(id => context.Objectives.All(o => o.Id != id))) return Results.BadRequest();
                context = context with { Objectives = context.Objectives.Where(o => ids.Contains(o.Id)).ToList() };
            }
            try {
                var (run, output) = await recorder.Record<SaveQuiz>(Owner(user), "ControlledTest", "controlled-test-v3", request, context, ai, async () => {
                var result = await ai.GenerateControlledQuizAsync(material.Title, context, request, ct);
                if (TeachingValidation.Quiz(result) is not null || ControlledLearning.ValidateGrounding(result) is not null ||
                    result.Questions.Count != request.QuestionCount || result.Questions.Any(q => !request.Types.Contains(q.Type) || q.Grounding is null || q.Grounding.RevisionId != context.RevisionId || context.Objectives.All(o => o.Id != q.Grounding.ObjectiveId)))
                    throw new AiUnavailableException("AI вернул вопросы с некорректными связями. Повторите генерацию.");
                var fresh = await db.Materials.AsNoTracking().SingleOrDefaultAsync(x => x.Id == material.Id && x.TeacherId == Owner(user), ct);
                if (fresh is null) throw new GenerationSourceChangedException("Материал удалён во время генерации.");
                var latest = await learning.Context(fresh, ct);
                if (latest.RevisionId != context.RevisionId || latest.PlanVersion != context.PlanVersion)
                    throw new GenerationSourceChangedException("Материал или цели изменились во время генерации. Обновите панель.");
                result = result with { MaterialId = material.Id, Controlled = true, Questions = result.Questions.Select(q => q with { Grounding = q.Grounding! with { Reviewed = false,
                    ObjectiveText = context.Objectives.Single(o => o.Id == q.Grounding!.ObjectiveId).Text } }).ToList() };
                var analysis = await learning.Analyze(result, Owner(user), ct);
                if (analysis.Questions.Any(q => q.Status == "NeedsReview")) throw new AiUnavailableException("AI сослался на неподтверждённый фрагмент. Повторите запрос или добавьте основание вручную.");
                return result;
                }, ct);
                return Results.Ok(output with { GenerationRunId = run.Id });
            }
            catch (GenerationSourceChangedException ex) { return Results.Conflict(new { message = ex.Message }); }
            catch (AiUnavailableException ex) { return Results.Problem(ex.Message, statusCode: 503); }
            catch (HttpRequestException) { return Results.Problem("Не удалось связаться с AI-провайдером.", statusCode: 503); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Results.Problem("AI не ответил вовремя.", statusCode: 504); }
        }).RequireAuthorization().RequireRateLimiting("ai");
    }
}
