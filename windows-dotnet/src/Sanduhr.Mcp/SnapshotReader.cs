using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Mcp;

/// <summary>How a snapshot read ended — the discriminator the tool layer maps
/// to typed statuses. Atomic writes make Malformed always-a-bug, never a race.</summary>
public enum SnapshotReadOutcome { Ok, Missing, Malformed }

/// <summary>
/// The reader half of the snapshot contract (WS-E): open shared
/// (ReadWrite|Delete so the writer's atomic swap never blocks on us), one retry
/// on IOException, any parse failure = Malformed. Never throws to callers.
/// </summary>
public static class SnapshotReader
{
    public static (SnapshotReadOutcome Outcome, JsonObject? Snapshot) Read(string path)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                if (!File.Exists(path))
                    return (SnapshotReadOutcome.Missing, null);
                using var fs = new FileStream(
                    path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                string text = sr.ReadToEnd();
                return JsonNode.Parse(text) is JsonObject obj
                    ? (SnapshotReadOutcome.Ok, obj)
                    : (SnapshotReadOutcome.Malformed, null);
            }
            catch (IOException) when (attempt == 0)
            {
                Thread.Sleep(50);
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                return (SnapshotReadOutcome.Malformed, null);
            }
        }
        return (SnapshotReadOutcome.Malformed, null);
    }
}
