namespace EduForge.Application;

public record SourceSection(Guid Id, string Label, string Text);
public record SourceSelection(Guid DocumentId, List<Guid> SectionIds);
public record SourceExcerpt(Guid DocumentId, Guid SectionId, string DocumentName, string Label, string Text);
public record SourceCitation(Guid DocumentId, Guid SectionId, string Quote);
public record GeneratedLessonBlock(string Kind, string Text, string Language, List<SourceCitation> Evidence, Guid Id = default);
public record GeneratedSourceLesson(List<GeneratedLessonBlock> Blocks);
public record SourceMaterialRequest(GenerateMaterial Material, List<SourceSelection> Sources);
public interface ISourceMaterialAiService
{
    Task<GeneratedSourceLesson> GenerateFromSourcesAsync(GenerateMaterial input, List<SourceExcerpt> sources, CancellationToken ct);
}
public record AiReceipt(string? Model, int? InputTokens, int? OutputTokens, string? ErrorCode = null);
public interface IAiDiagnostics { AiReceipt? LastReceipt { get; } }
