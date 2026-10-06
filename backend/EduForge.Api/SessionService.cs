using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace EduForge.Api;

public sealed class SessionService(MaterialDbContext db, TimeProvider clock, IHubContext<SessionHub> hub)
{
    public long Now => clock.GetUtcNow().ToUnixTimeMilliseconds();
    public static SessionSnapshot Read(TestingSession session) => JsonSerializer.Deserialize<SessionSnapshot>(session.Payload)!;
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static SessionParticipant? Participant(SessionSnapshot snapshot, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        var hash = Hash(token);
        return snapshot.Participants.SingleOrDefault(p => p.TokenHash == hash);
    }
    public async Task<bool> Save(TestingSession session, SessionSnapshot snapshot, CancellationToken ct)
    {
        session.Payload = JsonSerializer.Serialize(snapshot); session.Revision = Guid.NewGuid();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return false; }
        // Broadcast only an invalidation, never answers, participant tokens or grades.
        await hub.Clients.Group(session.Id.ToString()).SendAsync("Changed", ct);
        return true;
    }
    public static void Grade(SessionSnapshot snapshot, SessionParticipant participant)
    {
        foreach (var q in snapshot.Questions)
        {
            if (!participant.Answers.TryGetValue(q.Id, out var answer)) participant.Answers[q.Id] = answer = new();
            if (q.Type is QuestionType.OpenText or QuestionType.ShortText)
                answer.Score = string.IsNullOrWhiteSpace(answer.Text) ? 0 : null;
            else answer.Score = q.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet().SetEquals(answer.OptionIds) ? q.Points : 0;
        }
    }
    public void Finish(TestingSession session, SessionSnapshot snapshot, long at)
    {
        session.Status = SessionStatus.Finished; session.FinishedAt = at;
        foreach (var p in snapshot.Participants.Where(p => p.JoinedAt.HasValue && !p.SubmittedAt.HasValue))
        { p.SubmittedAt = at; Grade(snapshot, p); }
    }
    public async Task<bool> Expire(TestingSession session, CancellationToken ct)
    {
        if (session.Status != SessionStatus.Active || session.Deadline > Now) return true;
        var snapshot = Read(session); Finish(session, snapshot, session.Deadline!.Value);
        return await Save(session, snapshot, ct);
    }
    public object StudentView(TestingSession session, SessionSnapshot snapshot, SessionParticipant p) => new {
        session.Id, session.Title, session.GroupName, session.Status, session.StartedAt, session.Deadline, ServerNow = Now,
        p.Name, p.SubmittedAt,
        Questions = session.Status == SessionStatus.Waiting ? null : snapshot.Questions.Select(q => new {
            q.Id, q.Type, q.Text, q.Points, Options = q.Options.Select(o => new { o.Id, o.Text })
        }).ToArray(),
        Answers = p.Answers.Select(a => new { QuestionId = a.Key, a.Value.OptionIds, a.Value.Text }),
        Score = p.SubmittedAt.HasValue ? p.Answers.Values.Sum(a => a.Score ?? 0) : (int?)null,
        PendingReview = p.SubmittedAt.HasValue ? p.Answers.Values.Count(a => a.Score == null) : 0,
        TotalPoints = snapshot.Questions.Sum(q => q.Points)
    };
    public object TeacherView(TestingSession session, SessionSnapshot snapshot) => new {
        session.Id, session.Code, session.Title, session.GroupName, session.Status, session.DurationMinutes,
        session.StartedAt, session.Deadline, session.FinishedAt, ServerNow = Now,
        TotalPoints = snapshot.Questions.Sum(q => q.Points), Questions = snapshot.Questions,
        Participants = snapshot.Participants.Select(p => new {
            p.StudentId, p.Name, p.JoinedAt, p.SubmittedAt,
            AnsweredCount = p.Answers.Values.Count(a => a.OptionIds.Count > 0 || !string.IsNullOrWhiteSpace(a.Text)),
            Score = p.SubmittedAt.HasValue ? p.Answers.Values.Sum(a => a.Score ?? 0) : (int?)null,
            PendingReview = p.SubmittedAt.HasValue ? p.Answers.Values.Count(a => a.Score == null) : 0,
            p.Activity,
            Answers = p.SubmittedAt.HasValue ? p.Answers.Select(a => new { QuestionId = a.Key, a.Value.OptionIds, a.Value.Text, a.Value.Score, a.Value.Feedback }).ToArray() : null
        })
    };
}

public sealed class SessionExpiryWorker(IServiceScopeFactory scopes, ILogger<SessionExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MaterialDbContext>();
                var service = scope.ServiceProvider.GetRequiredService<SessionService>();
                var expired = await db.Sessions.Where(s => s.Status == SessionStatus.Active && s.Deadline <= service.Now).Select(s => s.Id).ToListAsync(stoppingToken);
                foreach (var id in expired) {
                    db.ChangeTracker.Clear();
                    var session = await db.Sessions.SingleAsync(s => s.Id == id, stoppingToken);
                    await service.Expire(session, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Could not finish overdue test sessions"); }
        }
    }
}
