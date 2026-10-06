namespace EduForge.Domain;
public sealed class Material
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeacherId { get; set; }
    public string Level { get; set; } = "Начинающий";
    public string Language { get; set; } = "Русский";
    public string Title { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
