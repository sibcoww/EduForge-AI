using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduForge.Application;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EduForge.Api;

public record EvidenceView(Guid BlockId, string Quote, string? OriginalText, string? CurrentText, bool Changed);
public record QuestionReview(int Index, string Status, List<string> Reasons, List<EvidenceView> Evidence);
public record ObjectiveCoverage(Guid Id, string Text, int QuestionCount, int ReviewedCount, int Points);
public record QuizAnalysis(LearningContext? Source, List<QuestionReview> Questions, List<ObjectiveCoverage> Coverage,
    int? CoveragePercent, bool Ready, bool MaterialChanged, List<QuestionWarning> QualityWarnings);

public sealed class ControlledLearning(MaterialDbContext db)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static QuestionGrounding? Read(Question question) => string.IsNullOrEmpty(question.Grounding) ? null : JsonSerializer.Deserialize<QuestionGrounding>(question.Grounding, Json);

    public async Task<LearningContext> Context(Material material, CancellationToken ct)
    {
        var blocks = MaterialStructure.Read(material.Id, material.Content);
        var revision = MaterialStructure.Revision(material.Id, blocks);
        if (!await db.MaterialRevisions.AnyAsync(x => x.Id == revision, ct)) {
            var snapshot = new MaterialRevision { Id = revision, MaterialId = material.Id, Blocks = JsonSerializer.Serialize(blocks, Json) };
            db.MaterialRevisions.Add(snapshot);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) {
                db.Entry(snapshot).State = EntityState.Detached;
                if (!await db.MaterialRevisions.AnyAsync(x => x.Id == revision, ct)) throw;
            }
        }
        var plan = await db.LearningPlans.AsNoTracking().SingleOrDefaultAsync(x => x.MaterialId == material.Id, ct);
        return new(material.Id, revision, blocks, plan?.Version ?? Guid.Empty,
            plan is null ? [] : JsonSerializer.Deserialize<List<LearningObjective>>(plan.Objectives, Json)!,
            plan is null || plan.ConfirmedRevision != revision);
    }

    public static string Digest(QuestionInput question, LearningObjective objective) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { question.Type, Text = question.Text.Trim(), question.Points,
            ModelAnswer = question.ModelAnswer.Trim(), Options = question.Options.Select(o => new { Text = o.Text.Trim(), o.IsCorrect }),
            Grounding = question.Grounding! with { Reviewed = false }, Objective = objective.Text }, Json))));

    public static SaveQuiz Draft(Quiz quiz) => new(quiz.Title, quiz.MaterialId,
        quiz.Questions.OrderBy(q => q.Position).Select(q => new QuestionInput(q.Type, q.Text, q.Points, q.ModelAnswer,
            q.Options.OrderBy(o => o.Position).Select(o => new OptionInput(o.Text, o.IsCorrect)).ToList(), Read(q))).ToList(), quiz.Controlled, quiz.GenerationRunId);

    public static string? ValidateGrounding(SaveQuiz draft)
    {
        if (draft.Controlled && draft.MaterialId is null) return "Для контролируемого теста выберите исходный материал.";
        foreach (var question in draft.Questions) {
            var g = question.Grounding;
            if (g is null) continue;
            if (draft.MaterialId is null || g.Explanation is null || g.Explanation.Length > 5000 || g.ObjectiveText is null || g.ObjectiveText.Length > 500 || g.Evidence is null || g.Evidence.Count > 8 ||
                g.Evidence.Any(e => e is null || string.IsNullOrWhiteSpace(e.Quote) || e.Quote.Length > 8000) ||
                g.Evidence.Select(e => e.BlockId).Distinct().Count() != g.Evidence.Count)
                return "Укажите объяснение до 5000 символов и до 8 различных фрагментов (до 8000 символов каждый).";
        }
        return null;
    }

    public async Task<QuizAnalysis> Analyze(SaveQuiz draft, Guid owner, CancellationToken ct)
    {
        var material = draft.MaterialId is null ? null : await db.Materials.AsNoTracking().SingleOrDefaultAsync(m => m.Id == draft.MaterialId && m.TeacherId == owner, ct);
        var source = material is null ? null : await Context(material, ct);
        var ids = draft.Questions.Where(q => q.Grounding is not null).Select(q => q.Grounding!.RevisionId).Distinct().ToArray();
        var snapshots = source is null ? [] : await db.MaterialRevisions.AsNoTracking()
            .Where(r => r.MaterialId == source.MaterialId && ids.Contains(r.Id)).ToListAsync(ct);
        var history = snapshots.ToDictionary(r => r.Id, r => JsonSerializer.Deserialize<List<SourceBlock>>(r.Blocks, Json)!);
        var reviews = new List<QuestionReview>();
        for (var i = 0; i < draft.Questions.Count; i++) {
            var question = draft.Questions[i]; var g = question.Grounding;
            var reasons = new List<string>(); var evidence = new List<EvidenceView>();
            if (source is null) reasons.Add("Исходный материал недоступен.");
            if (g is null) reasons.Add("Нет связи с целью и исходным фрагментом.");
            else {
                if (source?.Objectives.All(o => o.Id != g.ObjectiveId) != false) reasons.Add("Учебная цель не найдена. Выберите актуальную цель.");
                else if (source.Objectives.Single(o => o.Id == g.ObjectiveId).Text != g.ObjectiveText) reasons.Add("Учебная цель изменена. Выберите актуальную цель и проверьте вопрос заново.");
                if (string.IsNullOrWhiteSpace(g.Explanation)) reasons.Add("Добавьте объяснение основания ответа.");
                if (g.Evidence.Count == 0) reasons.Add("Выберите исходный фрагмент.");
                history.TryGetValue(g.RevisionId, out var original);
                if (original is null) reasons.Add("Ревизия основания недоступна или относится к другому материалу.");
                foreach (var e in g.Evidence) {
                    var before = original?.SingleOrDefault(b => b.Id == e.BlockId);
                    var after = source?.Blocks.SingleOrDefault(b => b.Id == e.BlockId);
                    var changed = before is null || after is null || before.Kind != after.Kind || before.Text.Replace("\r\n", "\n") != after.Text.Replace("\r\n", "\n");
                    if (before is null || !MaterialStructure.NormalizeText(before.Text).Contains(MaterialStructure.NormalizeText(e.Quote), StringComparison.Ordinal))
                        reasons.Add("Цитата не найдена в указанном исходном блоке.");
                    if (changed) reasons.Add(after is null ? "Исходный блок удалён. Перепривяжите вопрос." : "Исходный блок изменился. Проверьте вопрос по новой версии.");
                    evidence.Add(new(e.BlockId, e.Quote, before?.Text, after?.Text, changed));
                }
            }
            var status = reasons.Count > 0 ? "NeedsReview" : g!.Reviewed ? "Reviewed" : "Unreviewed";
            reviews.Add(new(i, status, reasons.Distinct().ToList(), evidence));
        }
        var coverage = source?.Objectives.Select(o => {
            var indices = Enumerable.Range(0, draft.Questions.Count).Where(i => draft.Questions[i].Grounding?.ObjectiveId == o.Id).ToList();
            return new ObjectiveCoverage(o.Id, o.Text, indices.Count, indices.Count(i => reviews[i].Status == "Reviewed"), indices.Sum(i => draft.Questions[i].Points));
        }).ToList() ?? [];
        var percent = coverage.Count == 0 ? (int?)null : (int)Math.Round(100d * coverage.Count(c => c.ReviewedCount > 0) / coverage.Count);
        var ready = source is not null && !source.PlanNeedsReview && coverage.Count > 0 && coverage.All(c => c.ReviewedCount > 0) &&
            reviews.Count > 0 && reviews.All(q => q.Status == "Reviewed");
        return new(source, reviews, coverage, percent, ready, source?.PlanNeedsReview ?? false, QuestionQuality.Analyze(draft.Questions));
    }

    public async Task<SaveQuiz> ViewDraft(Quiz quiz, Guid owner, CancellationToken ct)
    {
        var draft = Draft(quiz);
        if (draft.MaterialId is null) return draft;
        var material = await db.Materials.AsNoTracking().SingleOrDefaultAsync(x => x.Id == draft.MaterialId && x.TeacherId == owner, ct);
        if (material is null) return draft with { Questions = draft.Questions.Select(q => q with { Grounding = null }).ToList() };
        var source = await Context(material, ct);
        var stored = quiz.Questions.OrderBy(q => q.Position).ToList();
        return draft with { Questions = draft.Questions.Select((q, i) => {
            var objective = source.Objectives.SingleOrDefault(o => o.Id == q.Grounding?.ObjectiveId);
            return q.Grounding is null ? q : q with { Grounding = q.Grounding with {
                Reviewed = objective is not null && stored[i].ReviewDigest == Digest(q, objective)
            } };
        }).ToList() };
    }

    public async Task<bool> CanUse(Quiz quiz, Guid owner, CancellationToken ct) => !quiz.Controlled ||
        (await Analyze(await ViewDraft(quiz, owner, ct), owner, ct)).Ready;

    public static void StoreReviews(Quiz quiz, SaveQuiz draft, QuizAnalysis analysis)
    {
        var ordered = quiz.Questions.OrderBy(q => q.Position).ToList();
        for (var i = 0; i < ordered.Count; i++) {
            var q = draft.Questions[i];
            var objective = analysis.Source?.Objectives.SingleOrDefault(o => o.Id == q.Grounding?.ObjectiveId);
            ordered[i].Grounding = q.Grounding is null ? "" : JsonSerializer.Serialize(q.Grounding with { Reviewed = false }, Json);
            ordered[i].ReviewDigest = objective is not null && analysis.Questions[i].Status == "Reviewed" ? Digest(q, objective) : "";
        }
    }
}
