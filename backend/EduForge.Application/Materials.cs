using EduForge.Domain;
namespace EduForge.Application;
public record SaveMaterial(string Title, string Subject, string Content, string Level = "Начинающий", string Language = "Русский");
public record GenerateMaterial(string Subject, string Topic, string Requirements, string Level = "Начинающий", string Language = "Русский");
public interface IMaterialRepository
{
    Task<List<Material>> ListAsync(Guid teacherId, CancellationToken ct);
    Task<Material?> GetAsync(Guid id, Guid teacherId, CancellationToken ct);
    Task SaveAsync(Material material, CancellationToken ct);
}
public interface IAiService
{
    Task<string> GenerateMaterialAsync(GenerateMaterial request, CancellationToken ct);
    Task<SaveQuiz> GenerateQuizAsync(string title, string materialText, GenerateQuiz request, CancellationToken ct);
}
