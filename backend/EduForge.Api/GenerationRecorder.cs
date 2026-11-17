using System.Diagnostics;
using System.Text.Json;
using EduForge.Application;
using EduForge.Domain;
using EduForge.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EduForge.Api;

public sealed class GenerationSourceChangedException(string message) : Exception(message);

public sealed class GenerationRecorder(MaterialDbContext db, IConfiguration config)
{
    public async Task<(GenerationRun Run, T Result)> Record<T>(Guid owner, string kind, string template, object parameters,
        object sources, object provider, Func<Task<T>> generate, CancellationToken ct)
    {
        var run = new GenerationRun { TeacherId = owner, Kind = kind, TemplateVersion = template,
            Model = config["AI__Model"] ?? config["AI:Model"] ?? "",
            Parameters = JsonSerializer.Serialize(parameters, ControlledLearning.Json), SourceSnapshot = JsonSerializer.Serialize(sources, ControlledLearning.Json) };
        db.GenerationRuns.Add(run); await db.SaveChangesAsync(ct);
        var watch = Stopwatch.StartNew();
        try {
            var result = await generate();
            run.Result = JsonSerializer.Serialize(result, ControlledLearning.Json);
            run.Status = "Completed";
            return (run, result);
        } catch (Exception ex) {
            run.Status = "Failed";
            run.Failure = ex is OperationCanceledException ? "CancelledOrTimedOut" : ex is AiUnavailableException ? "ProviderOrValidationError" : "GenerationError";
            throw;
        } finally {
            run.FinishedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); run.DurationMs = watch.ElapsedMilliseconds;
            if (provider is IAiDiagnostics { LastReceipt: { } receipt }) {
                if (!string.IsNullOrWhiteSpace(receipt.Model)) run.Model = receipt.Model;
                run.InputTokens = receipt.InputTokens; run.OutputTokens = receipt.OutputTokens;
                if (run.Status == "Failed" && receipt.ErrorCode is not null) run.Failure = receipt.ErrorCode;
            }
            using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await db.SaveChangesAsync(completion.Token);
        }
    }

    public Task<bool> Owned(Guid? runId, Guid owner, bool material, CancellationToken ct) => runId is null ? Task.FromResult(true) :
        db.GenerationRuns.AnyAsync(r => r.Id == runId && r.TeacherId == owner && r.Status == "Completed" &&
            (material ? r.Kind == "Material" || r.Kind == "SourceMaterial" : r.Kind == "Test" || r.Kind == "ControlledTest"), ct);
}
