using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EduForge.Application;

public record SourceBlock(Guid Id, string Kind, string Text);
public record LearningObjective(Guid Id, string Text);
public record QuestionEvidence(Guid BlockId, string Quote);
public record QuestionGrounding(Guid RevisionId, Guid ObjectiveId, List<QuestionEvidence> Evidence, string Explanation, bool Reviewed = false, string ObjectiveText = "");
public record LearningContext(Guid MaterialId, Guid RevisionId, List<SourceBlock> Blocks, Guid PlanVersion,
    List<LearningObjective> Objectives, bool PlanNeedsReview);
public record ControlledQuizRequest(GenerateQuiz Quiz, Guid RevisionId, Guid PlanVersion, List<Guid>? ObjectiveIds = null);
public interface IControlledAiService
{
    Task<SaveQuiz> GenerateControlledQuizAsync(string title, LearningContext source, GenerateQuiz input, CancellationToken ct);
}

public static partial class MaterialStructure
{
    public static Guid Key(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
    public static string NormalizeText(string value) => Spaces().Replace(value, " ").Trim();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    public static List<SourceBlock> Read(Guid materialId, string content)
    {
        try {
            var doc = JsonNode.Parse(content);
            if (doc?["type"]?.GetValue<string>() == "doc" && doc["content"] is JsonArray nodes) {
                var blocks = new List<SourceBlock>();
                var used = new HashSet<Guid>();
                for (var i = 0; i < nodes.Count; i++) {
                    var node = nodes[i];
                    if (node is not JsonObject) continue;
                    var id = Guid.TryParse(node["attrs"]?["blockId"]?.ToString(), out var supplied) && supplied != Guid.Empty
                        ? supplied : Key($"{materialId}/legacy/{i}");
                    if (!used.Add(id)) { id = Key($"{materialId}/duplicate/{i}"); used.Add(id); }
                    var text = Text(node);
                    if (!string.IsNullOrWhiteSpace(text)) blocks.Add(new(id, node["type"]?.ToString() ?? "paragraph", text));
                }
                return blocks;
            }
        } catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        return content.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select((text, i) => new SourceBlock(Key($"{materialId}/legacy/{i}"), "paragraph", text.Trim()))
            .Where(x => x.Text.Length > 0).ToList();
    }

    // Preserve editor IDs; legacy documents are matched by text before position.
    public static string Normalize(Guid materialId, string content, string previous)
    {
        try {
            var doc = JsonNode.Parse(content);
            if (doc?["type"]?.GetValue<string>() != "doc" || doc["content"] is not JsonArray nodes) return content;
            var prior = Read(materialId, previous);
            var used = new HashSet<Guid>();
            foreach (var node in nodes.OfType<JsonObject>())
                if (Guid.TryParse(node["attrs"]?["blockId"]?.ToString(), out var id) && id != Guid.Empty) used.Add(id);
            var assigned = new HashSet<Guid>();
            for (var i = 0; i < nodes.Count; i++) {
                if (nodes[i] is not JsonObject node) continue;
                var supplied = Guid.TryParse(node["attrs"]?["blockId"]?.ToString(), out var id) && id != Guid.Empty && assigned.Add(id);
                if (!supplied) {
                    var match = prior.FirstOrDefault(b => !used.Contains(b.Id) && NormalizeText(b.Text) == NormalizeText(Text(node)));
                    if (match is null && i < prior.Count && !used.Contains(prior[i].Id)) match = prior[i];
                    id = match?.Id ?? Guid.NewGuid();
                    used.Add(id); assigned.Add(id);
                }
                if (node["attrs"] is not JsonObject) node["attrs"] = new JsonObject();
                node["attrs"]!["blockId"] = id.ToString();
            }
            return doc.ToJsonString();
        } catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return content; }
    }

    private static string Text(JsonNode? node)
    {
        if (node is not JsonObject) return "";
        if (node["text"] is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        if (node["type"]?.ToString() == "image") return node["attrs"]?["caption"]?.ToString() ?? node["attrs"]?["alt"]?.ToString() ?? "";
        var children = node["content"] as JsonArray;
        if (children is null) return "";
        var separator = node["type"]?.ToString() is "paragraph" or "heading" or "codeBlock" ? "" : "\n";
        return string.Join(separator, children.Select(Text));
    }

    public static Guid Revision(Guid materialId, List<SourceBlock> blocks) => Key(materialId + JsonSerializer.Serialize(
        blocks.Select(b => new { b.Id, b.Kind, Text = b.Text.Replace("\r\n", "\n") })));
}
