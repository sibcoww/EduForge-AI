using System.Security.Claims;
using EduForge.Application;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EduForge.Api;
public static class TeachingEndpoints
{
    static Guid Owner(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    static IQueryable<Quiz> OwnedQuizzes(MaterialDbContext db, ClaimsPrincipal user) => db.Quizzes.Where(x => x.TeacherId == Owner(user));
    static IQueryable<StudyGroup> OwnedGroups(MaterialDbContext db, ClaimsPrincipal user) => db.Groups.Where(x => x.TeacherId == Owner(user));
    public static void MapTeaching(this WebApplication app)
    {
        var quizzes = app.MapGroup("/api/tests").RequireAuthorization();
        quizzes.MapGet("/", async (Guid? materialId, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var query = OwnedQuizzes(db, user).AsNoTracking();
            if (materialId is not null) query = query.Where(x => x.MaterialId == materialId);
            return Results.Ok(await query.OrderByDescending(x => x.UpdatedAt).Select(x => new { x.Id, x.Title, x.MaterialId, x.UpdatedAt, QuestionCount = x.Questions.Count, TotalPoints = x.Questions.Sum(q => q.Points) }).ToListAsync(ct));
        });
        quizzes.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, ControlledLearning learning, CancellationToken ct) => {
            var quiz = await OwnedQuizzes(db, user).Include(x => x.Questions).ThenInclude(x => x.Options).SingleOrDefaultAsync(x => x.Id == id, ct);
            return quiz is null ? Results.NotFound() : Results.Ok(await QuizView(quiz, learning, Owner(user), ct));
        });
        quizzes.MapPost("/", async (SaveQuiz request, ClaimsPrincipal user, MaterialDbContext db, ControlledLearning learning, GenerationRecorder recorder, CancellationToken ct) => {
            if (TeachingValidation.Quiz(request) is { } errors) return Results.ValidationProblem(errors);
            if (ControlledLearning.ValidateGrounding(request) is { } reason) return Results.BadRequest(new { message = reason });
            if (!await recorder.Owned(request.GenerationRunId, Owner(user), false, ct)) return Results.NotFound();
            if (request.MaterialId is { } materialId && !await db.Materials.AnyAsync(x => x.Id == materialId && x.TeacherId == Owner(user), ct)) return Results.NotFound();
            var quiz = new Quiz { TeacherId = Owner(user) };
            var analysis = await learning.Analyze(request, Owner(user), ct);
            Apply(quiz, request); ControlledLearning.StoreReviews(quiz, request, analysis);
            db.Quizzes.Add(quiz); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/tests/{quiz.Id}", await QuizView(quiz, learning, Owner(user), ct));
        });
        quizzes.MapPut("/{id:guid}", async (Guid id, SaveQuiz request, ClaimsPrincipal user, MaterialDbContext db, ControlledLearning learning, GenerationRecorder recorder, CancellationToken ct) => {
            var quiz = await OwnedQuizzes(db, user).Include(x => x.Questions).ThenInclude(x => x.Options).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (quiz is null) return Results.NotFound();
            if (TeachingValidation.Quiz(request) is { } errors) return Results.ValidationProblem(errors);
            if (ControlledLearning.ValidateGrounding(request) is { } reason) return Results.BadRequest(new { message = reason });
            if (!await recorder.Owned(request.GenerationRunId, Owner(user), false, ct)) return Results.NotFound();
            if (request.MaterialId is { } materialId && !await db.Materials.AnyAsync(x => x.Id == materialId && x.TeacherId == Owner(user), ct)) return Results.NotFound();
            var analysis = await learning.Analyze(request, Owner(user), ct);
            db.Questions.RemoveRange(quiz.Questions);
            Apply(quiz, request); ControlledLearning.StoreReviews(quiz, request, analysis);
            db.Questions.AddRange(quiz.Questions); await db.SaveChangesAsync(ct);
            return Results.Ok(await QuizView(quiz, learning, Owner(user), ct));
        });
        quizzes.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var quiz = await OwnedQuizzes(db, user).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (quiz is null) return Results.NotFound();
            await using var transaction=await db.Database.BeginTransactionAsync(ct);
            await db.CopyReferences.Where(x=>x.CopyId==id&&x.Kind=="test"&&x.TeacherId==Owner(user)).ExecuteDeleteAsync(ct);
            await db.PublicationReviews.Where(x=>x.SourceId==id&&x.Kind=="test").ExecuteDeleteAsync(ct);
            await db.ContentAccesses.Where(x=>x.SourceId==id&&x.Kind=="test"&&x.TeacherId==Owner(user)).ExecuteDeleteAsync(ct);
            db.Quizzes.Remove(quiz); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return Results.NoContent();
        });
        app.MapPost("/api/ai/test", async (GenerateQuiz request, ClaimsPrincipal user, MaterialDbContext db, IAiService ai, GenerationRecorder recorder, CancellationToken ct) => {
            var material = await db.Materials.SingleOrDefaultAsync(x => x.Id == request.MaterialId && x.TeacherId == Owner(user), ct);
            if (material is null) return Results.NotFound();
            if (request.Requirements is null || request.Requirements.Length > 2000 || request.QuestionCount is < 1 or > 30 || (request.TotalPoints.HasValue && (request.TotalPoints < request.QuestionCount || request.TotalPoints > request.QuestionCount * 100)) || request.Difficulty is not ("Лёгкая" or "Средняя" or "Сложная") || request.Types is null || request.Types.Count is < 1 or > 5 || request.Types.Any(x => !Enum.IsDefined(x)) || request.Types.Distinct().Count() != request.Types.Count)
                return Results.BadRequest(new { message = "Укажите от 1 до 30 вопросов, сложность и типы вопросов без повторов." });
            var text = MaterialText.Extract(material.Content);
            if (string.IsNullOrWhiteSpace(text)) return Results.BadRequest(new { message = "Материал пуст. Добавьте содержание перед генерацией теста." });
            if (text.Length > 60000) return Results.BadRequest(new { message = "Для генерации выберите материал до 60 000 символов." });
            request = request with { Context = await MaterialTeachingSettings.Read(material, db, ct) };
            try { var (run, result) = await recorder.Record(Owner(user), "Test", "material-test-v2", request, new { material.Id, Text = text }, ai,
                () => ai.GenerateQuizAsync(material.Title, text, request, ct), ct); return Results.Ok(result with { GenerationRunId = run.Id }); }
            catch (AiUnavailableException ex) { return Results.Problem(ex.Message, statusCode: 503); }
            catch (HttpRequestException) { return Results.Problem("Не удалось связаться с AI-провайдером.", statusCode: 503); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Results.Problem("AI не ответил вовремя.", statusCode: 504); }
        }).RequireAuthorization().RequireRateLimiting("ai");

        var groups = app.MapGroup("/api/groups").RequireAuthorization();
        groups.MapGet("/", async (ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => Results.Ok(await OwnedGroups(db, user).AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.Description, StudentCount = x.Students.Count }).ToListAsync(ct)));
        groups.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var group = await OwnedGroups(db, user).Include(x => x.Students).SingleOrDefaultAsync(x => x.Id == id, ct);
            return group is null ? Results.NotFound() : Results.Ok(GroupView(group));
        });
        groups.MapPost("/", async (SaveGroup request, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            if (!TeachingValidation.Group(request)) return Results.BadRequest(new { message = "Название группы: до 100 символов; описание: до 2000." });
            var group = new StudyGroup { TeacherId = Owner(user), Name = request.Name.Trim(), Description = request.Description.Trim() };
            db.Groups.Add(group); await db.SaveChangesAsync(ct); return Results.Created($"/api/groups/{group.Id}", GroupView(group));
        });
        groups.MapPut("/{id:guid}", async (Guid id, SaveGroup request, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var group = await OwnedGroups(db, user).Include(x => x.Students).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (group is null) return Results.NotFound();
            if (!TeachingValidation.Group(request)) return Results.BadRequest(new { message = "Укажите название до 100 символов и описание до 2000." });
            group.Name = request.Name.Trim(); group.Description = request.Description.Trim(); await db.SaveChangesAsync(ct); return Results.Ok(GroupView(group));
        });
        groups.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            var group = await OwnedGroups(db, user).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (group is null) return Results.NotFound();
            db.Groups.Remove(group); await db.SaveChangesAsync(ct); return Results.NoContent();
        });
        groups.MapPost("/{id:guid}/students", async (Guid id, SaveStudent request, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            if (!await OwnedGroups(db, user).AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            if (!TeachingValidation.Student(request)) return Results.BadRequest(new { message = "Укажите имя и фамилию до 100 символов." });
            var student = new Student { GroupId = id, FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim() };
            db.Students.Add(student); await db.SaveChangesAsync(ct); return Results.Created($"/api/groups/{id}", StudentView(student));
        });
        groups.MapPut("/{id:guid}/students/{studentId:guid}", async (Guid id, Guid studentId, SaveStudent request, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            if (!await OwnedGroups(db, user).AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            var student = await db.Students.SingleOrDefaultAsync(x => x.GroupId == id && x.Id == studentId, ct);
            if (student is null) return Results.NotFound();
            if (!TeachingValidation.Student(request)) return Results.BadRequest(new { message = "Укажите имя и фамилию до 100 символов." });
            student.FirstName = request.FirstName.Trim(); student.LastName = request.LastName.Trim(); await db.SaveChangesAsync(ct); return Results.Ok(StudentView(student));
        });
        groups.MapDelete("/{id:guid}/students/{studentId:guid}", async (Guid id, Guid studentId, ClaimsPrincipal user, MaterialDbContext db, CancellationToken ct) => {
            if (!await OwnedGroups(db, user).AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            var student = await db.Students.SingleOrDefaultAsync(x => x.GroupId == id && x.Id == studentId, ct);
            if (student is null) return Results.NotFound();
            db.Students.Remove(student); await db.SaveChangesAsync(ct); return Results.NoContent();
        });
    }
    internal static void Apply(Quiz quiz, SaveQuiz request)
    {
        quiz.Title = request.Title.Trim(); quiz.MaterialId = request.MaterialId; quiz.Controlled = request.Controlled; quiz.UpdatedAt = DateTimeOffset.UtcNow;
        quiz.GenerationRunId = request.GenerationRunId;
        quiz.Questions = request.Questions.Select((q, index) => new Question {
            QuizId = quiz.Id, Position = index, Type = q.Type, Text = q.Text.Trim(), Points = q.Points, ModelAnswer = q.ModelAnswer.Trim(),
            Options = q.Options.Select((option, position) => new QuestionOption { Position = position, Text = option.Text.Trim(), IsCorrect = option.IsCorrect }).ToList()
        }).ToList();
    }
    static async Task<object> QuizView(Quiz quiz, ControlledLearning learning, Guid owner, CancellationToken ct) {
        var draft = await learning.ViewDraft(quiz, owner, ct);
        return new { quiz.Id, draft.Title, draft.MaterialId, draft.Controlled, quiz.GenerationRunId, quiz.UpdatedAt,
            Questions = quiz.Questions.OrderBy(q => q.Position).Select((q, i) => new {
                q.Id, q.Type, q.Text, q.Points, q.ModelAnswer, draft.Questions[i].Grounding,
                Options = q.Options.OrderBy(o => o.Position).Select(o => new { o.Id, o.Text, o.IsCorrect })
            }) };
    }
    static object StudentView(Student student) => new { student.Id, student.FirstName, student.LastName };
    static object GroupView(StudyGroup group) => new { group.Id, group.Name, group.Description, Students = group.Students.OrderBy(x => x.LastName).ThenBy(x => x.FirstName).Select(StudentView) };
}
