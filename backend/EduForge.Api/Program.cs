using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using EduForge.Api;
using EduForge.Application;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var localSettings = Environment.GetEnvironmentVariable("EDUFORGE_SETTINGS_FILE");
if (builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(localSettings))
    builder.Configuration.AddJsonFile(localSettings, optional: false, reloadOnChange: true);
var frontend = builder.Configuration["Frontend:Origin"] ?? "http://localhost:3000";
var key = builder.Configuration["Auth:SigningKey"] ?? throw new InvalidOperationException("Set Auth__SigningKey to a random secret of at least 32 bytes.");
if (Encoding.UTF8.GetByteCount(key) < 32) throw new InvalidOperationException("Auth signing key must contain at least 32 bytes.");
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSignalR();
builder.Services.AddScoped<SessionService>();
builder.Services.AddHostedService<SessionExpiryWorker>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddDbContext<MaterialDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("Database") ?? throw new InvalidOperationException("ConnectionStrings:Database is required.")));
builder.Services.AddScoped<IMaterialRepository, MaterialRepository>();
builder.Services.AddScoped<IPasswordHasher<Teacher>, PasswordHasher<Teacher>>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddHttpClient<IAiService, OpenAiService>(http => http.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(frontend).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
var auth = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => {
    options.TokenValidationParameters = new TokenValidationParameters {
        ValidateIssuer = true, ValidIssuer = "EduForge", ValidateAudience = true, ValidAudience = "EduForge",
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30), ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
    };
    options.Events = new JwtBearerEvents {
        OnMessageReceived = context => { context.Token = context.Request.Cookies[TokenService.CookieName]; return Task.CompletedTask; },
        OnTokenValidated = async context => {
            if (!Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || !Guid.TryParse(context.Principal?.FindFirstValue("stamp"), out var stamp)) { context.Fail("Invalid session"); return; }
            var db = context.HttpContext.RequestServices.GetRequiredService<MaterialDbContext>();
            if (!await db.Teachers.AnyAsync(x => x.Id == id && x.SecurityStamp == stamp, context.HttpContext.RequestAborted)) context.Fail("Session revoked");
        }
    };
});
auth.AddCookie("External", options => { options.ExpireTimeSpan = TimeSpan.FromMinutes(5); options.Cookie.HttpOnly = true; });
var googleEnabled = !string.IsNullOrWhiteSpace(builder.Configuration["Google:ClientId"]) && !string.IsNullOrWhiteSpace(builder.Configuration["Google:ClientSecret"]);
if (googleEnabled) auth.AddGoogle(options => {
    options.SignInScheme = "External";
    options.ClientId = builder.Configuration["Google:ClientId"]!;
    options.ClientSecret = builder.Configuration["Google:ClientSecret"]!;
    options.ClaimActions.MapJsonKey("email_verified", "email_verified");
    options.Events.OnRemoteFailure = context => { context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GoogleLogin").LogWarning("Google OAuth failed. Failure type: {Type}", context.Failure?.GetType().Name); context.HandleResponse(); context.Response.Redirect(frontend + "/?authError=google"); return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = 429;
    options.AddPolicy("join", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("ai", context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.UseExceptionHandler();
app.UseCors();
// Cookie-authenticated writes must carry the exact allowed Origin (CSRF protection).
app.Use(async (context, next) => {
    if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method)
        && context.Request.Headers.Origin.ToString() != frontend) { context.Response.StatusCode = 403; return; }
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations")) {
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<MaterialDbContext>().Database.MigrateAsync();
}
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapTeacherAuthentication(googleEnabled, frontend);
app.MapAdmin();
app.MapTeaching();
app.MapSessions();
app.MapLibrary();
app.MapTestLibrary();
var materials = app.MapGroup("/api/materials").RequireAuthorization();
materials.MapGet("/", async (ClaimsPrincipal user, IMaterialRepository repository, CancellationToken ct) => Results.Ok(await repository.ListAsync(TeacherId(user), ct)));
materials.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, IMaterialRepository repository, CancellationToken ct) =>
    await repository.GetAsync(id, TeacherId(user), ct) is { } material ? Results.Ok(material) : Results.NotFound());
materials.MapPost("/", async (SaveMaterial request, ClaimsPrincipal user, IMaterialRepository repository, CancellationToken ct) => {
    if (Validate(request) is { } error) return Results.ValidationProblem(error);
    var material = new Material { TeacherId = TeacherId(user) };
    Apply(material, request);
    await repository.SaveAsync(material, ct);
    return Results.Created($"/api/materials/{material.Id}", material);
});
materials.MapPut("/{id:guid}", async (Guid id, SaveMaterial request, ClaimsPrincipal user, IMaterialRepository repository, CancellationToken ct) => {
    if (Validate(request) is { } error) return Results.ValidationProblem(error);
    var material = await repository.GetAsync(id, TeacherId(user), ct);
    if (material is null) return Results.NotFound();
    Apply(material, request);
    await repository.SaveAsync(material, ct);
    return Results.Ok(material);
});
app.MapPost("/api/ai/material", async (GenerateMaterial request, IAiService ai, CancellationToken ct) => {
    if (Validate(new SaveMaterial(request.Topic, request.Subject, "", request.Level, request.Language)) is { } errors) return Results.ValidationProblem(errors);
    if (request.Requirements is null || request.Requirements.Length > 4000) return Results.BadRequest(new { message = "Дополнительные требования: до 4000 символов." });
    try { return Results.Ok(new { content = await ai.GenerateMaterialAsync(request, ct) }); }
    catch (AiUnavailableException ex) { return Results.Problem(ex.Message, statusCode: 503); }
    catch (HttpRequestException) { return Results.Problem("Не удалось связаться с AI-провайдером.", statusCode: 503); }
    catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Results.Problem("AI не ответил вовремя. Повторите запрос.", statusCode: 504); }
}).RequireAuthorization().RequireRateLimiting("ai");
app.Run();
static Guid TeacherId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
static Dictionary<string, string[]>? Validate(SaveMaterial request) {
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200) errors["title"] = ["Укажите название до 200 символов."];
    if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > 100) errors["subject"] = ["Укажите предмет до 100 символов."];
    if (request.Content is null || request.Content.Length > 500000) errors["content"] = ["Содержание превышает допустимый размер."];
    if (string.IsNullOrWhiteSpace(request.Level) || request.Level.Length > 50) errors["level"] = ["Укажите уровень до 50 символов."];
    if (string.IsNullOrWhiteSpace(request.Language) || request.Language.Length > 50) errors["language"] = ["Укажите язык до 50 символов."];
    return errors.Count == 0 ? null : errors;
}
static void Apply(Material material, SaveMaterial request) {
    material.Title = request.Title.Trim(); material.Subject = request.Subject.Trim(); material.Content = request.Content;
    material.Level = request.Level; material.Language = request.Language; material.UpdatedAt = DateTimeOffset.UtcNow;
}
public partial class Program;
