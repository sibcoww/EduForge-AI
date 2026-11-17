using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EduForge.Application;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EduForge.Tests;

public sealed class GenerationAccountDeletionTests
{
    private sealed class Ai : IAiService {
        public Task<string> GenerateMaterialAsync(GenerateMaterial request, CancellationToken ct) => Task.FromResult("Учебный текст");
        public Task<SaveQuiz> GenerateQuizAsync(string title, string materialText, GenerateQuiz request, CancellationToken ct) => throw new NotSupportedException();
    }
    private static async Task<Guid> Register(HttpClient client, string email) {
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, name = "Teacher", password = "Correct horse battery staple!" });
        response.EnsureSuccessStatusCode(); client.DefaultRequestHeaders.Add("Cookie", response.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourcesAndGenerationHistoryCountAsOwnedDataAndCascadeOnExplicitDeletion(bool sourceOnly) {
        using var factory = new ApiFactory(new Ai()) { AdminEmail = "admin-delete@example.test" };
        using var admin = factory.Browser(); using var teacher = factory.Browser();
        await Register(admin, "admin-delete@example.test"); var owner = await Register(teacher, "teacher-delete@example.test");
        if (sourceOnly) {
            using var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("Учебный текст")), "file", "lesson.txt");
            (await teacher.PostAsync("/api/sources", form)).EnsureSuccessStatusCode();
        } else (await teacher.PostAsJsonAsync("/api/ai/material", new { subject = "Python", topic = "Циклы", requirements = "" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/admin/users/{owner}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/users/{owner}?deleteData=true")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await teacher.GetAsync("/api/auth/me")).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MaterialDbContext>();
        Assert.False(await db.SourceDocuments.AnyAsync(s => s.TeacherId == owner));
        Assert.False(await db.GenerationRuns.AnyAsync(s => s.TeacherId == owner));
    }
}
