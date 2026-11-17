using EduForge.Application;
using EduForge.Domain;
using Microsoft.EntityFrameworkCore;
namespace EduForge.Infrastructure;
public sealed class MaterialDbContext(DbContextOptions<MaterialDbContext> options) : DbContext(options)
{
    public DbSet<MaterialRevision> MaterialRevisions => Set<MaterialRevision>();
    public DbSet<MaterialLearningPlan> LearningPlans => Set<MaterialLearningPlan>();
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();
    public DbSet<GenerationRun> GenerationRuns => Set<GenerationRun>();
    public DbSet<ContentAccess> ContentAccesses => Set<ContentAccess>();
    public DbSet<CopyReference> CopyReferences => Set<CopyReference>();
    public DbSet<PublicationReview> PublicationReviews => Set<PublicationReview>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<AssignmentSubmission> Submissions => Set<AssignmentSubmission>();
    public DbSet<AssignmentAsset> AssignmentAssets => Set<AssignmentAsset>();
    public DbSet<PasswordReset> PasswordResets => Set<PasswordReset>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<GroupInvitation> Invitations => Set<GroupInvitation>();
    public DbSet<EnrollmentRequest> Enrollments => Set<EnrollmentRequest>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<StudyGroup> Groups => Set<StudyGroup>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<TestingSession> Sessions => Set<TestingSession>();
    public DbSet<PublishedQuiz> QuizPublications => Set<PublishedQuiz>();
    public DbSet<PublishedMaterial> Publications => Set<PublishedMaterial>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<ContentAccess>().HasOne<Teacher>().WithMany().HasForeignKey(x=>x.TeacherId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ContentAccess>().HasIndex(x=>new{x.Kind,x.SourceId}).IsUnique();
        builder.Entity<ContentAccess>().HasIndex(x=>x.Token).IsUnique();
        builder.Entity<ContentAccess>().Property(x=>x.Token).HasMaxLength(64);
        builder.Entity<ContentAccess>().Property(x=>x.Version).IsConcurrencyToken();
        builder.Entity<MaterialRevision>().HasOne<Material>().WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<MaterialRevision>().HasIndex(x => x.MaterialId);
        builder.Entity<MaterialLearningPlan>().HasKey(x => x.MaterialId);
        builder.Entity<MaterialLearningPlan>().HasOne<Material>().WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<MaterialLearningPlan>().Property(x => x.Version).IsConcurrencyToken();
        builder.Entity<SourceDocument>().HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SourceDocument>().HasIndex(x => new { x.TeacherId, x.Sha256 }).IsUnique();
        builder.Entity<SourceDocument>().Property(x => x.Name).HasMaxLength(200);
        builder.Entity<GenerationRun>().HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<GenerationRun>().HasIndex(x => new { x.TeacherId, x.StartedAt });
        builder.Entity<MediaAsset>().HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<GroupInvitation>().HasOne<StudyGroup>().WithMany().HasForeignKey(x => x.Id).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<GroupInvitation>().HasIndex(x => x.Token).IsUnique();
        builder.Entity<EnrollmentRequest>().HasOne<StudyGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<EnrollmentRequest>().HasIndex(x => new { x.GroupId, x.NormalizedName }).IsUnique();
        builder.Entity<CopyReference>().HasIndex(x=>new{x.TeacherId,x.CopyId,x.Kind}).IsUnique();
        builder.Entity<PublicationReview>().HasIndex(x=>new{x.SourceId,x.Kind,x.CreatedAt});
        builder.Entity<Assignment>().HasOne<Teacher>().WithMany().HasForeignKey(x=>x.TeacherId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Assignment>().HasIndex(x=>x.Token).IsUnique();
        builder.Entity<AssignmentSubmission>().HasOne<Assignment>().WithMany().HasForeignKey(x=>x.AssignmentId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<AssignmentAsset>().HasOne<Assignment>().WithMany().HasForeignKey(x=>x.AssignmentId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<AssignmentAsset>().HasOne<MediaAsset>().WithMany().HasForeignKey(x=>x.Id).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<PasswordReset>().HasOne<Teacher>().WithMany().HasForeignKey(x=>x.TeacherId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<PasswordReset>().HasIndex(x=>x.Hash).IsUnique();
        var material = builder.Entity<Material>();
        material.HasKey(x => x.Id);
        material.Property(x => x.Title).HasMaxLength(200);
        material.Property(x => x.Subject).HasMaxLength(100);
        material.Property(x => x.Level).HasMaxLength(50);
        material.Property(x => x.Language).HasMaxLength(50);
        // SQLite is used by the integration test host; store UTC ticks for sortable timestamps there.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            material.Property(x => x.UpdatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        material.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        material.HasIndex(x => new { x.TeacherId, x.UpdatedAt });
        var teacher = builder.Entity<Teacher>();
        teacher.HasKey(x => x.Id);
        teacher.HasIndex(x => x.NormalizedEmail).IsUnique();
        teacher.HasIndex(x => x.GoogleId).IsUnique();
        teacher.Property(x => x.Email).HasMaxLength(254);
        teacher.Property(x => x.NormalizedEmail).HasMaxLength(254);
        teacher.Property(x => x.Name).HasMaxLength(100);
        var quiz = builder.Entity<Quiz>();
        quiz.Property(x => x.Title).HasMaxLength(200);
        quiz.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        quiz.HasOne<Material>().WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Restrict);
        quiz.HasIndex(x => new { x.TeacherId, x.UpdatedAt });
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            quiz.Property(x => x.UpdatedAt).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        quiz.HasMany(x => x.Questions).WithOne().HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
        var question = builder.Entity<Question>();
        question.Property(x => x.Text).HasMaxLength(4000);
        question.Property(x => x.ModelAnswer).HasMaxLength(5000);
        question.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
        question.HasMany(x => x.Options).WithOne().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<QuestionOption>().Property(x => x.Text).HasMaxLength(1000);
        var group = builder.Entity<StudyGroup>();
        group.Property(x => x.Name).HasMaxLength(100);
        group.Property(x => x.Description).HasMaxLength(2000);
        group.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        group.HasMany(x => x.Students).WithOne().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Student>().Property(x => x.FirstName).HasMaxLength(100);
        builder.Entity<Student>().Property(x => x.LastName).HasMaxLength(100);
        var session = builder.Entity<TestingSession>();
        session.Property(x => x.Code).HasMaxLength(8);
        session.HasIndex(x => x.Code).IsUnique();
        session.Property(x => x.Title).HasMaxLength(200);
        session.Property(x => x.GroupName).HasMaxLength(100);
        session.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        session.Property(x => x.Revision).IsConcurrencyToken();
        session.HasIndex(x => new { x.TeacherId, x.CreatedAt });
        session.HasIndex(x => new { x.Status, x.Deadline });
        session.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        var publishedQuiz = builder.Entity<PublishedQuiz>();
        publishedQuiz.HasOne<Quiz>().WithMany().HasForeignKey(x => x.Id).OnDelete(DeleteBehavior.Cascade);
        publishedQuiz.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        publishedQuiz.Property(x => x.Revision).IsConcurrencyToken();
        publishedQuiz.HasIndex(x => x.PublishedAt);
        var publication = builder.Entity<PublishedMaterial>();
        publication.Property(x => x.Title).HasMaxLength(200);
        publication.Property(x => x.Subject).HasMaxLength(100);
        publication.Property(x => x.Level).HasMaxLength(50);
        publication.Property(x => x.Language).HasMaxLength(50);
        publication.Property(x => x.AuthorName).HasMaxLength(100);
        publication.Property(x => x.Revision).IsConcurrencyToken();
        publication.HasIndex(x => x.PublishedAt);
        publication.HasOne<Material>().WithMany().HasForeignKey(x => x.Id).OnDelete(DeleteBehavior.Cascade);
        publication.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class MaterialRepository(MaterialDbContext db) : IMaterialRepository
{
    public Task<List<Material>> ListAsync(Guid teacherId, CancellationToken ct) => db.Materials.AsNoTracking().Where(x => x.TeacherId == teacherId).OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);
    public Task<Material?> GetAsync(Guid id, Guid teacherId, CancellationToken ct) => db.Materials.SingleOrDefaultAsync(x => x.Id == id && x.TeacherId == teacherId, ct);
    public async Task SaveAsync(Material material, CancellationToken ct)
    {
        if (db.Entry(material).State == EntityState.Detached) db.Materials.Add(material);
        await db.SaveChangesAsync(ct);
    }
}
