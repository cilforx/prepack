namespace PrePack.Domain;

public enum SyncAction { None, Push, Pull }

/// <summary>
/// Merge rules between the local SQLite database and MySQL. Pure functions so they can be unit tested
/// without a server. Timestamps are UTC; last writer wins.
/// </summary>
public static class SyncRules
{
    /// <summary>Version of data nobody has edited yet (seeded defaults on either side).</summary>
    public static readonly DateTime Epoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// One staff member present on one or both sides.
    /// Only-local → push; only-remote → pull; both → the newer edit wins. A tie needs nothing
    /// (the caller just clears the local dirty flag).
    /// </summary>
    public static SyncAction Staff(DateTime? localUpdated, bool localDirty, DateTime? remoteUpdated) =>
        Record(localUpdated, localDirty, remoteUpdated);

    /// <summary>Same last-writer-wins rule for any keyed record (staff, drug labels).</summary>
    public static SyncAction Record(DateTime? localUpdated, bool localDirty, DateTime? remoteUpdated)
    {
        if (localUpdated is null) return remoteUpdated is null ? SyncAction.None : SyncAction.Pull;
        if (remoteUpdated is null) return SyncAction.Push;
        var cmp = Trunc(localUpdated.Value).CompareTo(Trunc(remoteUpdated.Value));
        if (cmp > 0) return localDirty ? SyncAction.Push : SyncAction.None;
        if (cmp < 0) return SyncAction.Pull;
        return SyncAction.None;
    }

    /// <summary>
    /// The factor table is versioned as a whole (Validate checks across rows). Newer wins; a tie means
    /// both sides already agree, and MySQL is never overwritten by an untouched (epoch) local table.
    /// </summary>
    public static SyncAction Factors(DateTime localVersion, DateTime remoteVersion)
    {
        var cmp = Trunc(localVersion).CompareTo(Trunc(remoteVersion));
        return cmp > 0 ? SyncAction.Push : cmp < 0 ? SyncAction.Pull : SyncAction.None;
    }

    /// <summary>MySQL DATETIME keeps whole seconds; compare at that precision.</summary>
    private static DateTime Trunc(DateTime t) => new(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, t.Kind);
}
