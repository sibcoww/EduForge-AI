namespace EduForge.Domain;

public enum SessionStatus { Waiting, Active, Finished }

// An immutable source snapshot and attempts are stored together so every write competes
// with start/finish through the same optimistic concurrency token.
public sealed class TestingSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeacherId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string GroupName { get; set; } = "";
    public SessionStatus Status { get; set; }
    public int DurationMinutes { get; set; }
    public long CreatedAt { get; set; }
    public long? StartedAt { get; set; }
    public long? Deadline { get; set; }
    public long? FinishedAt { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string Payload { get; set; } = "";
}

public sealed class SessionSnapshot
{
    public List<SessionQuestion> Questions { get; set; } = [];
    public List<SessionParticipant> Participants { get; set; } = [];
}
public sealed class SessionQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public QuestionType Type { get; set; }
    public string Text { get; set; } = "";
    public int Points { get; set; }
    public string ModelAnswer { get; set; } = "";
    public List<SessionOption> Options { get; set; } = [];
}
public sealed class SessionOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
}
public sealed class SessionParticipant
{
    public Guid StudentId { get; set; }
    public string Name { get; set; } = "";
    public string? TokenHash { get; set; }
    public long? JoinedAt { get; set; }
    public long? SubmittedAt { get; set; }
    public Dictionary<Guid, SessionAnswer> Answers { get; set; } = [];
    public List<SessionActivity> Activity { get; set; } = [];
}
public sealed class SessionActivity
{
    public string Kind { get; set; } = "";
    public long At { get; set; }
}
public sealed class SessionAnswer
{
    public List<Guid> OptionIds { get; set; } = [];
    public string Text { get; set; } = "";
    public int? Score { get; set; }
    public string Feedback { get; set; } = "";
}
