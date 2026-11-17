using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using EduForge.Api;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace EduForge.Tests;
public sealed class PersistentLoginRecoveryTests
{
    sealed class FakeMail : IRecoveryMailSender
    {
        public bool IsConfigured => true;
        public string? Link { get; private set; }
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public Task SendAsync(string recipient, string link, CancellationToken ct)
        {
            Calls++; Link = link;
            if (Fail) throw new System.Net.Mail.SmtpException("test failure");
            return Task.CompletedTask;
        }
    }
    [Fact]
    public async Task SignInIssuesPersistentCookieAndMatchingThirtyDayToken()
    {
        using var f = new ApiFactory(); using var c = f.Browser();
        var r = await c.PostAsJsonAsync("/api/auth/register",new { email="persist@example.test",name="QA",password="Long enough test password!" });
        r.EnsureSuccessStatusCode();
        var cookie = r.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("max-age=2592000",cookie);
        Assert.Contains("expires=",cookie);
        Assert.Contains("httponly",cookie,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax",cookie,StringComparison.OrdinalIgnoreCase);
        var encoded = cookie.Split(';')[0].Split('=',2)[1];
        var token = new JwtSecurityTokenHandler().ReadJwtToken(encoded);
        Assert.InRange(token.ValidTo,DateTime.UtcNow.AddDays(30).AddMinutes(-1),DateTime.UtcNow.AddDays(30).AddMinutes(1));
        using var reopened = f.Browser(); reopened.DefaultRequestHeaders.Add("Cookie",cookie.Split(';')[0]);
        Assert.Equal(HttpStatusCode.OK,(await reopened.GetAsync("/api/auth/me")).StatusCode);
    }
    [Fact]
    public async Task RecoverySendsSingleUseLinkAndDoesNotSendForUnknownAccount()
    {
        var mail = new FakeMail(); using var f = new ApiFactory { MailSender=mail }; using var c=f.Browser();
        (await c.PostAsJsonAsync("/api/auth/register",new { email="recover@example.test",name="QA",password="Original long test password!" })).EnsureSuccessStatusCode();
        var known = await c.PostAsJsonAsync("/api/auth/recover",new {email="recover@example.test"});
        var unknown = await c.PostAsJsonAsync("/api/auth/recover",new {email="missing@example.test"});
        Assert.Equal(await known.Content.ReadAsStringAsync(),await unknown.Content.ReadAsStringAsync());
        Assert.Equal(1,mail.Calls); Assert.NotNull(mail.Link);
        var token = mail.Link!.Split("token=")[1];
        var reset=new { token,password="Replacement long test password!" };
        Assert.Equal(HttpStatusCode.NoContent,(await c.PostAsJsonAsync("/api/auth/reset",reset)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/auth/reset",reset)).StatusCode);
    }
    [Fact]
    public async Task FailedMailInvalidatesTheResetLink()
    {
        var mail=new FakeMail {Fail=true};using var f=new ApiFactory {MailSender=mail};using var c=f.Browser();
        (await c.PostAsJsonAsync("/api/auth/register",new {email="failed@example.test",name="QA",password="Long test password for SMTP!"})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await c.PostAsJsonAsync("/api/auth/recover",new {email="failed@example.test"})).StatusCode);
        using var scope=f.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<MaterialDbContext>();
        Assert.True((await db.PasswordResets.SingleAsync()).Used);
    }
}
