namespace EduForge.Application;

public record MaterialTeachingContext(string Level, string Language, string Requirements);
public interface ILearningObjectivesAiService
{
    Task<List<string>> SuggestObjectivesAsync(string title, List<SourceBlock> blocks, MaterialTeachingContext context, CancellationToken ct);
}
