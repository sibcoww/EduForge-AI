using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduForge.Infrastructure;
using EduForge.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EduForge.Tests;
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    readonly SqliteConnection connection = new($"Data Source=eduforge-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
    readonly IAiService? ai;
    public bool GoogleEnabled { get; set; }
    public string? AdminEmail { get; set; }
    public string? ContentRootOverride { get; set; }
    public TimeProvider? Clock { get; set; }
    public ApiFactory(IAiService? ai = null) { this.ai = ai; connection.Open(); }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        if (ContentRootOverride is not null) builder.UseContentRoot(ContentRootOverride);
        builder.ConfigureLogging(logging => { logging.ClearProviders(); logging.AddConsole(); logging.SetMinimumLevel(LogLevel.Warning); });
        if (GoogleEnabled) { builder.UseSetting("Google:ClientId", "test-client"); builder.UseSetting("Google:ClientSecret", "test-secret"); }
        builder.UseSetting("Auth:SigningKey", "test-only-key-with-more-than-thirty-two-bytes");
        builder.UseSetting("ConnectionStrings:Database", "Host=localhost;Database=unused");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
            ["Auth:SigningKey"] = "test-only-key-with-more-than-thirty-two-bytes",
            ["ConnectionStrings:Database"] = "Host=localhost;Database=unused",
            ["Database:ApplyMigrations"] = "false", ["AI:ApiKey"] = "", ["AI:Model"] = "",
            ["Admin:Email"] = AdminEmail, ["Google:ClientId"] = GoogleEnabled ? "test-client" : "", ["Google:ClientSecret"] = GoogleEnabled ? "test-secret" : ""
        }));
        builder.ConfigureServices(services => {
            if (Clock is not null) { services.RemoveAll<TimeProvider>(); services.AddSingleton(Clock); }
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            services.RemoveAll<DbContextOptions<MaterialDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<MaterialDbContext>>();
            services.AddDbContext<MaterialDbContext>(options => options.UseSqlite(connection.ConnectionString));
            if (ai is not null) { services.RemoveAll<IAiService>(); services.AddSingleton(ai); }
        });
    }
    public HttpClient Browser()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:3000");
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<MaterialDbContext>().Database.EnsureCreated();
        return client;
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) connection.Dispose(); }
}
public sealed class AuthenticationTests
{
    static async Task<string> Register(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Correct horse battery staple!", name = "Преподаватель" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        return cookie.Split(';')[0];
    }
    [Fact]
    public async Task MaterialsRequireLoginAndAreIsolatedBetweenTeachers()
    {
        using var factory = new ApiFactory(); using var alice = factory.Browser(); using var bob = factory.Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.GetAsync("/api/materials")).StatusCode);
        alice.DefaultRequestHeaders.Add("Cookie", await Register(alice, "alice@example.com"));
        var saved = await alice.PostAsJsonAsync("/api/materials", new { title = "Цикл for", subject = "Python", content = "Объяснение" });
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var id = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        bob.DefaultRequestHeaders.Add("Cookie", await Register(bob, "bob@example.com"));
        Assert.Empty((await bob.GetFromJsonAsync<JsonElement[]>("/api/materials"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/materials/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"/api/materials/{id}", new { title = "Changed", subject = "Python", content = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/materials/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.PutAsJsonAsync($"/api/materials/{id}", new { title = "Циклы", subject = "Python", content = "Новая версия" })).StatusCode);
        var updated = await alice.GetFromJsonAsync<JsonElement>($"/api/materials/{id}");
        Assert.Equal("Новая версия", updated.GetProperty("content").GetString());
        Assert.Equal("Начинающий", updated.GetProperty("level").GetString());
    }
    [Fact]
    public async Task LoginValidationCsrfAndLogoutRevocationWork()
    {
        using var factory = new ApiFactory(); using var client = factory.Browser();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new { email = "bad", password = "short", name = "" })).StatusCode);
        var cookie = await Register(client, "teacher@example.com");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email = "teacher@example.com", password = "wrong" })).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "TEACHER@example.com", password = "Correct horse battery staple!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        client.DefaultRequestHeaders.Remove("Origin");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        client.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", "http://localhost:3000");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
    [Fact]
    public async Task UnconfiguredAiReturnsClearErrorAndInvalidMaterialIsRejected()
    {
        using var factory = new ApiFactory(); using var client = factory.Browser();
        client.DefaultRequestHeaders.Add("Cookie", await Register(client, "ai@example.com"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/materials", new { title = "", subject = "", content = "" })).StatusCode);
        var result = await client.PostAsJsonAsync("/api/ai/material", new { subject = "Python", topic = "Циклы", requirements = "" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.Contains("AI", await result.Content.ReadAsStringAsync());
    }
}
