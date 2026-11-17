using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduForge.Application;
using EduForge.Domain;
using Xunit;

namespace EduForge.Tests;

public sealed class LearningSuggestionsTests
{
    private sealed class Ai : IAiService, IControlledAiService, ILearningObjectivesAiService {
        public MaterialTeachingContext? SuggestedContext;
        public GenerateQuiz? Ordinary, Controlled;
        public int Suggestions;
        public bool Duplicate;
        public Func<Task>? DuringSuggestion;
        public Task<string> GenerateMaterialAsync(GenerateMaterial request,CancellationToken ct)=>Task.FromResult("Класс — описание объектов. Объект — отдельный экземпляр класса. Атрибут хранит данные объекта.");
        public async Task<List<string>> SuggestObjectivesAsync(string title,List<SourceBlock> blocks,MaterialTeachingContext context,CancellationToken ct){
            Suggestions++;SuggestedContext=context;if(DuringSuggestion is not null)await DuringSuggestion();
            return Duplicate?["Назвать объект","Назвать объект"]:["Различать класс и объект","Находить атрибут объекта","Приводить простой пример объекта"];
        }
        static QuestionInput Question(int points,QuestionGrounding? grounding=null)=>new(QuestionType.SingleChoice,"Что хранит атрибут?",points,"Данные объекта",[new("Данные объекта",true),new("Ничего",false)],grounding);
        public Task<SaveQuiz> GenerateQuizAsync(string title,string text,GenerateQuiz input,CancellationToken ct){Ordinary=input;return Task.FromResult(new SaveQuiz(title,input.MaterialId,[Question(input.TotalPoints??1)]));}
        public Task<SaveQuiz> GenerateControlledQuizAsync(string title,LearningContext source,GenerateQuiz input,CancellationToken ct){Controlled=input;return Task.FromResult(new SaveQuiz(title,source.MaterialId,[Question(input.TotalPoints??1,new(source.RevisionId,source.Objectives[0].Id,[new(source.Blocks[0].Id,source.Blocks[0].Text)],"Атрибут хранит данные, как указано в материале."))],true));}
    }
    private static async Task Login(HttpClient client,string email){var r=await client.PostAsJsonAsync("/api/auth/register",new{email,name="Teacher",password="Correct horse battery staple!"});r.EnsureSuccessStatusCode();client.DefaultRequestHeaders.Add("Cookie",r.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);}
    private static async Task<Guid> Material(HttpClient client){var r=await client.PostAsJsonAsync("/api/ai/material",new{subject="Python",topic="Объекты",requirements="Для детей 6–10 лет. Простые слова и короткие примеры.",level="Начинающий",language="Русский"});r.EnsureSuccessStatusCode();var generated=await r.Content.ReadFromJsonAsync<JsonElement>();r=await client.PostAsJsonAsync("/api/materials",new{title="Объекты",subject="Python",content=generated.GetProperty("content").GetString(),generationRunId=generated.GetProperty("generationRunId").GetGuid()});r.EnsureSuccessStatusCode();return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();}
    [Fact]
    public async Task SuggestionsArePrivateDraftsAndAudienceReachesBothTestModes(){
        var ai=new Ai();using var factory=new ApiFactory(ai){ObjectivesAi=ai,ControlledAi=ai};using var alice=factory.Browser();using var bob=factory.Browser();await Login(alice,"goals-alice@example.test");await Login(bob,"goals-bob@example.test");var id=await Material(alice);
        Assert.Equal(HttpStatusCode.NotFound,(await bob.GetAsync($"/api/materials/{id}/teaching-context")).StatusCode);
        var source=(await alice.GetFromJsonAsync<LearningContext>($"/api/materials/{id}/learning"))!;
        Assert.Equal(HttpStatusCode.NotFound,(await bob.PostAsJsonAsync($"/api/materials/{id}/learning/suggest",new{source.RevisionId,source.PlanVersion})).StatusCode);
        var result=await alice.PostAsJsonAsync($"/api/materials/{id}/learning/suggest",new{source.RevisionId,source.PlanVersion});result.EnsureSuccessStatusCode();var proposed=await result.Content.ReadFromJsonAsync<JsonElement>();var goals=proposed.GetProperty("objectives").Deserialize<List<LearningObjective>>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(3,goals.Count);Assert.Equal(3,goals.Select(g=>g.Id).Distinct().Count());Assert.Contains("6–10",ai.SuggestedContext!.Requirements);
        var still=(await alice.GetFromJsonAsync<LearningContext>($"/api/materials/{id}/learning"))!;Assert.Empty(still.Objectives);Assert.Equal(source.PlanVersion,still.PlanVersion);
        Assert.Equal(HttpStatusCode.Conflict,(await alice.PostAsJsonAsync($"/api/materials/{id}/learning/suggest",new{revisionId=Guid.NewGuid(),source.PlanVersion})).StatusCode);Assert.Equal(1,ai.Suggestions);
        (await alice.PutAsJsonAsync($"/api/materials/{id}/learning",new{source.RevisionId,source.PlanVersion,Objectives=goals})).EnsureSuccessStatusCode();source=(await alice.GetFromJsonAsync<LearningContext>($"/api/materials/{id}/learning"))!;
        var quiz=new{materialId=id,questionCount=1,totalPoints=5,difficulty="Лёгкая",types=new[]{"SingleChoice"},requirements="Короткие вопросы",context=new{level="Продвинутый",language="English",requirements="Для взрослых"}};
        (await alice.PostAsJsonAsync("/api/ai/test",quiz)).EnsureSuccessStatusCode();(await alice.PostAsJsonAsync("/api/ai/test/controlled",new{quiz,source.RevisionId,source.PlanVersion})).EnsureSuccessStatusCode();
        foreach(var request in new[]{ai.Ordinary,ai.Controlled}){Assert.NotNull(request);Assert.Contains("6–10",request.Context!.Requirements);Assert.Equal("Русский",request.Context.Language);Assert.Equal("Начинающий",request.Context.Level);Assert.Equal("Короткие вопросы",request.Requirements);}
    }
    [Fact]
    public async Task BadSuggestionsDoNotOverwriteSavedGoals(){
        var ai=new Ai{Duplicate=true};using var factory=new ApiFactory(ai){ObjectivesAi=ai};using var client=factory.Browser();await Login(client,"goals-bad@example.test");var id=await Material(client);var source=(await client.GetFromJsonAsync<LearningContext>($"/api/materials/{id}/learning"))!;
        var original=new[]{new LearningObjective(Guid.NewGuid(),"Различать объект и класс")};(await client.PutAsJsonAsync($"/api/materials/{id}/learning",new{source.RevisionId,source.PlanVersion,Objectives=original})).EnsureSuccessStatusCode();source=(await client.GetFromJsonAsync<LearningContext>($"/api/materials/{id}/learning"))!;
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.PostAsJsonAsync($"/api/materials/{id}/learning/suggest",new{source.RevisionId,source.PlanVersion})).StatusCode);
        var after=(await client.GetFromJsonAsync<LearningContext>($"/api/materials/{id}/learning"))!;Assert.Equal(original[0],Assert.Single(after.Objectives));Assert.Equal(source.PlanVersion,after.PlanVersion);
    }
    [Fact]
    public async Task ChangedMaterialRejectsLateSuggestions(){
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ai=new Ai{DuringSuggestion=async()=>{entered.SetResult();await release.Task;}};using var factory=new ApiFactory(ai){ObjectivesAi=ai};using var client=factory.Browser();await Login(client,"goals-race@example.test");var id=await Material(client);var source=(await client.GetFromJsonAsync<LearningContext>($"/api/materials/{id}/learning"))!;
        var pending=client.PostAsJsonAsync($"/api/materials/{id}/learning/suggest",new{source.RevisionId,source.PlanVersion});await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try{(await client.PutAsJsonAsync($"/api/materials/{id}",new{title="Другой урок",subject="Python",content="Другой текст материала"})).EnsureSuccessStatusCode();}finally{release.TrySetResult();}
        Assert.Equal(HttpStatusCode.Conflict,(await pending).StatusCode);Assert.Empty((await client.GetFromJsonAsync<LearningContext>($"/api/materials/{id}/learning"))!.Objectives);
    }
}
