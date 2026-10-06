namespace EduForge.Domain;

public enum QuestionType { SingleChoice, MultipleChoice, TrueFalse, OpenText, ShortText }
public sealed class Quiz
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeacherId { get; set; }
    public Guid? MaterialId { get; set; }
    public string Title { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Question> Questions { get; set; } = [];
}
public sealed class Question
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuizId { get; set; }
    public int Position { get; set; }
    public QuestionType Type { get; set; }
    public string Text { get; set; } = "";
    public int Points { get; set; } = 1;
    public string ModelAnswer { get; set; } = "";
    public List<QuestionOption> Options { get; set; } = [];
}
public sealed class QuestionOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionId { get; set; }
    public int Position { get; set; }
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
}
public sealed class StudyGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeacherId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<Student> Students { get; set; } = [];
}
public sealed class Student
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
}
