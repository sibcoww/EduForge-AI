using System.Net;
using System.Text;
using System.Text.Json;
using EduForge.Application;
using EduForge.Infrastructure;
using EduForge.Domain;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EduForge.Tests;
public sealed class OpenAiServiceTests
{
    sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public string? Scheme { get; private set; }
        public Uri? Uri { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(ct); Scheme = request.Headers.Authorization?.Scheme; Uri = request.RequestUri;
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
    static OpenAiService Service(Handler handler) => new(new HttpClient(handler), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["AI:ApiKey"] = "fake-test-key", ["AI:Model"] = "configured-test-model"
    }).Build());
    [Fact]
    public async Task UsesUpdatedLocalModelAndReportsMissingModelSeparately()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["AI:ApiKey"] = "old-key", ["AI:Model"] = "old-model",
            ["AI__ApiKey"] = "local-key", ["AI__Model"] = ""
        }).Build();
        var handler = new Handler(HttpStatusCode.OK, """{"status":"completed","output":[{"content":[{"type":"output_text","text":"Урок"}]}]}""");
        var service = new OpenAiService(new HttpClient(handler), configuration);
        var input = new GenerateMaterial("Python", "Циклы", "");
        var error = await Assert.ThrowsAsync<AiUnavailableException>(() => service.GenerateMaterialAsync(input, default));
        Assert.Contains("AI__Model", error.Message);
        Assert.Null(handler.RequestBody);
        configuration["AI__Model"] = "updated-model";
        Assert.Equal("Урок", await service.GenerateMaterialAsync(input, default));
        using var payload = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("updated-model", payload.RootElement.GetProperty("model").GetString());
    }
    [Fact]
    public async Task SendsServerSideConfigurationAndExtractsOnlyText()
    {
        var handler = new Handler(HttpStatusCode.OK, """{"status":"completed","output":[{"type":"reasoning"},{"type":"message","content":[{"type":"output_text","text":"Объяснение"},{"type":"output_text","text":"Пример"}]}]}""");
        var result = await Service(handler).GenerateMaterialAsync(new GenerateMaterial("Python", "Циклы", "Пример range", "Начинающий", "Русский"), default);
        Assert.Equal("Объяснение\nПример", result);
        Assert.Equal("Bearer", handler.Scheme);
        Assert.Equal("https://api.openai.com/v1/responses", handler.Uri!.ToString());
        using var payload = JsonDocument.Parse(handler.RequestBody!);
        Assert.False(payload.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal("configured-test-model", payload.RootElement.GetProperty("model").GetString());
        using var input = JsonDocument.Parse(payload.RootElement.GetProperty("input").GetString()!);
        Assert.Equal("Циклы", input.RootElement.GetProperty("Topic").GetString());
        Assert.DoesNotContain("fake-test-key", handler.RequestBody);
    }
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "{\"provider_secret\":\"do-not-expose\"}")]
    [InlineData(HttpStatusCode.OK, "{\"status\":\"incomplete\",\"output\":[]}")]
    [InlineData(HttpStatusCode.OK, "{\"status\":\"completed\",\"output\":[]}")]
    [InlineData(HttpStatusCode.OK, "not-json")]
    public async Task RejectsProviderErrorsAndIncompleteOrEmptyOutput(HttpStatusCode status, string body)
    {
        var error = await Assert.ThrowsAsync<AiUnavailableException>(() => Service(new Handler(status, body)).GenerateMaterialAsync(new GenerateMaterial("Python", "Циклы", ""), default));
        Assert.DoesNotContain("do-not-expose", error.Message);
    }
    [Fact]
    public async Task GeneratesStructuredTestUsingSourceAndRequestedTypes()
    {
        var generated = new { title = "Циклы", questions = new[] { new { type = "SingleChoice", text = "Какой цикл?", points = 1, modelAnswer = "", options = new[] { new { text = "for", isCorrect = true }, new { text = "if", isCorrect = false } } } } };
        var response = JsonSerializer.Serialize(new { status = "completed", output = new[] { new { content = new[] { new { type = "output_text", text = JsonSerializer.Serialize(generated) } } } } });
        var handler = new Handler(HttpStatusCode.OK, response);
        var source = Guid.NewGuid();
        var result = await Service(handler).GenerateQuizAsync("Циклы", "for перебирает элементы", new GenerateQuiz(source, 1, "Средняя", [QuestionType.SingleChoice]), default);
        Assert.Equal(source, result.MaterialId); Assert.Single(result.Questions); Assert.Equal(QuestionType.SingleChoice, result.Questions[0].Type);
        using var payload = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("json_schema", payload.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        Assert.True(payload.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        using var input = JsonDocument.Parse(payload.RootElement.GetProperty("input").GetString()!);
        Assert.Equal("for перебирает элементы", input.RootElement.GetProperty("materialText").GetString());
    }
    [Theory]
    [InlineData(3)]
    [InlineData(101)]
    [InlineData(300)]
    public async Task AllocatesExactRequestedTotalAndPreservesCorrectOptions(int total)
    {
        var questions = Enumerable.Range(0, 3).Select(i => new {
            type = "SingleChoice", text = $"Вопрос {i}", points = i + 1, modelAnswer = "",
            options = new[] { new { text = "Верно", isCorrect = true }, new { text = "Неверно", isCorrect = false } }
        }).ToArray();
        var generated = JsonSerializer.Serialize(new { title = "Тест", questions });
        var response = JsonSerializer.Serialize(new { status = "completed", output = new[] { new { content = new[] { new { type = "output_text", text = generated } } } } });
        var handler = new Handler(HttpStatusCode.OK, response);
        var result = await Service(handler).GenerateQuizAsync("Тест", "Материал", new GenerateQuiz(Guid.NewGuid(), 3, "Средняя", [QuestionType.SingleChoice], total), default);
        Assert.Equal(total, result.Questions.Sum(q => q.Points));
        Assert.All(result.Questions, q => {
            Assert.InRange(q.Points, 1, 100);
            Assert.Equal("Верно", Assert.Single(q.Options.Where(o => o.IsCorrect)).Text);
            Assert.Equal("Неверно", Assert.Single(q.Options.Where(o => !o.IsCorrect)).Text);
        });
        using var payload = JsonDocument.Parse(handler.RequestBody!);
        using var input = JsonDocument.Parse(payload.RootElement.GetProperty("input").GetString()!);
        Assert.Equal(total, input.RootElement.GetProperty("TotalPoints").GetInt32());
    }
    [Theory]
    [InlineData(2, "SingleChoice", true)]
    [InlineData(1, "MultipleChoice", true)]
    [InlineData(1, "SingleChoice", false)]
    public async Task RejectsIncorrectCountUnexpectedTypesAndMissingCorrectAnswers(int count, string type, bool correct)
    {
        var generated = new { title = "Тест", questions = new[] { new { type, text = "Вопрос", points = 1, modelAnswer = "", options = new[] { new { text = "A", isCorrect = correct }, new { text = "B", isCorrect = false } } } } };
        var response = JsonSerializer.Serialize(new { status = "completed", output = new[] { new { content = new[] { new { type = "output_text", text = JsonSerializer.Serialize(generated) } } } } });
        await Assert.ThrowsAsync<AiUnavailableException>(() => Service(new Handler(HttpStatusCode.OK, response)).GenerateQuizAsync("Тест", "Исходный материал", new GenerateQuiz(Guid.NewGuid(), count, "Средняя", [QuestionType.SingleChoice]), default));
    }
}
