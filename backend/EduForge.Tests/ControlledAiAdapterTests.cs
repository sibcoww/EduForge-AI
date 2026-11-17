using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EduForge.Application;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EduForge.Tests;

public sealed class ControlledAiAdapterTests
{
    private sealed class Handler(string output) : HttpMessageHandler {
        public string? Body;
        public string? Authorization;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            Body = await request.Content!.ReadAsStringAsync(ct); Authorization = request.Headers.Authorization?.Parameter;
            var response = JsonSerializer.Serialize(new { status = "completed", output = new[] { new { content = new[] { new { type = "output_text", text = output } } } } });
            return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task ControlledAdapterSuppliesVerifiedBlocksAndGoalsAndNormalizesScores() {
        var source = new LearningContext(Guid.NewGuid(), Guid.NewGuid(), [new(Guid.NewGuid(), "paragraph", "for перебирает элементы")], Guid.NewGuid(), [new(Guid.NewGuid(), "Объяснить цикл")], false);
        var result = new SaveQuiz("Тест", source.MaterialId, [new(QuestionType.SingleChoice, "Что делает for?", 1, "Перебирает элементы", [new("Перебирает", true), new("Удаляет", false)],
            new(source.RevisionId, source.Objectives[0].Id, [new(source.Blocks[0].Id, source.Blocks[0].Text)], "В материале дано определение"))]);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web); json.Converters.Add(new JsonStringEnumConverter());
        var handler = new Handler(JsonSerializer.Serialize(result, json));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AI:ApiKey"] = "old-test-key", ["AI__ApiKey"] = "current-test-key", ["AI__Model"] = "configured-test-model" }).Build();
        var service = new OpenAiService(new HttpClient(handler), configuration);
        var generated = await service.GenerateControlledQuizAsync("Циклы", source, new(source.MaterialId, 1, "Средняя", [QuestionType.SingleChoice], 7,"Без сложных терминов",new("Начинающий","Русский","Для детей 6–10 лет")), default);
        Assert.True(generated.Controlled); Assert.Equal(7, generated.Questions.Single().Points);
        Assert.False(generated.Questions[0].Grounding!.Reviewed);
        Assert.Equal("current-test-key", handler.Authorization);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.True(body.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        Assert.Equal("grounded_teaching_test", body.RootElement.GetProperty("text").GetProperty("format").GetProperty("name").GetString());
        using var input = JsonDocument.Parse(body.RootElement.GetProperty("input").GetString()!);
        Assert.Equal("Для детей 6–10 лет",input.RootElement.GetProperty("Context").GetProperty("Requirements").GetString());
        Assert.Equal("Без сложных терминов",input.RootElement.GetProperty("Requirements").GetString());
        Assert.Equal(source.RevisionId, input.RootElement.GetProperty("RevisionId").GetGuid());
        Assert.Equal(source.Blocks[0].Text, input.RootElement.GetProperty("Blocks")[0].GetProperty("Text").GetString());
        Assert.Equal(source.Objectives[0].Id, input.RootElement.GetProperty("Objectives")[0].GetProperty("Id").GetGuid());
        Assert.DoesNotContain("current-test-key", handler.Body);
    }
}
