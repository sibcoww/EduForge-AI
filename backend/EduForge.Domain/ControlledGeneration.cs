namespace EduForge.Domain;

public sealed class MaterialRevision
{
    public Guid Id { get; set; }
    public Guid MaterialId { get; set; }
    public string Blocks { get; set; } = "[]";
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public sealed class MaterialLearningPlan
{
    public Guid MaterialId { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public Guid ConfirmedRevision { get; set; }
    public string Objectives { get; set; } = "[]";
}
