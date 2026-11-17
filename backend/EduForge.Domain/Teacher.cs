namespace EduForge.Domain;
public sealed class Teacher
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string Name { get; set; } = "";
    public string Bio { get; set; } = "";
    public int? Age { get; set; }
    public string Country { get; set; } = "";
    public string City { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public string Disciplines { get; set; } = "[]";
    public string? PasswordHash { get; set; }
    public string? GoogleId { get; set; }
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();
}
