using System.Security.Claims;
using System.Text.Json;
using EduForge.Api;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using Xunit;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace EduForge.Tests;
public class GoogleClaimTests
{
 [Fact]
 public void ActualGoogleHandlerMapsV3VerifiedEmail(){using var factory=new ApiFactory{GoogleEnabled=true};var options=factory.Services.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get("Google");using var json=JsonDocument.Parse("""{"sub":"google-user-id","email":"teacher@example.com","email_verified":true}""");var identity=new ClaimsIdentity();foreach(var action in options.ClaimActions)action.Run(json.RootElement,identity,"Google");Assert.True(AuthenticationEndpoints.GoogleEmailVerified(new ClaimsPrincipal(identity)));}
 [Theory] [InlineData(true)] [InlineData(false)]
 public void ReadsGoogleBooleanUsingActualClaimMapping(bool verified){using var json=JsonDocument.Parse(JsonSerializer.Serialize(new{email_verified=verified}));var identity=new ClaimsIdentity();new JsonKeyClaimAction("email_verified",ClaimValueTypes.String,"email_verified").Run(json.RootElement,identity,"Google");Assert.Equal(verified,AuthenticationEndpoints.GoogleEmailVerified(new ClaimsPrincipal(identity)));}
 [Theory] [InlineData("true",true)] [InlineData("True",true)] [InlineData("false",false)] [InlineData("",false)]
 public void AcceptsOnlyVerifiedEmail(string value,bool expected){Assert.Equal(expected,AuthenticationEndpoints.GoogleEmailVerified(new ClaimsPrincipal(new ClaimsIdentity([new Claim("email_verified",value)]))));}
}
