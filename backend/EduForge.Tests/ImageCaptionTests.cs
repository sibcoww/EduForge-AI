using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduForge.Api;
using EduForge.Application;
using Xunit;

namespace EduForge.Tests;

public sealed class ImageCaptionTests
{
    private sealed class CaptionAi : IImageCaptionAiService {
        public int Calls;
        public Task<string> GenerateCaptionAsync(byte[] image, string contentType, ImageCaptionRequest input, CancellationToken ct) {
            Calls++; Assert.Equal("image/png", contentType); Assert.NotEmpty(image);
            return Task.FromResult("1. В красном прямоугольнике выделен инструмент «Прямоугольник».");
        }
    }
    private static async Task Login(HttpClient client, string email) {
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Correct horse battery staple!", name = "Teacher" });
        response.EnsureSuccessStatusCode(); client.DefaultRequestHeaders.Add("Cookie", response.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
    }
    [Fact]
    public async Task CaptionRequiresOwnerAndRejectsExternalUrlsAndInvalidNumbering() {
        var ai = new CaptionAi(); using var factory = new ApiFactory { CaptionAi = ai }; using var alice = factory.Browser(); using var bob = factory.Browser();
        var empty = new ImageCaptionRequest($"/api/media/{Guid.NewGuid()}", "", []);
        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.PostAsJsonAsync("/api/ai/image-caption", empty)).StatusCode);
        await Login(alice,"caption-alice@example.test"); await Login(bob,"caption-bob@example.test");
        using var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jf1kAAAAASUVORK5CYII=")), "file", "screen.png");
        var upload = await alice.PostAsync("/api/media",form);upload.EnsureSuccessStatusCode();
        var url = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
        var input = new ImageCaptionRequest(url, "Инструменты", [new(1,"rect","#dc2626","Прямоугольник")]);
        Assert.Equal(HttpStatusCode.NotFound,(await bob.PostAsJsonAsync("/api/ai/image-caption",input)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await alice.PostAsJsonAsync("/api/ai/image-caption", input with { ImageUrl="https://example.com/private.png" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await alice.PostAsJsonAsync("/api/ai/image-caption", input with { Annotations=[new(2,"rect","#dc2626","")] })).StatusCode);
        Assert.Equal(0,ai.Calls);
        var result = await alice.PostAsJsonAsync("/api/ai/image-caption",input);result.EnsureSuccessStatusCode();
        Assert.Contains("красном",(await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("caption").GetString());Assert.Equal(1,ai.Calls);
    }
}
