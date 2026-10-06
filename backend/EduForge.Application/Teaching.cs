using System.Text.Json;
using EduForge.Domain;

namespace EduForge.Application;
public record OptionInput(string Text, bool IsCorrect);
public record QuestionInput(QuestionType Type, string Text, int Points, string ModelAnswer, List<OptionInput> Options);
public record SaveQuiz(string Title, Guid? MaterialId, List<QuestionInput> Questions);
public record GenerateQuiz(Guid MaterialId, int QuestionCount, string Difficulty, List<QuestionType> Types, int? TotalPoints = null);
public record SaveGroup(string Name, string Description);
public record SaveStudent(string FirstName, string LastName);

public static class TeachingValidation
{
    public static Dictionary<string, string[]>? Quiz(SaveQuiz request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Text(request.Title, 200)) errors["title"] = ["Название теста: от 1 до 200 символов."];
        if (request.Questions is null || request.Questions.Count is < 1 or > 100) {
            errors["questions"] = ["Добавьте от 1 до 100 вопросов."]; return errors;
        }
        for (var index = 0; index < request.Questions.Count; index++) {
            var q = request.Questions[index];
            var key = $"questions[{index}]";
            if (q is null || !Enum.IsDefined(q.Type) || !Text(q.Text, 4000) || q.Points is < 1 or > 100 || q.ModelAnswer is null || q.ModelAnswer.Length > 5000 || q.Options is null) {
                errors[key] = [$"Вопрос {index + 1}: укажите текст, корректный тип и от 1 до 100 баллов."]; continue;
            }
            if (q.Type is QuestionType.OpenText or QuestionType.ShortText) {
                if (q.Options.Count != 0 || (q.Type == QuestionType.ShortText && !Text(q.ModelAnswer, 5000)))
                    errors[key] = [$"Вопрос {index + 1}: текстовый вопрос не содержит вариантов; короткий ответ требует эталона."];
                continue;
            }
            if (q.Options.Count is < 2 or > 10 || q.Options.Any(x => x is null || !Text(x.Text, 1000))) {
                errors[key] = [$"Вопрос {index + 1}: нужны от 2 до 10 непустых вариантов ответа."]; continue;
            }
            var correct = q.Options.Count(x => x.IsCorrect);
            if (correct < 1 || (q.Type != QuestionType.MultipleChoice && correct != 1)) errors[key] = [$"Вопрос {index + 1}: отметьте правильные ответы (один для одиночного выбора)."];
            if (q.Options.Select(x => x.Text.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != q.Options.Count) errors[key] = [$"Вопрос {index + 1}: варианты не должны повторяться."];
            if (q.Type == QuestionType.TrueFalse && (q.Options.Count != 2 || q.Options[0].Text.Trim() != "Да" || q.Options[1].Text.Trim() != "Нет")) errors[key] = [$"Вопрос {index + 1}: варианты Да / Нет."];
        }
        return errors.Count == 0 ? null : errors;
    }
    public static bool Text(string? text, int max) => !string.IsNullOrWhiteSpace(text) && text.Length <= max;
    public static bool Group(SaveGroup request) => Text(request.Name, 100) && request.Description is not null && request.Description.Length <= 2000;
    public static bool Student(SaveStudent request) => Text(request.FirstName, 100) && Text(request.LastName, 100);
}
public static class MaterialText
{
    public static string Extract(string content)
    {
        try {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "doc") return content;
            var fragments = new List<string>();
            Read(doc.RootElement, fragments);
            return string.Join("", fragments).Trim();
        } catch (JsonException) { return content; }
    }
    static void Read(JsonElement node, List<string> fragments)
    {
        if (node.ValueKind != JsonValueKind.Object) return;
        if (node.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) fragments.Add(text.GetString()!);
        if (node.TryGetProperty("content", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray()) Read(child, fragments);
        if (node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() is "paragraph" or "heading" or "codeBlock" or "hardBreak" or "tableCell") fragments.Add("\n");
    }
}
