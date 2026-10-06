using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EduForge.Tests;
public sealed class SessionTests
{
    sealed class Clock : TimeProvider {
        public DateTimeOffset Current = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Current;
    }
    static async Task Login(HttpClient client, string email = "sessions@example.test") {
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Correct horse battery staple!", name = "Teacher" });
        response.EnsureSuccessStatusCode(); client.DefaultRequestHeaders.Add("Cookie", response.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
    }
    static async Task<JsonElement> Json(HttpResponseMessage response) { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    static async Task<(JsonElement Session, string Group, string Quiz)> Create(HttpClient teacher) {
        var quiz = await Json(await teacher.PostAsJsonAsync("/api/tests", new { title = "Снимок теста", questions = new object[] {
            new { type = "MultipleChoice", text = "Выберите две", points = 3, modelAnswer = "", options = new[] { new { text = "A", isCorrect = true }, new { text = "B", isCorrect = true }, new { text = "C", isCorrect = false } } },
            new { type = "OpenText", text = "Объясните", points = 5, modelAnswer = "Образец", options = Array.Empty<object>() }
        } }));
        var group = await Json(await teacher.PostAsJsonAsync("/api/groups", new { name = "Группа", description = "" })); var groupId = group.GetProperty("id").GetString()!;
        await Json(await teacher.PostAsJsonAsync($"/api/groups/{groupId}/students", new { firstName = "Алина", lastName = "Ахметова" }));
        await Json(await teacher.PostAsJsonAsync($"/api/groups/{groupId}/students", new { firstName = "Данияр", lastName = "Айтбаев" }));
        var session = await Json(await teacher.PostAsJsonAsync("/api/sessions", new { quizId = quiz.GetProperty("id"), groupId, durationMinutes = 1 }));
        return (session, groupId, quiz.GetProperty("id").GetString()!);
    }
    static async Task<string> Join(HttpClient student, JsonElement session, int index = 0) {
        var joined = await Json(await student.PostAsJsonAsync($"/api/join/{session.GetProperty("code").GetString()}", new { studentId = session.GetProperty("participants")[index].GetProperty("studentId") }));
        var token = joined.GetProperty("token").GetString()!; student.DefaultRequestHeaders.Add("X-Attempt-Token", token); return token;
    }
    [Fact]
    public async Task WaitingStartAutosaveSubmissionGradingAndManualReview() {
        using var factory = new ApiFactory(); using var teacher = factory.Browser(); using var student = factory.Browser(); await Login(teacher);
        var (session, _, _) = await Create(teacher); var id = session.GetProperty("id").GetString(); var code = session.GetProperty("code").GetString();
        await Join(student, session);
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsJsonAsync($"/api/join/{code}", new { studentId = session.GetProperty("participants")[0].GetProperty("studentId") })).StatusCode);
        var waiting = await student.GetFromJsonAsync<JsonElement>($"/api/attempts/{id}"); Assert.Equal(JsonValueKind.Null, waiting.GetProperty("questions").ValueKind);
        var q = session.GetProperty("questions")[0]; var qid = q.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await student.PutAsJsonAsync($"/api/attempts/{id}/answers/{qid}", new { optionIds = new[] { q.GetProperty("options")[0].GetProperty("id") }, text = "" })).StatusCode);
        await Json(await teacher.PostAsync($"/api/sessions/{id}/start", null));
        Assert.Equal(HttpStatusCode.NoContent, (await student.PostAsJsonAsync($"/api/attempts/{id}/activity", new { kind = "Hidden" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.PostAsJsonAsync($"/api/attempts/{id}/activity", new { kind = "ForgedScore" })).StatusCode);
        var activeResponse = await student.GetAsync($"/api/attempts/{id}"); var activeText = await activeResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("isCorrect", activeText); Assert.DoesNotContain("modelAnswer", activeText); Assert.DoesNotContain("tokenHash", activeText);
        var selected = new[] { q.GetProperty("options")[0].GetProperty("id"), q.GetProperty("options")[1].GetProperty("id") };
        Assert.Equal(HttpStatusCode.NoContent, (await student.PutAsJsonAsync($"/api/attempts/{id}/answers/{qid}", new { optionIds = selected, text = "" })).StatusCode);
        var openId = session.GetProperty("questions")[1].GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await student.PutAsJsonAsync($"/api/attempts/{id}/answers/{openId}", new { optionIds = Array.Empty<Guid>(), text = "Мой ответ" })).StatusCode);
        var resumed = await student.GetFromJsonAsync<JsonElement>($"/api/attempts/{id}"); Assert.Equal(2, resumed.GetProperty("answers").GetArrayLength());
        var submitted = await Json(await student.PostAsync($"/api/attempts/{id}/submit", null)); Assert.Equal(3, submitted.GetProperty("score").GetInt32()); Assert.Equal(1, submitted.GetProperty("pendingReview").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await student.PutAsJsonAsync($"/api/attempts/{id}/answers/{qid}", new { optionIds = Array.Empty<Guid>(), text = "" })).StatusCode);
        await Json(await teacher.PostAsync($"/api/sessions/{id}/finish", null));
        var reviewed = await Json(await teacher.PutAsJsonAsync($"/api/sessions/{id}/review/{session.GetProperty("participants")[0].GetProperty("studentId").GetString()}/{openId}", new { score = 4, feedback = "Хорошо" }));
        Assert.Equal("Hidden", reviewed.GetProperty("participants")[0].GetProperty("activity")[0].GetProperty("kind").GetString());
        Assert.Equal(7, reviewed.GetProperty("participants")[0].GetProperty("score").GetInt32()); Assert.Equal(0, reviewed.GetProperty("participants")[0].GetProperty("pendingReview").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PutAsJsonAsync($"/api/sessions/{id}/review/{session.GetProperty("participants")[0].GetProperty("studentId").GetString()}/{openId}", new { score = 6, feedback = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.GetAsync($"/api/join/{code}")).StatusCode);
    }
    [Fact]
    public async Task DeadlineFinishesSavedAnswersAndRejectsLateChangesWithoutStudentPolling() {
        var clock = new Clock(); using var factory = new ApiFactory { Clock = clock }; using var teacher = factory.Browser(); using var student = factory.Browser(); await Login(teacher);
        var (session, _, _) = await Create(teacher); var id = session.GetProperty("id").GetString(); await Join(student, session);
        await Json(await teacher.PostAsync($"/api/sessions/{id}/start", null));
        var q = session.GetProperty("questions")[0]; var qid = q.GetProperty("id").GetString();
        await student.PutAsJsonAsync($"/api/attempts/{id}/answers/{qid}", new { optionIds = new[] { q.GetProperty("options")[0].GetProperty("id") }, text = "" });
        clock.Current = clock.Current.AddMinutes(2);
        // Check storage directly: the background worker must finish without an HTTP read.
        var finished = false;
        for (var i = 0; i < 30 && !finished; i++) { await Task.Delay(100); using var scope = factory.Services.CreateScope(); finished = await scope.ServiceProvider.GetRequiredService<MaterialDbContext>().Sessions.AnyAsync(s => s.Status == SessionStatus.Finished); }
        Assert.True(finished);
        Assert.Equal(HttpStatusCode.Conflict, (await student.PutAsJsonAsync($"/api/attempts/{id}/answers/{qid}", new { optionIds = new[] { q.GetProperty("options")[0].GetProperty("id"), q.GetProperty("options")[1].GetProperty("id") }, text = "" })).StatusCode);
        var result = await teacher.GetFromJsonAsync<JsonElement>($"/api/sessions/{id}"); Assert.Equal("Finished", result.GetProperty("status").GetString());
        Assert.Equal(0, result.GetProperty("participants")[0].GetProperty("score").GetInt32());
        Assert.Equal(result.GetProperty("deadline").GetInt64(), result.GetProperty("participants")[0].GetProperty("submittedAt").GetInt64());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("participants")[1].GetProperty("submittedAt").ValueKind);
    }
    [Fact]
    public async Task OwnershipTokensAndSnapshotsSurviveSourceDeletion() {
        using var factory = new ApiFactory(); using var alice = factory.Browser(); using var bob = factory.Browser(); using var student = factory.Browser(); await Login(alice); await Login(bob, "bob@example.test");
        var (session, group, quiz) = await Create(alice); var id = session.GetProperty("id").GetString();
        Assert.Empty((await bob.GetFromJsonAsync<JsonElement[]>("/api/sessions"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/sessions/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/sessions/{id}/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync("/api/sessions", new { quizId = quiz, groupId = group, durationMinutes = 15 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await student.GetAsync($"/api/attempts/{id}")).StatusCode);
        await Join(student, session);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/tests/{quiz}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/groups/{group}")).StatusCode);
        await Json(await alice.PostAsync($"/api/sessions/{id}/start", null));
        var active = await student.GetFromJsonAsync<JsonElement>($"/api/attempts/{id}"); Assert.Equal("Выберите две", active.GetProperty("questions")[0].GetProperty("text").GetString());
        var qid = active.GetProperty("questions")[0].GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.BadRequest, (await student.PutAsJsonAsync($"/api/attempts/{id}/answers/{qid}", new { optionIds = new[] { Guid.NewGuid() }, text = "" })).StatusCode);
        await Json(await alice.PostAsync($"/api/sessions/{id}/finish", null));
        var result = await alice.GetFromJsonAsync<JsonElement>($"/api/sessions/{id}"); Assert.Equal("Айтбаев Данияр", result.GetProperty("participants")[0].GetProperty("name").GetString());
        using var other = factory.Browser(); other.DefaultRequestHeaders.Add("X-Attempt-Token", new string('F', 64));
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.PostAsync($"/api/attempts/{id}/submit", null)).StatusCode);
    }
    [Fact]
    public async Task SessionRevisionPreventsStaleAnswerWriteOverwritingFinish() {
        using var factory = new ApiFactory(); using var teacher = factory.Browser(); await Login(teacher); var (session, _, _) = await Create(teacher); var id = session.GetProperty("id").GetGuid();
        using var firstScope = factory.Services.CreateScope(); using var secondScope = factory.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<MaterialDbContext>(); var second = secondScope.ServiceProvider.GetRequiredService<MaterialDbContext>();
        var stale = await first.Sessions.SingleAsync(s => s.Id == id); var finished = await second.Sessions.SingleAsync(s => s.Id == id);
        finished.Status = SessionStatus.Finished; finished.Revision = Guid.NewGuid(); await second.SaveChangesAsync();
        stale.Payload = "stale answers"; stale.Revision = Guid.NewGuid(); await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => first.SaveChangesAsync());
    }
}
