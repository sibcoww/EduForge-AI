using System.Text.RegularExpressions;

namespace EduForge.Application;

public record QuestionWarning(int Index, string Code, string Message);
public static partial class QuestionQuality
{
    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex Separators();
    private static string Normalize(string? value) => Separators().Replace((value ?? "").ToLowerInvariant(), " ").Trim();
    private static double Similarity(string a, string b) {
        var x = Normalize(a).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var y = Normalize(b).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var union = x.Union(y).Count(); return union == 0 ? 0 : (double)x.Intersect(y).Count() / union;
    }
    public static List<QuestionWarning> Analyze(List<QuestionInput> questions)
    {
        var warnings = new List<QuestionWarning>();
        for (var i = 0; i < questions.Count; i++) {
            var q = questions[i];
            if (string.IsNullOrWhiteSpace(q.Text)) continue;
            for (var j = 0; j < i; j++) {
                var exact = Normalize(q.Text) == Normalize(questions[j].Text);
                if (exact || q.Text.Length >= 25 && Similarity(q.Text, questions[j].Text) >= .85)
                    warnings.Add(new(i, exact ? "DuplicateQuestion" : "SimilarQuestion", $"Вопрос похож на вопрос {j + 1}. Проверьте, не дублируют ли они одну проверку."));
            }
            for (var a = 0; a < q.Options.Count; a++) for (var b = 0; b < a; b++) {
                if (Normalize(q.Options[a].Text) == Normalize(q.Options[b].Text) && Normalize(q.Options[a].Text).Length > 0 ||
                    q.Options[a].Text.Length >= 20 && q.Options[b].Text.Length >= 20 && Similarity(q.Options[a].Text, q.Options[b].Text) >= .85)
                    warnings.Add(new(i, "SimilarOptions", $"Варианты {b + 1} и {a + 1} очень похожи. Проверьте однозначность ответа."));
            }
            if (q.Type is Domain.QuestionType.OpenText or Domain.QuestionType.ShortText && string.IsNullOrWhiteSpace(q.ModelAnswer))
                warnings.Add(new(i, "MissingReferenceAnswer", "Нет эталонного ответа: добавьте его для воспроизводимой проверки."));
            if (q.Type == Domain.QuestionType.MultipleChoice && q.Options.Count > 1 && q.Options.All(o => o.IsCorrect))
                warnings.Add(new(i, "AllOptionsCorrect", "Все варианты отмечены правильными. Убедитесь, что это соответствует замыслу задания."));
        }
        return warnings;
    }
}
