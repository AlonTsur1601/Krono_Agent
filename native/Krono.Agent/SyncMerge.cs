using System.Text.Json.Nodes;

namespace Krono_Agent;

public enum SyncDecision { Nothing, Acknowledge, Pull, Insert, Update, Conflict }

public static class SyncMerge
{
    public static SyncDecision Decide(JsonObject? local, JsonObject? cloud, JsonObject? baseline)
    {
        if (local is null) return cloud is null ? SyncDecision.Nothing : SyncDecision.Pull;
        if (cloud is null) return baseline is null ? SyncDecision.Insert : JsonNode.DeepEquals(local, baseline) && Tombstone(local) ? SyncDecision.Nothing : SyncDecision.Conflict;
        if (JsonNode.DeepEquals(local, cloud)) return SyncDecision.Acknowledge;
        if (SameContent(local, cloud)) return UpdatedAt(local) > UpdatedAt(cloud) ? SyncDecision.Update : SyncDecision.Pull;
        // A tombstone beats a concurrent edit: an offline client must not revive deleted data.
        if (Tombstone(cloud) && !Tombstone(local)) return SyncDecision.Pull;
        if (Tombstone(local) && !Tombstone(cloud)) return SyncDecision.Update;
        if (baseline is null) return SyncDecision.Conflict;
        if (SameContent(local, baseline)) return SyncDecision.Pull;
        return SameContent(cloud, baseline) ? SyncDecision.Update : SyncDecision.Conflict;
    }
    static long UpdatedAt(JsonObject row) => long.TryParse(row["updated_at"]?.ToString(), out var time) ? time : 0;
    static bool SameContent(JsonObject a, JsonObject b) => a.Count == b.Count && a.All(pair => pair.Key == "updated_at" || b.ContainsKey(pair.Key) && JsonNode.DeepEquals(pair.Value, b[pair.Key]));
    static bool Tombstone(JsonObject row) => row["deleted_at"] is not null;
}
