using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EduForge.Api;
public record CreateSession(Guid QuizId, Guid GroupId, int DurationMinutes);
public record JoinSession(Guid StudentId);
public record SaveSessionAnswer(List<Guid> OptionIds, string Text);
public record ReviewSessionAnswer(int Score, string Feedback);
public record LogSessionActivity(string Kind);

public static class SessionEndpoints
{
    static Guid Owner(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    static IResult Conflict() => Results.Conflict(new { message = "Сессия изменилась. Обновите состояние и повторите действие." });
    static string? Token(HttpRequest request) => request.Headers["X-Attempt-Token"].FirstOrDefault();
    public static void MapSessions(this WebApplication app)
    {
        var teacher = app.MapGroup("/api/sessions").RequireAuthorization();
        teacher.MapGet("/", async (ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => Results.Ok(await db.Sessions.AsNoTracking().Where(s => s.TeacherId == Owner(user)).OrderByDescending(s => s.CreatedAt).Select(s => new { s.Id, s.Code, s.Title, s.GroupName, s.Status, s.CreatedAt }).ToListAsync(ct)));
        teacher.MapPost("/", async (CreateSession input, ClaimsPrincipal user, MaterialDbContext db, SessionService service, CancellationToken ct) => {
            if (input.DurationMinutes is < 1 or > 180) return Results.BadRequest(new { message = "Продолжительность: от 1 до 180 минут." });
            var quiz = await db.Quizzes.Include(q => q.Questions).ThenInclude(q => q.Options).SingleOrDefaultAsync(q => q.Id == input.QuizId && q.TeacherId == Owner(user), ct);
            var group = await db.Groups.Include(g => g.Students).SingleOrDefaultAsync(g => g.Id == input.GroupId && g.TeacherId == Owner(user), ct);
            if (quiz is null || group is null) return Results.NotFound();
            if (quiz.Questions.Count == 0 || group.Students.Count is < 1 or > 200) return Results.BadRequest(new { message = "Нужен тест с вопросами и группа от 1 до 200 студентов." });
            var snapshot = new SessionSnapshot {
                Questions = quiz.Questions.OrderBy(q => q.Position).Select(q => new SessionQuestion {
                    Type = q.Type, Text = q.Text, Points = q.Points, ModelAnswer = q.ModelAnswer,
                    Options = q.Options.OrderBy(o => o.Position).Select(o => new SessionOption { Text = o.Text, IsCorrect = o.IsCorrect }).ToList()
                }).ToList(),
                Participants = group.Students.OrderBy(p => p.LastName).ThenBy(p => p.FirstName).Select(p => new SessionParticipant { StudentId = p.Id, Name = $"{p.LastName} {p.FirstName}" }).ToList()
            };
            string code;
            do { const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; code = new string(Enumerable.Range(0, 8).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray()); }
            while (await db.Sessions.AnyAsync(s => s.Code == code, ct));
            var session = new TestingSession { TeacherId = Owner(user), Code = code, Title = quiz.Title, GroupName = group.Name, DurationMinutes = input.DurationMinutes, CreatedAt = service.Now, Payload = JsonSerializer.Serialize(snapshot) };
            db.Sessions.Add(session); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/sessions/{session.Id}", service.TeacherView(session, snapshot));
        });
        teacher.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, SessionService service, CancellationToken ct) => {
            var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == id && s.TeacherId == Owner(user), ct);
            if (session is null) return Results.NotFound();
            if (!await service.Expire(session, ct)) return Conflict();
            return Results.Ok(service.TeacherView(session, SessionService.Read(session)));
        });
        teacher.MapPost("/{id:guid}/start", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, SessionService service, CancellationToken ct) => {
            var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == id && s.TeacherId == Owner(user), ct);
            if (session is null) return Results.NotFound();
            if (session.Status != SessionStatus.Waiting) return Conflict();
            session.Status = SessionStatus.Active; session.StartedAt = service.Now; session.Deadline = session.StartedAt + session.DurationMinutes * 60000L;
            var snapshot = SessionService.Read(session);
            return await service.Save(session, snapshot, ct) ? Results.Ok(service.TeacherView(session, snapshot)) : Conflict();
        });
        teacher.MapPost("/{id:guid}/finish", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, SessionService service, CancellationToken ct) => {
            var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == id && s.TeacherId == Owner(user), ct);
            if (session is null) return Results.NotFound();
            if (!await service.Expire(session, ct)) return Conflict();
            var snapshot = SessionService.Read(session);
            if (session.Status != SessionStatus.Finished) {
                service.Finish(session, snapshot, service.Now);
                if (!await service.Save(session, snapshot, ct)) return Conflict();
            }
            return Results.Ok(service.TeacherView(session, snapshot));
        });
        teacher.MapPut("/{id:guid}/review/{studentId:guid}/{questionId:guid}", async (Guid id, Guid studentId, Guid questionId, ReviewSessionAnswer input, ClaimsPrincipal user, MaterialDbContext db, SessionService service, CancellationToken ct) => {
            var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == id && s.TeacherId == Owner(user), ct);
            if (session is null) return Results.NotFound();
            if (!await service.Expire(session, ct)) return Conflict();
            var snapshot = SessionService.Read(session); var p = snapshot.Participants.SingleOrDefault(p => p.StudentId == studentId);
            var q = snapshot.Questions.SingleOrDefault(q => q.Id == questionId);
            if (p is null || q is null) return Results.NotFound();
            if (session.Status != SessionStatus.Finished || !p.SubmittedAt.HasValue || q.Type is not (QuestionType.OpenText or QuestionType.ShortText)) return Conflict();
            if (input.Score < 0 || input.Score > q.Points || input.Feedback is null || input.Feedback.Length > 2000) return Results.BadRequest(new { message = "Балл должен находиться в пределах баллов вопроса; комментарий — до 2000 символов." });
            var a = p.Answers[q.Id]; a.Score = input.Score; a.Feedback = input.Feedback.Trim();
            return await service.Save(session, snapshot, ct) ? Results.Ok(service.TeacherView(session, snapshot)) : Conflict();
        });

        var student = app.MapGroup("/api/join").RequireRateLimiting("join");
        student.MapGet("/{code}", async (string code, MaterialDbContext db, SessionService service, CancellationToken ct) => {
            var session = await db.Sessions.SingleOrDefaultAsync(s => s.Code == code.ToUpperInvariant(), ct);
            if (session is null) return Results.NotFound();
            if (!await service.Expire(session, ct)) return Conflict();
            if (session.Status == SessionStatus.Finished) return Results.BadRequest(new { message = "Тестирование завершено." });
            return Results.Ok(new { session.Title, session.GroupName, session.Status, Students = SessionService.Read(session).Participants.Select(p => new { p.StudentId, p.Name, Joined = p.JoinedAt.HasValue }) });
        });
        student.MapPost("/{code}", async (string code, JoinSession input, MaterialDbContext db, SessionService service, CancellationToken ct) => {
            var session = await db.Sessions.SingleOrDefaultAsync(s => s.Code == code.ToUpperInvariant(), ct);
            if (session is null) return Results.NotFound();
            if (!await service.Expire(session, ct)) return Conflict();
            if (session.Status == SessionStatus.Finished) return Conflict();
            var snapshot = SessionService.Read(session); var p = snapshot.Participants.SingleOrDefault(p => p.StudentId == input.StudentId);
            if (p is null) return Results.NotFound();
            if (p.JoinedAt.HasValue) return Results.Conflict(new { message = "Этот студент уже присоединился. Продолжите на устройстве, где открыли тест." });
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); p.TokenHash = SessionService.Hash(token); p.JoinedAt = service.Now;
            return await service.Save(session, snapshot, ct) ? Results.Ok(new { token, sessionId = session.Id }) : Conflict();
        });
        var attempt = app.MapGroup("/api/attempts");
        attempt.MapPost("/{id:guid}/activity", async (Guid id, LogSessionActivity input, HttpRequest request, MaterialDbContext db, SessionService service, CancellationToken ct) => {
            if (input.Kind is not ("Hidden" or "Visible" or "Blur" or "Focus" or "FullscreenEntered" or "FullscreenExited")) return Results.BadRequest();
            var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == id, ct);
            if (session is null) return Results.NotFound();
            var snapshot = SessionService.Read(session); var p = SessionService.Participant(snapshot, Token(request));
            if (p is null) return Results.Unauthorized();
            if (!await service.Expire(session, ct)) return Conflict();
            if (session.Status != SessionStatus.Active || p.SubmittedAt.HasValue) return Results.NoContent();
            if (p.Activity.Count >= 200 || p.Activity.LastOrDefault() is { } last && last.Kind == input.Kind && service.Now - last.At < 1000) return Results.NoContent();
            p.Activity.Add(new SessionActivity { Kind = input.Kind, At = service.Now });
            return await service.Save(session, snapshot, ct) ? Results.NoContent() : Conflict();
        });
        attempt.MapGet("/{id:guid}", async (Guid id, HttpRequest request, MaterialDbContext db, SessionService service, CancellationToken ct) => {
            var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == id, ct);
            if (session is null) return Results.NotFound();
            if (SessionService.Participant(SessionService.Read(session), Token(request)) is null) return Results.Unauthorized();
            if (!await service.Expire(session, ct)) return Conflict();
            var snapshot = SessionService.Read(session); return Results.Ok(service.StudentView(session, snapshot, SessionService.Participant(snapshot, Token(request))!));
        });
        attempt.MapPut("/{id:guid}/answers/{questionId:guid}", async (Guid id, Guid questionId, SaveSessionAnswer input, HttpRequest request, MaterialDbContext db, SessionService service, CancellationToken ct) => {
            var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == id, ct);
            if (session is null) return Results.NotFound();
            var snapshot = SessionService.Read(session); var p = SessionService.Participant(snapshot, Token(request));
            if (p is null) return Results.Unauthorized();
            if (!await service.Expire(session, ct)) return Conflict();
            if (session.Status != SessionStatus.Active || p.SubmittedAt.HasValue) return Results.Conflict(new { message = "Приём ответов закрыт." });
            var q = snapshot.Questions.SingleOrDefault(q => q.Id == questionId);
            if (q is null) return Results.NotFound();
            if (input.OptionIds is null || input.Text is null || input.Text.Length > 5000 || input.OptionIds.Count > 10 || input.OptionIds.Distinct().Count() != input.OptionIds.Count || input.OptionIds.Any(id => q.Options.All(o => o.Id != id))) return Results.BadRequest();
            if (q.Type is QuestionType.OpenText or QuestionType.ShortText ? input.OptionIds.Count != 0 : input.Text.Length != 0 || (q.Type != QuestionType.MultipleChoice && input.OptionIds.Count > 1)) return Results.BadRequest();
            p.Answers[q.Id] = new SessionAnswer { OptionIds = input.OptionIds, Text = input.Text };
            return await service.Save(session, snapshot, ct) ? Results.NoContent() : Conflict();
        });
        attempt.MapPost("/{id:guid}/submit", async (Guid id, HttpRequest request, MaterialDbContext db, SessionService service, CancellationToken ct) => {
            var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == id, ct);
            if (session is null) return Results.NotFound();
            if (SessionService.Participant(SessionService.Read(session), Token(request)) is null) return Results.Unauthorized();
            if (!await service.Expire(session, ct)) return Conflict();
            var snapshot = SessionService.Read(session); var p = SessionService.Participant(snapshot, Token(request))!;
            if (session.Status == SessionStatus.Waiting) return Conflict();
            if (!p.SubmittedAt.HasValue) { p.SubmittedAt = service.Now; SessionService.Grade(snapshot, p); if (!await service.Save(session, snapshot, ct)) return Conflict(); }
            return Results.Ok(service.StudentView(session, snapshot, p));
        });
        app.MapHub<SessionHub>("/api/hubs/sessions");
    }
}
