using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace EduForge.Api;
public record Credentials(string Email, string Password, string Name = "");
public sealed class TokenService(IConfiguration configuration, IWebHostEnvironment environment)
{
    public const string CookieName = "eduforge_session";
    public void SignIn(HttpContext context, Teacher teacher)
    {
        var expires = DateTime.UtcNow.AddHours(8);
        var token = new JwtSecurityToken("EduForge", "EduForge", [
            new Claim(ClaimTypes.NameIdentifier, teacher.Id.ToString()),
            new Claim("stamp", teacher.SecurityStamp.ToString())
        ], expires: expires, signingCredentials: new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Auth:SigningKey"]!)), SecurityAlgorithms.HmacSha256));
        context.Response.Cookies.Append(CookieName, new JwtSecurityTokenHandler().WriteToken(token), new CookieOptions {
            HttpOnly = true, Secure = !environment.IsDevelopment(), SameSite = SameSiteMode.Lax, Path = "/", Expires = expires
        });
    }
}
public static class AuthenticationEndpoints
{
    static readonly string DummyPasswordHash = new PasswordHasher<Teacher>().HashPassword(new Teacher(), "unused-password-for-timing-check");
    public static void MapTeacherAuthentication(this WebApplication app, bool googleEnabled, string frontend)
    {
        var group = app.MapGroup("/api/auth");
        group.MapGet("/providers", () => Results.Ok(new { google = googleEnabled }));
        group.MapPost("/register", async (Credentials input, MaterialDbContext db, IPasswordHasher<Teacher> hasher, TokenService tokens, HttpContext context, CancellationToken ct) => {
            if (!ValidEmail(input.Email) || input.Password is null || input.Password.Length < 12 || input.Password.Length > 128 || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 100)
                return Results.BadRequest(new { message = "Укажите имя, корректный email и пароль от 12 до 128 символов." });
            var teacher = new Teacher { Email = input.Email.Trim(), NormalizedEmail = input.Email.Trim().ToUpperInvariant(), Name = input.Name.Trim() };
            teacher.PasswordHash = hasher.HashPassword(teacher, input.Password);
            db.Teachers.Add(teacher);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) {
                return Results.Conflict(new { message = "Не удалось зарегистрироваться с этим email." });
            }
            tokens.SignIn(context, teacher);
            return Results.Ok(Profile(teacher, app.Configuration));
        }).RequireRateLimiting("auth");
        group.MapPost("/login", async (Credentials input, MaterialDbContext db, IPasswordHasher<Teacher> hasher, TokenService tokens, HttpContext context, CancellationToken ct) => {
            if (!ValidEmail(input.Email) || string.IsNullOrEmpty(input.Password) || input.Password.Length > 128) return Results.Unauthorized();
            var teacher = await db.Teachers.SingleOrDefaultAsync(x => x.NormalizedEmail == input.Email.Trim().ToUpperInvariant(), ct);
            // Perform a hash verification even for an unknown email to reduce timing differences.
            var candidate = teacher ?? new Teacher();
            var hash = teacher?.PasswordHash ?? DummyPasswordHash;
            var result = hasher.VerifyHashedPassword(candidate, hash, input.Password);
            if (teacher?.PasswordHash is null || result == PasswordVerificationResult.Failed) return Results.Unauthorized();
            if (result == PasswordVerificationResult.SuccessRehashNeeded) { teacher.PasswordHash = hasher.HashPassword(teacher, input.Password); await db.SaveChangesAsync(ct); }
            tokens.SignIn(context, teacher);
            return Results.Ok(Profile(teacher, app.Configuration));
        }).RequireRateLimiting("auth");
        group.MapGet("/me", async (ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var teacher = await db.Teachers.FindAsync([Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!)], ct);
            return teacher is null ? Results.Unauthorized() : Results.Ok(Profile(teacher, app.Configuration));
        }).RequireAuthorization();
        group.MapPost("/logout", async (ClaimsPrincipal user, MaterialDbContext db, HttpContext context, CancellationToken ct) => {
            var teacher = await db.Teachers.FindAsync([Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!)], ct);
            if (teacher is not null) { teacher.SecurityStamp = Guid.NewGuid(); await db.SaveChangesAsync(ct); }
            context.Response.Cookies.Delete(TokenService.CookieName, new CookieOptions { Path = "/" });
            return Results.NoContent();
        }).RequireAuthorization();
        group.MapGet("/google", () => googleEnabled
            ? Results.Challenge(new AuthenticationProperties { RedirectUri = "/api/auth/google/complete" }, ["Google"])
            : Results.Problem("Google OAuth ещё не настроен.", statusCode: 503)).RequireRateLimiting("auth");
        group.MapGet("/google/complete", async (HttpContext context, MaterialDbContext db, TokenService tokens, CancellationToken ct) => {
            var result = await context.AuthenticateAsync("External");
            await context.SignOutAsync("External");
            var googleId = result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = result.Principal?.FindFirstValue(ClaimTypes.Email);
            if (!result.Succeeded || string.IsNullOrEmpty(googleId) || !ValidEmail(email) || !GoogleEmailVerified(result.Principal))
                return Results.Redirect(frontend + "/?authError=google");
            var teacher = await db.Teachers.SingleOrDefaultAsync(x => x.GoogleId == googleId, ct);
            if (teacher is null) {
                // Never automatically link an existing password account by email alone.
                if (await db.Teachers.AnyAsync(x => x.NormalizedEmail == email!.Trim().ToUpperInvariant(), ct)) return Results.Redirect(frontend + "/?authError=existing");
                teacher = new Teacher { Email = email!, NormalizedEmail = email!.Trim().ToUpperInvariant(), Name = result.Principal?.FindFirstValue(ClaimTypes.Name) ?? "Преподаватель", GoogleId = googleId };
                if (teacher.Name.Length > 100) teacher.Name = teacher.Name[..100];
                db.Teachers.Add(teacher);
                try { await db.SaveChangesAsync(ct); } catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Redirect(frontend + "/?authError=existing"); }
            }
            tokens.SignIn(context, teacher);
            return Results.Redirect(frontend);
        });
    }
    public static bool GoogleEmailVerified(ClaimsPrincipal? principal) => bool.TryParse(principal?.FindFirstValue("email_verified"), out var verified) && verified;
    static bool ValidEmail(string? email) => email is not null && email.Length <= 254 && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email.Trim();
    static object Profile(Teacher teacher, IConfiguration config) => new { teacher.Id, teacher.Email, teacher.Name, IsAdmin = AdminEndpoints.IsAdmin(teacher.Email, config) };
}
