using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
namespace EduForge.Tests;
public class AdminTests
{
 static async Task<string> Register(HttpClient client,string email){var r=await client.PostAsJsonAsync("/api/auth/register",new{email,password="Correct horse battery staple!",name="Teacher"});r.EnsureSuccessStatusCode();client.DefaultRequestHeaders.Add("Cookie",r.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;}
 [Fact] public async Task OnlyAdminCanDeleteAndDeletedSessionsAreRevoked(){using var factory=new ApiFactory{AdminEmail="owner@example.com"};using var admin=factory.Browser();using var other=factory.Browser();var owner=await Register(admin,"owner@example.com");var id=await Register(other,"other@example.com");Assert.Equal(HttpStatusCode.Forbidden,(await other.GetAsync("/api/admin/users")).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await admin.DeleteAsync("/api/admin/users/"+owner)).StatusCode);Assert.Equal(HttpStatusCode.NoContent,(await admin.DeleteAsync("/api/admin/users/"+id)).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await other.GetAsync("/api/auth/me")).StatusCode);}
 [Fact] public async Task RemovingOwnedDataRequiresExplicitFlag(){using var factory=new ApiFactory{AdminEmail="owner@example.com"};using var admin=factory.Browser();using var other=factory.Browser();await Register(admin,"owner@example.com");var id=await Register(other,"other@example.com");(await other.PostAsJsonAsync("/api/materials",new{title="Урок",subject="Python",content="Материал"})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await admin.DeleteAsync("/api/admin/users/"+id)).StatusCode);Assert.Equal(HttpStatusCode.NoContent,(await admin.DeleteAsync("/api/admin/users/"+id+"?deleteData=true")).StatusCode);}
}
