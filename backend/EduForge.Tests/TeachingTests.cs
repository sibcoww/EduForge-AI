using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduForge.Application;
using EduForge.Domain;
using Xunit;

namespace EduForge.Tests;
public sealed class TeachingTests
{
    static async Task SignIn(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Correct horse battery staple!", name = "Teacher" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", response.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
    }
    static object Choice(string text = "Вопрос", string type = "SingleChoice") => new { type, text, points = 2, modelAnswer = "", options = new[] { new { text = "А", isCorrect = true }, new { text = "Б", isCorrect = false } } };
    static async Task<string> Material(HttpClient client, string content = "for перебирает элементы")
    {
        var response = await client.PostAsJsonAsync("/api/materials", new { title = "Цикл for", subject = "Python", content });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }
    [Fact]
    public async Task AllQuestionTypesRoundTripAndCanBeReorderedAndEdited()
    {
        using var factory = new ApiFactory(); using var client = factory.Browser(); await SignIn(client, "quiz@example.com");
        var materialId = await Material(client);
        var questions = new object[] {
            Choice("Одиночный"), Choice("Множественный", "MultipleChoice"),
            new { type = "TrueFalse", text = "Утверждение", points = 1, modelAnswer = "", options = new[] { new { text = "Да", isCorrect = false }, new { text = "Нет", isCorrect = true } } },
            new { type = "OpenText", text = "Объясните", points = 5, modelAnswer = "Пример", options = Array.Empty<object>() },
            new { type = "ShortText", text = "Назовите", points = 1, modelAnswer = "for", options = Array.Empty<object>() }
        };
        var response = await client.PostAsJsonAsync("/api/tests", new { title = "Тест", materialId, questions });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>(); var id = saved.GetProperty("id").GetString();
        var detailResponse = await client.GetAsync($"/api/tests/{id}");
        Assert.True(detailResponse.IsSuccessStatusCode, await detailResponse.Content.ReadAsStringAsync());
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, detail.GetProperty("questions").GetArrayLength());
        Assert.Equal("SingleChoice", detail.GetProperty("questions")[0].GetProperty("type").GetString());
        Assert.True(detail.GetProperty("questions")[0].GetProperty("options")[0].GetProperty("isCorrect").GetBoolean());
        Assert.Equal("for", detail.GetProperty("questions")[4].GetProperty("modelAnswer").GetString());
        var update = await client.PutAsJsonAsync($"/api/tests/{id}", new { title = "Изменён", materialId, questions = new[] { Choice("Второй теперь первый"), Choice("Первый теперь второй") } });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        detail = await client.GetFromJsonAsync<JsonElement>($"/api/tests/{id}");
        Assert.Equal(2, detail.GetProperty("questions").GetArrayLength());
        Assert.Equal("Второй теперь первый", detail.GetProperty("questions")[0].GetProperty("text").GetString());
        var list = await client.GetFromJsonAsync<JsonElement[]>($"/api/tests?materialId={materialId}");
        Assert.Single(list!); Assert.Equal(4, list![0].GetProperty("totalPoints").GetInt32());
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>($"/api/tests?materialId={Guid.NewGuid()}"))!);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/tests/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tests/{id}")).StatusCode);
    }
    [Fact]
    public async Task ForeignTestsGroupsStudentsAndSourceMaterialsAreInaccessible()
    {
        using var factory = new ApiFactory(); using var alice = factory.Browser(); using var bob = factory.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.GetAsync("/api/tests")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.GetAsync("/api/groups")).StatusCode);
        await SignIn(alice, "alice-teaching@example.com"); await SignIn(bob, "bob-teaching@example.com");
        var materialId = await Material(alice);
        var testResponse = await alice.PostAsJsonAsync("/api/tests", new { title = "Тест", materialId, questions = new[] { Choice() } });
        var testId = (await testResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        Assert.Empty((await bob.GetFromJsonAsync<JsonElement[]>("/api/tests"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/tests/{testId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"/api/tests/{testId}", new { title = "X", materialId, questions = new[] { Choice() } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/tests/{testId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync("/api/tests", new { title = "X", materialId, questions = new[] { Choice() } })).StatusCode);
        var groupResponse = await alice.PostAsJsonAsync("/api/groups", new { name = "Python-24", description = "" });
        var groupId = (await groupResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        var studentResponse = await alice.PostAsJsonAsync($"/api/groups/{groupId}/students", new { firstName = "Данияр", lastName = "Айтбаев" });
        var studentId = (await studentResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        Assert.Empty((await bob.GetFromJsonAsync<JsonElement[]>("/api/groups"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/groups/{groupId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"/api/groups/{groupId}", new { name = "X", description = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/groups/{groupId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"/api/groups/{groupId}/students", new { firstName = "X", lastName = "Y" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"/api/groups/{groupId}/students/{studentId}", new { firstName = "X", lastName = "Y" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/groups/{groupId}/students/{studentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync("/api/ai/test", new { materialId, questionCount = 1, difficulty = "Средняя", types = new[] { "SingleChoice" } })).StatusCode);
    }
    [Fact]
    public async Task GroupsAndStudentsCanBeCreatedEditedAndDeleted()
    {
        using var factory = new ApiFactory(); using var client = factory.Browser(); await SignIn(client, "groups@example.com");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/groups", new { name = "", description = "" })).StatusCode);
        var group = await client.PostAsJsonAsync("/api/groups", new { name = "Python-24", description = "Новички" });
        var id = (await group.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/groups/{id}", new { name = "Python-25", description = "Новая группа" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/groups/{id}/students", new { firstName = "", lastName = "X" })).StatusCode);
        var student = await client.PostAsJsonAsync($"/api/groups/{id}/students", new { firstName = " Данияр ", lastName = " Айтбаев " });
        Assert.Equal(HttpStatusCode.Created, student.StatusCode);
        var studentId = (await student.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/groups/{id}/students/{studentId}", new { firstName = "Али", lastName = "Жумабаев" })).StatusCode);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/groups/{id}");
        Assert.Equal("Python-25", detail.GetProperty("name").GetString()); Assert.Equal("Али", detail.GetProperty("students")[0].GetProperty("firstName").GetString());
        var list = await client.GetFromJsonAsync<JsonElement[]>("/api/groups"); Assert.Equal(1, list![0].GetProperty("studentCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/groups/{Guid.NewGuid()}/students/{studentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/groups/{id}/students/{studentId}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/groups/{id}")).GetProperty("students").EnumerateArray());
        await client.PostAsJsonAsync($"/api/groups/{id}/students", new { firstName = "X", lastName = "Y" });
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/groups/{id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>("/api/groups"))!);
    }
    sealed class FakeAi : IAiService
    {
        public string? Text { get; private set; }
        public int Calls { get; private set; }
        public Task<string> GenerateMaterialAsync(GenerateMaterial request, CancellationToken ct) => Task.FromResult("unused");
        public Task<SaveQuiz> GenerateQuizAsync(string title, string materialText, GenerateQuiz request, CancellationToken ct) {
            Text = materialText; Calls++;
            return Task.FromResult(new SaveQuiz(title, request.MaterialId, [new QuestionInput(QuestionType.SingleChoice, "Вопрос", 1, "", [new("Да", true), new("Нет", false)])]));
        }
    }
    [Fact]
    public async Task GenerationUsesSavedMaterialTextAndDoesNotSaveUntilTeacherApproves()
    {
        var ai = new FakeAi(); using var factory = new ApiFactory(ai); using var client = factory.Browser(); await SignIn(client, "generation@example.com");
        var materialId = await Material(client, """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Цикл "},{"type":"text","text":"for","marks":[{"type":"bold"}]}]},{"type":"paragraph","content":[{"type":"text","text":"перебирает элементы"}]}]}""");
        var generated = await client.PostAsJsonAsync("/api/ai/test", new { materialId, materialText = "attacker content", questionCount = 1, difficulty = "Средняя", types = new[] { "SingleChoice" } });
        Assert.Equal(HttpStatusCode.OK, generated.StatusCode); Assert.Equal("Цикл for\nперебирает элементы", ai.Text); Assert.Equal(1, ai.Calls);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>("/api/tests"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/ai/test", new { materialId, questionCount = 31, difficulty = "Средняя", types = new[] { "SingleChoice" } })).StatusCode);
        var empty = await Material(client, "");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/ai/test", new { materialId = empty, questionCount = 1, difficulty = "Средняя", types = new[] { "SingleChoice" } })).StatusCode); Assert.Equal(1, ai.Calls);
    }
    [Theory]
    [InlineData("SingleChoice", 0)]
    [InlineData("SingleChoice", 2)]
    [InlineData("MultipleChoice", 0)]
    [InlineData("TrueFalse", 1)]
    [InlineData("ShortText", 0)]
    [InlineData("OpenText", 1)]
    public async Task InvalidAnswerStructuresAreRejected(string type, int correct)
    {
        using var factory = new ApiFactory(); using var client = factory.Browser(); await SignIn(client, "invalid@example.com");
        var options = type == "ShortText" ? Array.Empty<object>() : new object[] { new { text = "A", isCorrect = correct > 0 }, new { text = "B", isCorrect = correct > 1 } };
        var response = await client.PostAsJsonAsync("/api/tests", new { title = "Тест", materialId = (Guid?)null, questions = new[] { new { type, text = "Вопрос", points = 1, modelAnswer = "", options } } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>("/api/tests"))!);
    }
}
