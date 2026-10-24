using System.Text.Json;
using WinMaint.Application;

namespace WinMaint.Infrastructure.Win;

public sealed class FileStabilitySessionStore : IStabilitySessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string statePath;

    public FileStabilitySessionStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinMaint",
            "stability-session.json"))
    {
    }

    internal FileStabilitySessionStore(string statePath)
    {
        this.statePath = Path.GetFullPath(statePath);
    }

    public async Task<InterruptedStabilitySession?> ReadInterruptedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(statePath))
        {
            return File.Exists(statePath + ".tmp")
                ? new InterruptedStabilitySession(DateTimeOffset.MinValue, "Escritura inicial interrumpida")
                : null;
        }

        try
        {
            await using var stream = new FileStream(
                statePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var state = await JsonSerializer.DeserializeAsync<SessionState>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return state is null ? null : new InterruptedStabilitySession(state.StartedAt, state.LastCompletedProbeId);
        }
        catch (JsonException)
        {
            return new InterruptedStabilitySession(DateTimeOffset.MinValue, "Estado ilegible; conservar para revisión");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new InterruptedStabilitySession(DateTimeOffset.MinValue, "Estado inaccesible; conservar para revisión");
        }
    }

    public Task StartAsync(DateTimeOffset startedAt, CancellationToken cancellationToken) =>
        WriteAtomicAsync(new SessionState(startedAt, null), cancellationToken);

    public async Task UpdateAsync(string completedProbeId, CancellationToken cancellationToken)
    {
        var current = await ReadInterruptedAsync(cancellationToken).ConfigureAwait(false);
        var startedAt = current?.StartedAt ?? DateTimeOffset.Now;
        await WriteAtomicAsync(new SessionState(startedAt, completedProbeId), cancellationToken).ConfigureAwait(false);
    }

    public Task CompleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(statePath))
        {
            File.Delete(statePath);
        }

        var temporary = statePath + ".tmp";
        if (File.Exists(temporary))
        {
            File.Delete(temporary);
        }

        return Task.CompletedTask;
    }

    private async Task WriteAtomicAsync(SessionState state, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(statePath)
            ?? throw new InvalidOperationException("La ruta de sesión no tiene directorio.");
        Directory.CreateDirectory(directory);
        var temporary = statePath + ".tmp";
        await using (var stream = new FileStream(
            temporary,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, statePath, overwrite: true);
    }

    private sealed record SessionState(DateTimeOffset StartedAt, string? LastCompletedProbeId);
}
