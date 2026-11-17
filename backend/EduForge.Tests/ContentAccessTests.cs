using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduForge.Application;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace EduForge.Tests;
public sealed class ContentAccessTests
{
    static async Task Login(HttpClient client,string email){var r=await client.PostAsJsonAsync("/api/auth/register",new{email,name="Teacher",password="Correct horse battery staple!"});r.EnsureSuccessStatusCode();client.DefaultRequestHeaders.Add("Cookie",r.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);}
    static async Task<JsonElement> Json(HttpResponseMessage r){Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync());return await r.Content.ReadFromJsonAsync<JsonElement>();}
    static async Task<Guid> Material(HttpClient c,string content="Учебный материал"){var m=await Json(await c.PostAsJsonAsync("/api/materials",new{title="Материал",subject="Закрытая дисциплина",content}));return m.GetProperty("id").GetGuid();}
    static async Task<Guid> Quiz(HttpClient c,Guid? materialId=null){var q=await Json(await c.PostAsJsonAsync("/api/tests",new{title="Тест",materialId,questions=new[]{new{type="SingleChoice",text="Вопрос",points=5,modelAnswer="PRIVATE-ANSWER-SECRET",options=new[]{new{text="Первый",isCorrect=true},new{text="Второй",isCorrect=false}}}}}));return q.GetProperty("id").GetGuid();}
    static async Task<JsonElement> Access(HttpClient c,string kind,Guid id,string visibility){var state=await c.GetFromJsonAsync<JsonElement>($"/api/access/{kind}/{id}");return await Json(await c.PutAsJsonAsync($"/api/access/{kind}/{id}",new{visibility,version=state.GetProperty("version")}));}
    static async Task<string> Image(HttpClient c){using var form=new MultipartFormDataContent();form.Add(new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jf1kAAAAASUVORK5CYII=")),"file","screen.png");return (await Json(await c.PostAsync("/api/media",form))).GetProperty("url").GetString()!;}
    [Fact]
    public async Task UnlistedRequiresLoginAndTokenDoesNotLeakIntoCatalogOrOriginalImages(){
        using var f=new ApiFactory();using var owner=f.Browser();using var reader=f.Browser();using var anonymous=f.Browser();await Login(owner,"access-owner@example.test");await Login(reader,"access-reader@example.test");
        var visible=await Image(owner);var original=await Image(owner);var content=JsonSerializer.Serialize(new{type="doc",content=new object[]{new{type="paragraph",content=new[]{new{type="text",text="Урок"}}},new{type="image",attrs=new{src=visible,originalSrc=original,annotations=new[]{new{label="PRIVATE-ANNOTATION"}}}}}});var id=await Material(owner,content);
        Assert.Equal(HttpStatusCode.NotFound,(await reader.GetAsync($"/api/access/material/{id}")).StatusCode);
        var state=await Access(owner,"material",id,"Unlisted");var share=state.GetProperty("sharePath").GetString()!;var token=share.Split('/').Last();
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api"+share)).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(visible+"?share="+token)).StatusCode);
        var view=await reader.GetAsync("/api"+share);view.EnsureSuccessStatusCode();var body=await view.Content.ReadAsStringAsync();Assert.DoesNotContain(original,body);Assert.DoesNotContain("PRIVATE-ANNOTATION",body);Assert.Contains("no-store",view.Headers.CacheControl!.ToString());
        Assert.Equal(0,(await reader.GetFromJsonAsync<JsonElement>("/api/library")).GetProperty("total").GetInt32());Assert.Empty((await reader.GetFromJsonAsync<JsonElement>("/api/library/filters")).GetProperty("subjects").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound,(await reader.GetAsync($"/api/library/{id}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await reader.PostAsync($"/api/library/{id}/copy",null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await reader.GetAsync(visible)).StatusCode);Assert.Equal(HttpStatusCode.OK,(await reader.GetAsync(visible+"?share="+token)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await reader.GetAsync(original+"?share="+token)).StatusCode);
        var forged=await Material(reader,JsonSerializer.Serialize(new{type="doc",content=new object[]{new{type="paragraph",content=new[]{new{type="text",text="Посторонний материал"}}},new{type="image",attrs=new{src=original}}}}));var forgedAccess=await Access(reader,"material",forged,"Unlisted");var forgedToken=forgedAccess.GetProperty("sharePath").GetString()!.Split('/').Last();Assert.Equal(HttpStatusCode.NotFound,(await reader.GetAsync(original)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await reader.GetAsync(original+"?share="+forgedToken)).StatusCode);
        await Access(owner,"material",id,"Private");Assert.Equal(HttpStatusCode.NotFound,(await reader.GetAsync("/api"+share)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await reader.GetAsync(visible+"?share="+token)).StatusCode);
        var again=await Access(owner,"material",id,"Unlisted");Assert.NotEqual(share,again.GetProperty("sharePath").GetString());Assert.Equal(HttpStatusCode.NotFound,(await reader.GetAsync("/api"+share)).StatusCode);
    }
    [Fact]
    public async Task TestLinkHidesAnswerKeysAndUnlistedTestsDoNotCopyWithPublicMaterial(){
        using var f=new ApiFactory();using var owner=f.Browser();using var reader=f.Browser();await Login(owner,"test-share-owner@example.test");await Login(reader,"test-share-reader@example.test");var material=await Material(owner);var id=await Quiz(owner,material);
        var state=await Access(owner,"test",id,"Unlisted");var body=await (await reader.GetAsync("/api"+state.GetProperty("sharePath").GetString())).Content.ReadAsStringAsync();Assert.DoesNotContain("PRIVATE-ANSWER-SECRET",body);Assert.DoesNotContain("isCorrect",body,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("modelAnswer",body,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("grounding",body,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0,(await reader.GetFromJsonAsync<JsonElement>("/api/library/tests")).GetProperty("total").GetInt32());Assert.Equal(HttpStatusCode.NotFound,(await reader.PostAsync($"/api/library/tests/{id}/copy",null)).StatusCode);
        await Access(owner,"material",material,"Public");var copy=await Json(await reader.PostAsync($"/api/library/{material}/copy",null));Assert.Empty((await reader.GetFromJsonAsync<JsonElement>($"/api/tests?materialId={copy.GetProperty("id").GetGuid()}")).EnumerateArray());
        await Access(owner,"test",id,"Public");var second=await Json(await reader.PostAsync($"/api/library/{material}/copy",null));Assert.Single((await reader.GetFromJsonAsync<JsonElement>($"/api/tests?materialId={second.GetProperty("id").GetGuid()}")).EnumerateArray());
    }
    [Fact]
    public async Task PrivacyChangeCancelsPendingModerationAndOldVersionsCannotReopenIt(){
        using var f=new ApiFactory{ModerationEnabled=true,AdminEmail="sharing-admin@example.test"};using var owner=f.Browser();using var admin=f.Browser();await Login(owner,"moderated-owner@example.test");await Login(admin,"sharing-admin@example.test");var id=await Material(owner);
        var pending=await Access(owner,"material",id,"Public");Assert.Equal("Pending",pending.GetProperty("status").GetString());Assert.Equal(JsonValueKind.Null,pending.GetProperty("sharePath").ValueKind);var queue=await admin.GetFromJsonAsync<JsonElement[]>("/api/moderation");var review=Assert.Single(queue!).GetProperty("id").GetGuid();
        var closed=await Access(owner,"material",id,"Private");Assert.Equal(HttpStatusCode.NotFound,(await admin.PostAsJsonAsync($"/api/moderation/{review}",new{approve=true,reason=""})).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/api/library/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await owner.PutAsJsonAsync($"/api/access/material/{id}",new{visibility="Unlisted",version=pending.GetProperty("version")})).StatusCode);Assert.Equal("Private",(await owner.GetFromJsonAsync<JsonElement>($"/api/access/material/{id}")).GetProperty("visibility").GetString());
        await Access(owner,"material",id,"Public");queue=await admin.GetFromJsonAsync<JsonElement[]>("/api/moderation");review=Assert.Single(queue!).GetProperty("id").GetGuid();Assert.Equal(HttpStatusCode.NoContent,(await admin.PostAsJsonAsync($"/api/moderation/{review}",new{approve=true,reason=""})).StatusCode);Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync($"/api/library/{id}")).StatusCode);
        await Access(owner,"material",id,"Private");Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/api/library/{id}")).StatusCode);
    }
    [Fact]
    public async Task DeletingSourcesRevokesLinksPreservesTestsAndDoesNotTouchSessions(){
        using var f=new ApiFactory();using var owner=f.Browser();using var other=f.Browser();await Login(owner,"delete-owner@example.test");await Login(other,"delete-other@example.test");var material=await Material(owner);var test=await Quiz(owner,material);var shared=await Access(owner,"material",material,"Unlisted");var testShare=await Access(owner,"test",test,"Unlisted");
        var teacher=(await owner.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetGuid();Guid sessionId;const string snapshot="{\"Questions\":[],\"Participants\":[]}";
        using(var scope=f.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<MaterialDbContext>();var session=new TestingSession{TeacherId=teacher,Code="ABC12345",Title="Прежняя сессия",GroupName="Группа",Status=SessionStatus.Finished,Payload=snapshot};db.Sessions.Add(session);await db.SaveChangesAsync();sessionId=session.Id;}
        Assert.Equal(HttpStatusCode.NotFound,(await other.DeleteAsync($"/api/materials/{material}")).StatusCode);Assert.Equal(HttpStatusCode.NoContent,(await owner.DeleteAsync($"/api/materials/{material}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync("/api"+shared.GetProperty("sharePath").GetString())).StatusCode);
        var saved=await owner.GetFromJsonAsync<JsonElement>($"/api/tests/{test}");Assert.Equal(JsonValueKind.Null,saved.GetProperty("materialId").ValueKind);Assert.False(saved.GetProperty("controlled").GetBoolean());Assert.Equal("PRIVATE-ANSWER-SECRET",saved.GetProperty("questions")[0].GetProperty("modelAnswer").GetString());
        Assert.Equal(HttpStatusCode.NoContent,(await owner.DeleteAsync($"/api/tests/{test}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync("/api"+testShare.GetProperty("sharePath").GetString())).StatusCode);
        using(var scope=f.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<MaterialDbContext>();Assert.Equal(snapshot,(await db.Sessions.SingleAsync(s=>s.Id==sessionId)).Payload);Assert.Empty(await db.ContentAccesses.ToListAsync());}
    }
    [Theory]
    [InlineData("Это **слово**.","Это слово.")]
    [InlineData("2 ** 3", "2 ** 3")]
    [InlineData("`print(\"**x**\")`", "`print(\"**x**\")`")]
    [InlineData("```python\nprint(\"**x**\")\n```", "```python\nprint(\"**x**\")\n```")]
    [InlineData("f(**kwargs) и g(**kwargs)","f(**kwargs) и g(**kwargs)")]
    public void MarkdownCleanupPreservesCode(string input,string expected)=>Assert.Equal(expected,AiText.Plain(input));
}
