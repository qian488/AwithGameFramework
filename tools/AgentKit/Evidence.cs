using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AwithGameFrame.AgentKit;

public static class Evidence
{
    private static readonly JsonSerializerOptions EventOptions = new(TaskFiles.JsonOptions) { WriteIndented = false };
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static string Digest<T>(T value) => Hash(JsonSerializer.SerializeToUtf8Bytes(value, TaskFiles.JsonOptions));
    public static string State(Workspace workspace, TaskSpec task, string directory)
    {
        var context = ContextBundle.ReadFresh(workspace, directory, task);
        var state = workspace.CaptureBaseline();
        return Digest(new { task, context = context.Digest, state.Head,
            files = state.Files.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray() });
    }
    public static void Event(string directory, string kind, object data) => File.AppendAllText(
        Path.Combine(directory, "events.jsonl"),
        JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, kind, data }, EventOptions) + "\n", new UTF8Encoding(false));
}
