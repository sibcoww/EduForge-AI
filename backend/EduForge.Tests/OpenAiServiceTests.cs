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
    public async Task RemovesBoldBeforeValidatingTrueFalseOptions()
    {
        var text=JsonSerializer.Serialize(new{title="**Тест**",questions=new[]{new{type="TrueFalse",text="Это **объект**?",points=1,modelAnswer="**Да**",options=new[]{new{text="**Да**",isCorrect=true},new{text="**Нет**",isCorrect=false}}}}});
        var response=JsonSerializer.Serialize(new{status="completed",output=new[]{new{content=new[]{new{type="output_text",text}}}}});
        var result=await Service(new Handler(HttpStatusCode.OK,response)).GenerateQuizAsync("Тест","Объект",new(Guid.NewGuid(),1,"Лёгкая",[QuestionType.TrueFalse],1),default);
        Assert.Equal("Тест",result.Title);Assert.Equal("Это объект?",result.Questions[0].Text);Assert.Equal("Да",result.Questions[0].Options[0].Text);Assert.Equal("Нет",result.Questions[0].Options[1].Text);
    }
    [Fact]
    public async Task RejectsOptionsThatBecomeDuplicatesAfterMarkdownCleanup()
    {
        var text=JsonSerializer.Serialize(new{title="Тест",questions=new[]{new{type="SingleChoice",text="Вопрос",points=1,modelAnswer="Да",options=new[]{new{text="**Да**",isCorrect=true},new{text="Да",isCorrect=false}}}}});
        var response=JsonSerializer.Serialize(new{status="completed",output=new[]{new{content=new[]{new{type="output_text",text}}}}});
        await Assert.ThrowsAsync<AiUnavailableException>(()=>Service(new Handler(HttpStatusCode.OK,response)).GenerateQuizAsync("Тест","Материал",new(Guid.NewGuid(),1,"Лёгкая",[QuestionType.SingleChoice],1),default));
    }
    [Fact]
    public async Task ObjectivesUseMaterialBlocksAudienceAndStrictSchema()
    {
        var text=JsonSerializer.Serialize(new{objectives=new[]{"Различать класс и объект","Находить атрибут объекта"}});
        var response=JsonSerializer.Serialize(new{status="completed",output=new[]{new{content=new[]{new{type="output_text",text}}}}});
        var handler=new Handler(HttpStatusCode.OK,response);var context=new MaterialTeachingContext("Начинающий","Русский","Для детей 6–10 лет");
        var goals=await Service(handler).SuggestObjectivesAsync("Объекты",[new(Guid.NewGuid(),"paragraph","Объект — экземпляр класса")],context,default);
        Assert.Equal(2,goals.Count);using var payload=JsonDocument.Parse(handler.RequestBody!);Assert.True(payload.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        using var input=JsonDocument.Parse(payload.RootElement.GetProperty("input").GetString()!);Assert.Equal(context.Requirements,input.RootElement.GetProperty("context").GetProperty("Requirements").GetString());Assert.Equal("Объект — экземпляр класса",input.RootElement.GetProperty("blocks")[0].GetProperty("Text").GetString());
    }
    [Theory]
    [InlineData("{\"objectives\":[]}")]
    [InlineData("{\"objectives\":[\"Назвать объект\",\"Назвать объект\"]}")]
    public async Task RejectsInvalidObjectives(string text)
    {
        var response=JsonSerializer.Serialize(new{status="completed",output=new[]{new{content=new[]{new{type="output_text",text}}}}});
        await Assert.ThrowsAsync<AiUnavailableException>(()=>Service(new Handler(HttpStatusCode.OK,response)).SuggestObjectivesAsync("Тема",[],new("Начинающий","Русский",""),default));
    }
    [Fact]
    public async Task CaptionSendsImageAndNumberedAnnotationsUsingConfiguredModel()
    {
        var handler = new Handler(HttpStatusCode.OK, """{"status":"completed","output":[{"content":[{"type":"output_text","text":"1. В красном прямоугольнике выделен инструмент."}]}]}""");
        var result = await Service(handler).GenerateCaptionAsync([137,80,78,71], "image/png", new("/api/media/unused", "SketchUp", [new(1,"rect","#dc2626","Прямоугольник")]), default);
        Assert.Contains("красном", result);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("configured-test-model", body.RootElement.GetProperty("model").GetString());
        var parts = body.RootElement.GetProperty("input")[0].GetProperty("content");
        Assert.Equal("input_image", parts[1].GetProperty("type").GetString());
        Assert.StartsWith("data:image/png;base64,", parts[1].GetProperty("image_url").GetString());
        using var notes = JsonDocument.Parse(parts[0].GetProperty("text").GetString()!);
        Assert.Equal(1, notes.RootElement.GetProperty("Annotations")[0].GetProperty("Number").GetInt32());
        Assert.Equal("#dc2626", notes.RootElement.GetProperty("Annotations")[0].GetProperty("Color").GetString());
        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
    }
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
    public async Task ExpiredKeyIsReportedWithoutReturningProviderMessageOrSecret()
    {
        var handler = new Handler(HttpStatusCode.Unauthorized, """{"error":{"code":"expired_secret_key","message":"secret-provider-message"}}""");
        var service = Service(handler);
        var error = await Assert.ThrowsAsync<AiUnavailableException>(() => service.GenerateMaterialAsync(new("Python", "Циклы", ""), default));
        Assert.Contains("истёк", error.Message);
        Assert.Equal("expired_secret_key", service.LastReceipt!.ErrorCode);
        Assert.DoesNotContain("secret-provider-message", error.Message);
    }
    [Fact]
    public async Task ReceiptReadsActualModelAndUsageAndAcceptsMissingCounts()
    {
        var handler = new Handler(HttpStatusCode.OK, """{"model":"resolved-model","usage":{"input_tokens":42,"output_tokens":null},"status":"completed","output":[{"content":[{"type":"output_text","text":"Урок"}]}]}""");
        var service = Service(handler);
        Assert.Equal("Урок", await service.GenerateMaterialAsync(new("Python", "Циклы", ""), default));
        Assert.Equal("resolved-model", service.LastReceipt!.Model); Assert.Equal(42, service.LastReceipt.InputTokens); Assert.Null(service.LastReceipt.OutputTokens);
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
        var result = await Service(handler).GenerateQuizAsync("Тест", "Материал", new GenerateQuiz(Guid.NewGuid(), 3, "Средняя", [QuestionType.SingleChoice], total, "Короткие вопросы",new("Начинающий","Русский","Для детей 6–10 лет")), default);
        Assert.Equal(total, result.Questions.Sum(q => q.Points));
        Assert.All(result.Questions, q => {
            Assert.InRange(q.Points, 1, 100);
            Assert.Equal("Верно", Assert.Single(q.Options.Where(o => o.IsCorrect)).Text);
            Assert.Equal("Неверно", Assert.Single(q.Options.Where(o => !o.IsCorrect)).Text);
        });
        using var payload = JsonDocument.Parse(handler.RequestBody!);
        using var input = JsonDocument.Parse(payload.RootElement.GetProperty("input").GetString()!);
        Assert.Equal(total, input.RootElement.GetProperty("TotalPoints").GetInt32());
        Assert.Equal("Для детей 6–10 лет",input.RootElement.GetProperty("Context").GetProperty("Requirements").GetString());
        Assert.Equal("Короткие вопросы",input.RootElement.GetProperty("Requirements").GetString());
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
