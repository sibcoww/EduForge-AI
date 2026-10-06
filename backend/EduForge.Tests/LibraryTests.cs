using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
namespace EduForge.Tests;
public sealed class LibraryTests
{
    static async Task Login(HttpClient client, string email, string name) {
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, name, password = "Correct horse battery staple!" }); response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("Cookie", response.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
    }
    static async Task<JsonElement> Json(HttpResponseMessage response) { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    static async Task<string> Material(HttpClient client, string title = "Циклы в Python", string content = "Первый опубликованный текст") {
        var material = await Json(await client.PostAsJsonAsync("/api/materials", new { title, subject = "Python", content })); return material.GetProperty("id").GetString()!;
    }
    [Fact]
    public async Task PublishedTestsCopyIndependentlyAndWithMaterialWithoutPrivateTests() {
        using var factory = new ApiFactory(); using var author = factory.Browser(); using var reader = factory.Browser();
        await Login(author,"quiz-author@example.test","Автор"); await Login(reader,"quiz-reader@example.test","Читатель");
        var materialId = await Material(author);
        object Quiz(string title) => new { title, materialId, questions = new[] {new {type="SingleChoice",text="Вопрос",points=7,modelAnswer="",options=new[]{new{text="Да",isCorrect=true},new{text="Нет",isCorrect=false}}}}};
        var quiz = await Json(await author.PostAsJsonAsync("/api/tests", Quiz("Публичный тест"))); var id=quiz.GetProperty("id").GetString();
        await Json(await author.PostAsJsonAsync("/api/tests", Quiz("Личный тест")));
        Assert.Equal(HttpStatusCode.NotFound,(await reader.PostAsync($"/api/tests/{id}/publication",null)).StatusCode);
        Assert.Empty((await reader.GetFromJsonAsync<JsonElement>("/api/library/tests")).GetProperty("items").EnumerateArray());
        await Json(await author.PostAsync($"/api/tests/{id}/publication",null));
        await Json(await author.PutAsJsonAsync($"/api/tests/{id}",Quiz("Новая личная версия")));
        Assert.Equal("Публичный тест",(await reader.GetFromJsonAsync<JsonElement>($"/api/library/tests/{id}")).GetProperty("title").GetString());
        var copied=await Json(await reader.PostAsync($"/api/library/tests/{id}/copy",null));var copyId=copied.GetProperty("id").GetString();
        var copy=await reader.GetFromJsonAsync<JsonElement>($"/api/tests/{copyId}");Assert.Equal(JsonValueKind.Null,copy.GetProperty("materialId").ValueKind);Assert.Equal(7,copy.GetProperty("questions")[0].GetProperty("points").GetInt32());Assert.Equal(HttpStatusCode.NotFound,(await author.GetAsync($"/api/tests/{copyId}")).StatusCode);
        await Json(await author.PostAsync($"/api/materials/{materialId}/publication",null));
        var materialCopy=await Json(await reader.PostAsync($"/api/library/{materialId}/copy",null));var newMaterialId=materialCopy.GetProperty("id").GetString();
        var related=await reader.GetFromJsonAsync<JsonElement>($"/api/tests?materialId={newMaterialId}");Assert.Single(related.EnumerateArray());Assert.Equal("Публичный тест",related[0].GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NoContent,(await author.DeleteAsync($"/api/tests/{id}/publication")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await reader.GetAsync($"/api/library/tests/{id}")).StatusCode);Assert.Equal(HttpStatusCode.OK,(await reader.GetAsync($"/api/tests/{copyId}")).StatusCode);
    }
    [Fact]
    public async Task PublicationIsExplicitSnapshotAndCopiesArePrivateIndependentMaterials() {
        using var factory = new ApiFactory(); using var author = factory.Browser(); using var reader = factory.Browser();
        await Login(author, "author@example.test", "Автор"); await Login(reader, "reader@example.test", "Читатель");
        const string formatted = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Первый опубликованный текст\",\"marks\":[{\"type\":\"bold\"}]}]}]}";
        var id = await Material(author, content: formatted);
        var before = await reader.GetFromJsonAsync<JsonElement>("/api/library"); Assert.Equal(0, before.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/library/{id}")).StatusCode);
        await Json(await author.PostAsync($"/api/materials/{id}/publication", null));
        var published = await reader.GetFromJsonAsync<JsonElement>($"/api/library/{id}"); Assert.Equal("Первый опубликованный текст", published.GetProperty("content").GetString()); Assert.Equal("Автор", published.GetProperty("authorName").GetString());
        Assert.False(published.GetProperty("isOwn").GetBoolean()); Assert.False(published.TryGetProperty("teacherId", out _));
        await Json(await author.PutAsJsonAsync($"/api/materials/{id}", new { title = "Изменённый черновик", subject = "Python", content = "Приватная версия" }));
        published = await reader.GetFromJsonAsync<JsonElement>($"/api/library/{id}"); Assert.Equal("Первый опубликованный текст", published.GetProperty("content").GetString());
        var copy = await Json(await reader.PostAsync($"/api/library/{id}/copy", null)); var copyId = copy.GetProperty("id").GetString(); Assert.NotEqual(id, copyId);
        Assert.Equal(formatted, (await reader.GetFromJsonAsync<JsonElement>($"/api/materials/{copyId}")).GetProperty("content").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await author.GetAsync($"/api/materials/{copyId}")).StatusCode);
        await Json(await reader.PutAsJsonAsync($"/api/materials/{copyId}", new { title = "Мой урок", subject = "Python", content = "Мои изменения" }));
        Assert.Equal("Первый опубликованный текст", (await author.GetFromJsonAsync<JsonElement>($"/api/library/{id}")).GetProperty("content").GetString());
        await Json(await author.PostAsync($"/api/materials/{id}/publication", null));
        Assert.Equal("Приватная версия", (await reader.GetFromJsonAsync<JsonElement>($"/api/library/{id}")).GetProperty("content").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await author.DeleteAsync($"/api/materials/{id}/publication")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/library/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.PostAsync($"/api/library/{id}/copy", null)).StatusCode);
        Assert.Equal("Мои изменения", (await reader.GetFromJsonAsync<JsonElement>($"/api/materials/{copyId}")).GetProperty("content").GetString());
    }
    [Fact]
    public async Task PublishingRequiresOwnershipContentAndOriginAndReadingRequiresTeacherLogin() {
        using var factory = new ApiFactory(); using var author = factory.Browser(); using var other = factory.Browser(); using var guest = factory.Browser();
        await Login(author, "owner@example.test", "Автор"); await Login(other, "other@example.test", "Другой"); var id = await Material(author);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/library")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync($"/api/library/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/materials/{id}/publication", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/materials/{id}/publication")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/materials/{id}/publication")).StatusCode);
        var empty = await Material(author, "Пустой материал", "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\"}]}");
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsync($"/api/materials/{empty}/publication", null)).StatusCode);
        author.DefaultRequestHeaders.Remove("Origin"); Assert.Equal(HttpStatusCode.Forbidden, (await author.PostAsync($"/api/materials/{id}/publication", null)).StatusCode);
    }
    [Fact]
    public async Task SearchFiltersAndPaginationReturnOnlyPublishedSummaries() {
        using var factory = new ApiFactory(); using var author = factory.Browser(); using var reader = factory.Browser(); await Login(author, "search@example.test", "Автор"); await Login(reader, "search-reader@example.test", "Другой");
        for (var i = 0; i < 22; i++) { var id = await Material(author, $"Циклы {i}"); await Json(await author.PostAsync($"/api/materials/{id}/publication", null)); }
        await Material(author, "Приватное");
        var page = await reader.GetFromJsonAsync<JsonElement>("/api/library?q=" + Uri.EscapeDataString("ЦИКЛЫ")); Assert.Equal(22, page.GetProperty("total").GetInt32()); Assert.Equal(20, page.GetProperty("items").GetArrayLength()); Assert.False(page.GetProperty("items")[0].TryGetProperty("content", out _));
        var second = await reader.GetFromJsonAsync<JsonElement>("/api/library?page=2&subject=Python&language=" + Uri.EscapeDataString("Русский")); Assert.Equal(2, second.GetProperty("items").GetArrayLength());
        Assert.Equal(0, (await reader.GetFromJsonAsync<JsonElement>("/api/library?mine=true")).GetProperty("total").GetInt32());
        Assert.Equal(22, (await author.GetFromJsonAsync<JsonElement>("/api/library?mine=true")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await reader.GetFromJsonAsync<JsonElement>("/api/library?q=%25")).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await reader.GetAsync("/api/library?page=0")).StatusCode);
    }
}
