using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EduForge.Application;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EduForge.Tests;

public sealed class ControlledLearningTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static async Task Login(HttpClient client, string email = "controlled@example.com") {
        var r = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Correct horse battery staple!", name = "Teacher" });
        r.EnsureSuccessStatusCode(); client.DefaultRequestHeaders.Add("Cookie", r.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
    }
    private static string Content(string a = "for перебирает элементы списка", string b = "range(3) создаёт числа 0, 1, 2", Guid? first = null, Guid? second = null, bool bold = false) =>
        JsonSerializer.Serialize(new { type = "doc", content = new[] {
            new { type = "paragraph", attrs = new { blockId = first?.ToString() }, content = new[] { new { type = "text", text = a, marks = bold ? new object[] { new { type = "bold" } } : Array.Empty<object>() } } },
            new { type = "paragraph", attrs = new { blockId = second?.ToString() }, content = new[] { new { type = "text", text = b, marks = Array.Empty<object>() } } }
        } });
    private static async Task<LearningContext> Source(HttpClient client, string? content = null) {
        var r = await client.PostAsJsonAsync("/api/materials", new { title = "Циклы", subject = "Python", content = content ?? Content() });
        r.EnsureSuccessStatusCode(); var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        return (await client.GetFromJsonAsync<LearningContext>($"/api/materials/{id}/learning", Json))!;
    }
    private static async Task<LearningContext> Confirm(HttpClient client, LearningContext source, List<LearningObjective>? goals = null) {
        var r = await client.PutAsJsonAsync($"/api/materials/{source.MaterialId}/learning", new { source.RevisionId, source.PlanVersion,
            Objectives = goals ?? new List<LearningObjective> { new(Guid.NewGuid(), "Объяснить for"), new(Guid.NewGuid(), "Применить range") } });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<LearningContext>(Json))!;
    }
    private static QuestionInput Question(LearningContext source, int index, bool reviewed = true) => new(QuestionType.SingleChoice,
        "Что означает " + index, 3, "Верный ответ", [new("Верно", true), new("Неверно", false)],
        new(source.RevisionId, source.Objectives[index].Id, [new(source.Blocks[index].Id, source.Blocks[index].Text)], "Ответ следует из приведённого объяснения", reviewed, source.Objectives[index].Text));
    private static SaveQuiz Draft(LearningContext source) => new("Проверка циклов", source.MaterialId, [Question(source, 0), Question(source, 1)], true);
    private static async Task<JsonElement> Save(HttpClient client, SaveQuiz draft, Guid? id = null) {
        var response = id is null ? await client.PostAsJsonAsync("/api/tests", draft, Json) : await client.PutAsJsonAsync($"/api/tests/{id}", draft, Json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task<JsonElement> Analyze(HttpClient client, SaveQuiz draft) {
        var r = await client.PostAsJsonAsync("/api/tests/analyze", draft, Json); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task<Guid> Group(HttpClient client) {
        var response = await client.PostAsJsonAsync("/api/groups", new { name = "Группа", description = "" });
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await client.PostAsJsonAsync($"/api/groups/{id}/students", new { firstName = "Алия", lastName = "А" })).EnsureSuccessStatusCode(); return id;
    }

    [Fact]
    public async Task ReviewedGroundingRoundTripsAndEveryObjectiveIsCountedOnce() {
        using var factory = new ApiFactory(); using var client = factory.Browser(); await Login(client);
        var source = await Confirm(client, await Source(client)); var draft = Draft(source);
        var saved = await Save(client, draft); var id = saved.GetProperty("id").GetGuid();
        var loaded = (await client.GetFromJsonAsync<SaveQuiz>($"/api/tests/{id}", Json))!;
        Assert.All(loaded.Questions, q => Assert.True(q.Grounding!.Reviewed));
        var report = await Analyze(client, loaded);
        Assert.True(report.GetProperty("ready").GetBoolean()); Assert.Equal(100, report.GetProperty("coveragePercent").GetInt32());
        Assert.All(report.GetProperty("coverage").EnumerateArray(), goal => { Assert.Equal(1, goal.GetProperty("reviewedCount").GetInt32()); Assert.Equal(3, goal.GetProperty("points").GetInt32()); });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/tests/{id}/publication", null)).StatusCode);
    }

    [Fact]
    public async Task ChangedAndDeletedBlocksRequireReviewButOldSessionsKeepTheirSnapshot() {
        using var factory = new ApiFactory(); using var client = factory.Browser(); await Login(client);
        var source = await Confirm(client, await Source(client)); var saved = await Save(client, Draft(source)); var id = saved.GetProperty("id").GetGuid();
        var groupId = await Group(client);
        var sessionResponse = await client.PostAsJsonAsync("/api/sessions", new { quizId = id, groupId, durationMinutes = 10 });
        Assert.True(sessionResponse.IsSuccessStatusCode, await sessionResponse.Content.ReadAsStringAsync());
        var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        var changed = Content("for перебирает элементы кортежа", first: source.Blocks[0].Id, second: source.Blocks[1].Id);
        (await client.PutAsJsonAsync($"/api/materials/{source.MaterialId}", new { title = "Циклы", subject = "Python", content = changed })).EnsureSuccessStatusCode();
        var loaded = (await client.GetFromJsonAsync<SaveQuiz>($"/api/tests/{id}", Json))!;
        var report = await Analyze(client, loaded);
        Assert.Equal("NeedsReview", report.GetProperty("questions")[0].GetProperty("status").GetString());
        Assert.Equal("Reviewed", report.GetProperty("questions")[1].GetProperty("status").GetString());
        Assert.False(report.GetProperty("ready").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/tests/{id}/publication", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/sessions", new { quizId = id, groupId, durationMinutes = 10 })).StatusCode);
        var preserved = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/{session.GetProperty("id").GetGuid()}");
        Assert.Equal(session.GetProperty("questions").GetRawText(), preserved.GetProperty("questions").GetRawText());
        var oneBlock = JsonSerializer.Serialize(new { type = "doc", content = new[] { new { type = "paragraph", attrs = new { blockId = source.Blocks[1].Id }, content = new[] { new { type = "text", text = source.Blocks[1].Text } } } } });
        (await client.PutAsJsonAsync($"/api/materials/{source.MaterialId}", new { title = "Циклы", subject = "Python", content = oneBlock })).EnsureSuccessStatusCode();
        report = await Analyze(client, loaded);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("questions")[0].GetProperty("evidence")[0].GetProperty("currentText").ValueKind);
    }

    [Fact]
    public async Task FormattingDoesNotInvalidateEvidenceAndGoalChangesInvalidateReview() {
        using var factory = new ApiFactory(); using var client = factory.Browser(); await Login(client);
        var source = await Confirm(client, await Source(client)); var saved = await Save(client, Draft(source)); var id = saved.GetProperty("id").GetGuid();
        (await client.PutAsJsonAsync($"/api/materials/{source.MaterialId}", new { title = "Циклы", subject = "Python", content = Content(first: source.Blocks[0].Id, second: source.Blocks[1].Id, bold: true) })).EnsureSuccessStatusCode();
        var fresh = (await client.GetFromJsonAsync<LearningContext>($"/api/materials/{source.MaterialId}/learning", Json))!;
        Assert.Equal(source.RevisionId, fresh.RevisionId);
        var goals = fresh.Objectives.Select((o, i) => i == 0 ? o with { Text = "Применять for" } : o).ToList();
        await Confirm(client, fresh, goals);
        var loaded = (await client.GetFromJsonAsync<SaveQuiz>($"/api/tests/{id}", Json))!;
        Assert.False(loaded.Questions[0].Grounding!.Reviewed); Assert.True(loaded.Questions[1].Grounding!.Reviewed);
    }

    [Fact]
    public async Task MissingCoverageAndFabricatedQuotesCannotBeUsedEvenIfMarkedReviewed() {
        using var factory = new ApiFactory(); using var client = factory.Browser(); await Login(client);
        var source = await Confirm(client, await Source(client)); var draft = Draft(source);
        var missing = draft with { Questions = [Question(source, 0)] };
        Assert.False((await Analyze(client, missing)).GetProperty("ready").GetBoolean());
        var bad = Question(source, 0) with { Grounding = Question(source, 0).Grounding! with { Evidence = [new(source.Blocks[0].Id, "Этого нет в материале")] } };
        var saved = await Save(client, draft with { Questions = [bad, Question(source, 1)] });
        Assert.False(saved.GetProperty("questions")[0].GetProperty("grounding").GetProperty("reviewed").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/tests/{saved.GetProperty("id").GetGuid()}/publication", null)).StatusCode);
    }

    [Fact]
    public async Task ForeignMaterialsAndRevisionsDoNotRevealSourceText() {
        using var factory = new ApiFactory(); using var alice = factory.Browser(); using var bob = factory.Browser();
        await Login(alice, "alice-controlled@example.com"); await Login(bob, "bob-controlled@example.com");
        var source = await Confirm(alice, await Source(alice, Content("Приватный текст преподавателя")));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/materials/{source.MaterialId}/learning")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync("/api/tests/analyze", Draft(source), Json)).StatusCode);
        var own = await Confirm(bob, await Source(bob));
        var forged = Question(source, 0) with { Grounding = Question(source, 0).Grounding! with { Evidence = [new(source.Blocks[0].Id, "Запрошенная цитата")] } };
        var attack = new SaveQuiz("Тест", own.MaterialId, [forged], true);
        var report = await Analyze(bob, attack);
        Assert.DoesNotContain("Приватный текст", report.GetRawText());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("questions")[0].GetProperty("evidence")[0].GetProperty("originalText").ValueKind);
    }

    [Fact]
    public async Task LearningPlansRejectConcurrentUpdatesAndNewGoalsNeedCoverage() {
        using var factory = new ApiFactory(); using var client = factory.Browser(); await Login(client);
        var source = await Confirm(client, await Source(client));
        var newest = await Confirm(client, source);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/materials/{source.MaterialId}/learning", new { source.RevisionId, source.PlanVersion, source.Objectives })).StatusCode);
        var report = await Analyze(client, Draft(source));
        Assert.False(report.GetProperty("ready").GetBoolean());
        Assert.All(report.GetProperty("coverage").EnumerateArray(), o => Assert.Equal(0, o.GetProperty("reviewedCount").GetInt32()));
    }

    private sealed class Ai(bool forge = false) : IControlledAiService {
        public int Calls { get; private set; }
        public Task<SaveQuiz> GenerateControlledQuizAsync(string title, LearningContext source, GenerateQuiz input, CancellationToken ct) {
            Calls++;
            var q = Question(source, 0) with { Points = input.TotalPoints ?? 3 };
            if (forge) q = q with { Grounding = q.Grounding! with { Evidence = [new(source.Blocks[0].Id, "Выдуманная цитата")] } };
            return Task.FromResult(new SaveQuiz(title, source.MaterialId, [q], true));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ControlledGenerationUsesSavedGoalsValidatesEvidenceAndNeverSavesDraft(bool forge) {
        var ai = new Ai(forge); using var factory = new ApiFactory { ControlledAi = ai }; using var client = factory.Browser(); await Login(client);
        var source = await Source(client);
        var input = new ControlledQuizRequest(new(source.MaterialId, 1, "Средняя", [QuestionType.SingleChoice], 3), source.RevisionId, source.PlanVersion);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/ai/test/controlled", input, Json)).StatusCode);
        Assert.Equal(0, ai.Calls);
        source = await Confirm(client, source);
        var response = await client.PostAsJsonAsync("/api/ai/test/controlled", input with { PlanVersion = source.PlanVersion }, Json);
        Assert.Equal(forge ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK, response.StatusCode);
        if (!forge) Assert.False((await response.Content.ReadFromJsonAsync<SaveQuiz>(Json))!.Questions[0].Grounding!.Reviewed);
        Assert.Equal(1, ai.Calls); Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>("/api/tests"))!);
    }

    [Fact]
    public void NormalizationKeepsBlockIdentityAcrossEditingAndRejectsDuplicateIds() {
        var materialId = Guid.NewGuid(); var original = MaterialStructure.Normalize(materialId, Content(), "");
        var blocks = MaterialStructure.Read(materialId, original);
        var edited = MaterialStructure.Normalize(materialId, Content("Исправленный for", first: blocks[0].Id, second: blocks[1].Id), original);
        Assert.Equal(blocks.Select(b => b.Id), MaterialStructure.Read(materialId, edited).Select(b => b.Id));
        var duplicate = MaterialStructure.Normalize(materialId, Content(first: blocks[0].Id, second: blocks[0].Id), original);
        Assert.Equal(2, MaterialStructure.Read(materialId, duplicate).Select(b => b.Id).Distinct().Count());
    }

    [Fact]
    public void PythonIndentationChangesRevisionEvenWhenWhitespaceNormalizedTextMatches() {
        var materialId = Guid.NewGuid(); var blockId = Guid.NewGuid();
        var original = new List<SourceBlock> { new(blockId, "codeBlock", "for i in range(3):\n    print(i)\nprint('end')") };
        var changed = new List<SourceBlock> { new(blockId, "codeBlock", "for i in range(3):\n    print(i)\n    print('end')") };
        Assert.Equal(MaterialStructure.NormalizeText(original[0].Text), MaterialStructure.NormalizeText(changed[0].Text));
        Assert.NotEqual(MaterialStructure.Revision(materialId, original), MaterialStructure.Revision(materialId, changed));
    }
}
