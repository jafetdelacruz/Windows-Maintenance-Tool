using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinMaint.Application;

namespace WinMaint.Reporting;

// Append-only checkpoints: a torn tail is detectable and cannot authorize resumption.
// Flush(true)/WriteThrough request OS durability, not a guarantee against dishonest storage.
public sealed class FileTemporaryCleanupJournal(string directory) : ITemporaryCleanupJournal, ITemporaryCleanupRecoveryReader, IDisposable
{
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinMaint", "CleanupJournal");
    private const string Schema = "temporary-cleanup-journal-1.1";
    private const int MaximumCheckpointBytes = 16 * 1024 * 1024;
    private readonly string directory = Path.GetFullPath(directory);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<Guid, State> states = [];
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    private sealed record Envelope(string SchemaVersion, string Payload, string PreviousHash, string Hash);
    private sealed record State(int NextSequence, string Hash, bool Closed);

    public async Task AppendAsync(TemporaryCleanupCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.AttemptId == Guid.Empty || checkpoint.Sequence is < 0 or > 40_002 || !Enum.IsDefined(checkpoint.Kind))
            throw new IOException("Invalid cleanup checkpoint identity.");
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string folder = Path.Combine(directory, checkpoint.AttemptId.ToString("N"));
            if (checkpoint.Kind == TemporaryCleanupCheckpointKind.Started)
            {
                if (checkpoint.Sequence != 0 || states.ContainsKey(checkpoint.AttemptId) || Directory.Exists(folder) ||
                    TemporaryCleanupRecoveryEngine.Replay(checkpoint.AttemptId, [checkpoint]).IsCorrupt)
                    throw new IOException("A cleanup attempt cannot overwrite an earlier journal.");
                Directory.CreateDirectory(directory);
                RejectReparse(directory);
                Directory.CreateDirectory(folder);
                states.Add(checkpoint.AttemptId, new(0, string.Empty, false));
            }
            if (!states.TryGetValue(checkpoint.AttemptId, out var state) || state.Closed || state.NextSequence != checkpoint.Sequence)
                throw new IOException("Cleanup checkpoint order is invalid.");
            RejectReparse(folder);
            string payload = JsonSerializer.Serialize(checkpoint, Options);
            string hash = Hash(state.Hash, payload);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(Schema, payload, state.Hash, hash), Options);
            if (bytes.Length > MaximumCheckpointBytes) throw new IOException("Cleanup checkpoint exceeds the bound.");
            string path = Path.Combine(folder, checkpoint.Sequence.ToString("D8", CultureInfo.InvariantCulture) + ".json");
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            states[checkpoint.AttemptId] = new(checkpoint.Sequence + 1, hash,
                checkpoint.Kind == TemporaryCleanupCheckpointKind.Finished);
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<TemporaryCleanupRecovery>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        var recoveries = new List<TemporaryCleanupRecovery>();
        if (!Directory.Exists(directory)) return recoveries;
        RejectReparse(directory);
        foreach (string folder in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out var attemptId)) continue;
            var checkpoints = new List<TemporaryCleanupCheckpoint>();
            bool corrupt = false;
            string previousHash = string.Empty;
            try
            {
                RejectReparse(folder);
                foreach (string path in Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RejectReparse(path);
                    if (Path.GetFileName(path) != checkpoints.Count.ToString("D8", CultureInfo.InvariantCulture) + ".json" ||
                        new FileInfo(path).Length > MaximumCheckpointBytes)
                    { corrupt = true; break; }
                    var raw = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                    using (var parsed = JsonDocument.Parse(raw)) EvidenceJsonValidation.NoDuplicates(parsed.RootElement);
                    var envelope = JsonSerializer.Deserialize<Envelope>(raw, Options);
                    if (envelope is null || envelope.SchemaVersion != Schema || envelope.Payload is null ||
                        envelope.PreviousHash != previousHash || envelope.Hash != Hash(previousHash, envelope.Payload))
                    { corrupt = true; break; }
                    using (var payload = JsonDocument.Parse(envelope.Payload)) EvidenceJsonValidation.NoDuplicates(payload.RootElement);
                    var checkpoint = JsonSerializer.Deserialize<TemporaryCleanupCheckpoint>(envelope.Payload, Options);
                    if (checkpoint is null) { corrupt = true; break; }
                    checkpoints.Add(checkpoint);
                    previousHash = envelope.Hash;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            { corrupt = true; }
            recoveries.Add(TemporaryCleanupRecoveryEngine.Replay(attemptId, checkpoints, corrupt));
        }
        return recoveries;
    }

    private static string Hash(string previous, string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(previous + "\n" + payload)));

    private static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A cleanup journal cannot traverse a reparse point.");
    }

    public void Dispose() => gate.Dispose();
}
