using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EduForge.Api;
using EduForge.Application;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using Xunit;

namespace EduForge.Tests;

public sealed class SourceGenerationTests
{
    private sealed class SourceAi(bool forge = false) : ISourceMaterialAiService, IAiDiagnostics {
        public AiReceipt? LastReceipt => new("test-provider-snapshot", 120, 80);
        public List<SourceExcerpt>? Received;
        public Task<GeneratedSourceLesson> GenerateFromSourcesAsync(GenerateMaterial input, List<SourceExcerpt> sources, CancellationToken ct) {
            Received = sources;
            return Task.FromResult(new GeneratedSourceLesson([
                new("heading", "Циклы Python", "", []),
                new("screenshotPlaceholder", "Покажите окно редактора с циклом for", "", []),
                new("paragraph", "Цикл for перебирает элементы.", "", [new(sources[0].DocumentId, sources[0].SectionId, forge ? "Выдуманная цитата" : sources[0].Text)]),
                new("codeBlock", "for i in range(3):\n    print(i)", "python", [new(sources[0].DocumentId, sources[0].SectionId, sources[0].Text)])
            ]));
        }
    }
    private static async Task Login(HttpClient client, string email) {
        var r = await client.PostAsJsonAsync("/api/auth/register", new { email, name = "Teacher", password = "Correct horse battery staple!" }); r.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("Cookie", r.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
    }
    private static async Task<JsonElement> Upload(HttpClient client, string name = "Python.txt", byte[]? bytes = null) {
        using var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(bytes ?? Encoding.UTF8.GetBytes("Цикл for перебирает элементы. range(3) создаёт числа 0, 1, 2.")), "file", name);
        var r = await client.PostAsync("/api/sources", form); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task<HttpResponseMessage> Generate(HttpClient client, JsonElement source, string? section = null) {
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/sources/{source.GetProperty("id").GetGuid()}");
        return await client.PostAsJsonAsync("/api/ai/material/from-sources", new { material = new { subject = "Python", topic = "Циклы", requirements = "Пояснения и код" },
            sources = new[] { new { documentId = source.GetProperty("id").GetGuid(), sectionIds = new[] { section ?? detail.GetProperty("sections")[0].GetProperty("id").GetString() } } } });
    }

    [Fact]
    public async Task SourceLessonHasVerifiedEvidencePrivatePassportAndMeasuredReceipt() {
        var ai = new SourceAi(); using var factory = new ApiFactory { SourceAi = ai }; using var client = factory.Browser(); await Login(client, "source-owner@example.test");
        var source = await Upload(client); var response = await Generate(client, source); Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(); var runId = result.GetProperty("generationRunId").GetGuid();
        var passport = await client.GetFromJsonAsync<JsonElement>($"/api/generations/{runId}");
        Assert.Equal("Completed", passport.GetProperty("status").GetString()); Assert.Equal("test-provider-snapshot", passport.GetProperty("model").GetString());
        Assert.Equal(120, passport.GetProperty("inputTokens").GetInt32()); Assert.Equal(80, passport.GetProperty("outputTokens").GetInt32());
        Assert.Null(passport.GetProperty("evaluation").GetProperty("correctness").GetString());
        Assert.Single(ai.Received!);
        var materialResponse = await client.PostAsJsonAsync("/api/materials", new { title = "Учебный пример", subject = "Python", content = result.GetProperty("content").GetString(), generationRunId = runId });
        Assert.True(materialResponse.IsSuccessStatusCode, await materialResponse.Content.ReadAsStringAsync());
        var material = await materialResponse.Content.ReadFromJsonAsync<JsonElement>(); var materialId = material.GetProperty("id").GetGuid();
        var evidence = await client.GetFromJsonAsync<JsonElement>($"/api/materials/{materialId}/source-evidence");
        Assert.Equal(3, evidence.GetProperty("blocks").GetArrayLength()); Assert.False(evidence.GetProperty("blocks")[1].GetProperty("changed").GetBoolean());
        var document = System.Text.Json.Nodes.JsonNode.Parse(material.GetProperty("content").GetString()!)!;
        Assert.Equal("screenshotPlaceholder",document["content"]![1]!["type"]!.GetValue<string>());
        document["content"]![2]!["content"]![0]!["text"] = "Материал изменён преподавателем.";
        var content = document.ToJsonString();
        (await client.PutAsJsonAsync($"/api/materials/{materialId}", new { title = "Учебный пример", subject = "Python", content, generationRunId = runId })).EnsureSuccessStatusCode();
        evidence = await client.GetFromJsonAsync<JsonElement>($"/api/materials/{materialId}/source-evidence");
        Assert.True(evidence.GetProperty("blocks")[1].GetProperty("changed").GetBoolean());
    }

    [Fact]
    public async Task FabricatedCitationFailsAndFailureIsRecordedWithoutSavingMaterial() {
        using var factory = new ApiFactory { SourceAi = new SourceAi(true) }; using var client = factory.Browser(); await Login(client, "source-invalid@example.test");
        var response = await Generate(client, await Upload(client)); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>("/api/materials"))!);
        var runs = (await client.GetFromJsonAsync<JsonElement[]>("/api/generations"))!; Assert.Single(runs); Assert.Equal("Failed", runs[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task SourcesPassportsAndSnapshotsAreOwnerOnlyAndWrongSelectionsAreRejected() {
        using var factory = new ApiFactory { SourceAi = new SourceAi() }; using var alice = factory.Browser(); using var bob = factory.Browser();
        await Login(alice, "alice-sources@example.test"); await Login(bob, "bob-sources@example.test");
        var source = await Upload(alice); var id = source.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/sources/{id}")).StatusCode);
        var generated = await Generate(alice, source); generated.EnsureSuccessStatusCode(); var result = await generated.Content.ReadFromJsonAsync<JsonElement>();
        var run = result.GetProperty("generationRunId").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/generations/{run}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync("/api/materials", new { title = "Чужой паспорт", subject = "Python", content = "Текст", generationRunId = run })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Generate(alice, source, Guid.NewGuid().ToString())).StatusCode);
        Assert.Empty((await bob.GetFromJsonAsync<JsonElement[]>("/api/generations"))!);
    }

    [Fact]
    public async Task ReuploadReusesSourceAndUnsupportedOrScannedFilesAreRejected() {
        using var factory = new ApiFactory(); using var client = factory.Browser(); await Login(client, "upload-source@example.test");
        var a = await Upload(client); var b = await Upload(client); Assert.Equal(a.GetProperty("id").GetGuid(), b.GetProperty("id").GetGuid());
        using var form = new MultipartFormDataContent(); form.Add(new StringContent("hello"), "file", "file.exe");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/sources", form)).StatusCode);
        var builder = new PdfDocumentBuilder(); builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        Assert.Throws<InvalidDataException>(() => SourceExtraction.Extract("scan.pdf", builder.Build(), default));
    }

    [Fact]
    public void ExtractsPdfDocxAndUtf8TextWithoutExecutingEmbeddedContent() {
        var builder = new PdfDocumentBuilder(); var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica); page.AddText("Python loops and range", 12, new PdfPoint(20, 700), font);
        var pdf = SourceExtraction.Extract("lesson.pdf", builder.Build(), default); Assert.Contains("Python", pdf[0].Text); Assert.Equal("Страница 1", pdf[0].Label);
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true)) {
            using var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open());
            writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Циклы Python</w:t></w:r></w:p></w:body></w:document>");
        }
        Assert.Contains("Циклы Python", SourceExtraction.Extract("lesson.docx", buffer.ToArray(), default)[0].Text);
        Assert.Throws<InvalidDataException>(() => SourceExtraction.Extract("bad.pdf", Encoding.UTF8.GetBytes("not pdf"), default));
    }

    [Fact]
    public async Task ExpertEvaluationKeepsUnknownValuesEmptyAndCsvIsSafe() {
        using var factory = new ApiFactory { SourceAi = new SourceAi() }; using var client = factory.Browser(); await Login(client, "expert@example.test");
        var response = await Generate(client, await Upload(client)); var run = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("generationRunId").GetGuid();
        var evaluation = new GenerationEvaluation("=BAD()", 7.5m, 4, null, 5, 0, "+unsafe formula");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/generations/{run}/evaluation", evaluation)).StatusCode);
        var passport = await client.GetFromJsonAsync<JsonElement>($"/api/generations/{run}"); Assert.Equal(JsonValueKind.Null, passport.GetProperty("evaluation").GetProperty("clarity").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/generations/{run}/evaluation", evaluation with { Correctness = 6 })).StatusCode);
        var csv = await client.GetStringAsync("/api/generations/export.csv"); Assert.Contains("'=BAD()", csv); Assert.Contains("'+unsafe formula", csv);
    }

    [Fact]
    public void QualityChecksWarnAboutDuplicatesAndSimilarAnswersWithoutClaimingSemanticCorrectness() {
        var first = new QuestionInput(EduForge.Domain.QuestionType.SingleChoice, "Как используется цикл for в Python?", 1, "", [new("Перебирает элементы коллекции", true), new("Перебирает элементы коллекции!", false)]);
        var warnings = QuestionQuality.Analyze([first, first with { Text = "Как используется цикл for в Python?!" }]);
        Assert.Contains(warnings, w => w.Code == "DuplicateQuestion"); Assert.Contains(warnings, w => w.Code == "SimilarOptions");
        Assert.Empty(QuestionQuality.Analyze([new(EduForge.Domain.QuestionType.SingleChoice, "Чему равно 1 + 1?", 1, "", [new("2", true), new("3", false)])]));
    }
}
