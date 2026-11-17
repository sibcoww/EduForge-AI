namespace EduForge.Domain;

public sealed class SourceDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeacherId { get; set; }
    public string Name { get; set; } = "";
    public string Format { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Sections { get; set; } = "[]";
    public int CharacterCount { get; set; }
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public sealed class GenerationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeacherId { get; set; }
    public string Kind { get; set; } = "";
    public string TemplateVersion { get; set; } = "";
    public string Model { get; set; } = "";
    public string Status { get; set; } = "Running";
    public long StartedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long? FinishedAt { get; set; }
    public long? DurationMs { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public string Parameters { get; set; } = "{}";
    public string SourceSnapshot { get; set; } = "[]";
    public string Result { get; set; } = "";
    public string Failure { get; set; } = "";
    public string Experiment { get; set; } = "";
    public decimal? PreparationMinutes { get; set; }
    public int? Correctness { get; set; }
    public int? Clarity { get; set; }
    public int? Alignment { get; set; }
    public int? CorrectedQuestions { get; set; }
    public string EvaluationNote { get; set; } = "";
}
