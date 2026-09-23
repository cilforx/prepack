using PrePack.Config;
using PrePack.Domain;

namespace PrePack.Data;

public sealed record SyncStatus(bool Configured, bool Running, DateTime? LastSyncAt, string? LastError,
    int Unsynced, bool InBackoff);

/// <summary>
/// Local SQLite → MySQL. Order matters: schema (with the HOSxP/JHCIS guard) → staff (two-way, last writer wins)
/// → factor table (newer version wins) → print logs (push only; local rows are kept as the backup).
/// One run at a time; after a failure it waits before trying again so a dead server never slows printing.
/// </summary>
public sealed class SyncService(LocalDb local, Func<MySqlSettings> settings)
{
    private static readonly TimeSpan Backoff = TimeSpan.FromSeconds(60);
    private const int LogBatch = 500;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _schemaReady;
    private DateTime? _lastSyncAt;
    private string? _lastError;
    private DateTime _retryAfter = DateTime.MinValue;

    public SyncStatus Status => new(settings().IsConfigured, _gate.CurrentCount == 0, _lastSyncAt, _lastError,
        local.UnsyncedCount(), DateTime.UtcNow < _retryAfter);

    /// <summary>MySQL settings changed: forget the schema check and any backoff.</summary>
    public void Reset()
    {
        _schemaReady = false;
        _retryAfter = DateTime.MinValue;
        _lastError = null;
    }

    public bool CanReachNow => settings().IsConfigured && DateTime.UtcNow >= _retryAfter;

    /// <summary>
    /// Server-first: sync right now and wait for it, unless the server is not set up or failed within the
    /// last minute (then the caller just uses the local database). Returns true when the server is up to date.
    /// </summary>
    public async Task<bool> TryNowAsync() => CanReachNow && await RunAsync(wait: true);

    /// <summary>
    /// Runs one sync. Returns false when not configured, backing off, already running (and wait=false), or failed.
    /// force = ignore the backoff (user pressed "sync now" or saved settings).
    /// </summary>
    public async Task<bool> RunAsync(bool force = false, bool wait = false)
    {
        var m = settings();
        if (!m.IsConfigured) return false;
        if (!force && DateTime.UtcNow < _retryAfter) return false;
        if (!await _gate.WaitAsync(wait ? Timeout.Infinite : 0)) return false;
        try
        {
            var password = ConfigStore.Unprotect(m.PasswordEnc);
            if (!_schemaReady)
            {
                await Schema.EnsureAsync(m.Host, m.Port, m.User, password, m.Database);
                _schemaReady = true;
            }
            var remote = PrePackDb.Create(m.Host, m.Port, m.User, password, m.Database);
            await SyncStaffAsync(remote);
            await SyncFactorsAsync(remote);
            await SyncDrugLabelsAsync(remote);
            await PushLogsAsync(remote);

            _lastSyncAt = DateTime.Now;
            _lastError = null;
            _retryAfter = DateTime.MinValue;
            return true;
        }
        catch (Exception e)
        {
            _lastError = e is UserError ? e.Message : "sync ไม่สำเร็จ: " + e.Message;
            _retryAfter = DateTime.UtcNow + Backoff;
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SyncStaffAsync(PrePackDb remote)
    {
        var remoteRows = (await remote.ListStaffAsync()).ToDictionary(x => x.Row.Staff.Uid, x => x.Row);
        var localRows = local.ListStaffSync().ToDictionary(x => x.Staff.Uid);

        foreach (var uid in localRows.Keys.Union(remoteRows.Keys))
        {
            localRows.TryGetValue(uid, out var l);
            remoteRows.TryGetValue(uid, out var r);
            switch (SyncRules.Staff(l?.UpdatedUtc, l?.Dirty ?? false, r?.UpdatedUtc))
            {
                case SyncAction.Push:
                    await remote.UpsertStaffAsync(l!.Staff, l.UpdatedUtc);
                    local.MarkStaffClean(uid, l.UpdatedUtc);
                    break;
                case SyncAction.Pull:
                    local.ApplyRemoteStaff(r!.Staff, r.UpdatedUtc);
                    break;
                default:
                    if (l is { Dirty: true }) local.MarkStaffClean(uid, l.UpdatedUtc);
                    break;
            }
        }
    }

    private async Task SyncDrugLabelsAsync(PrePackDb remote)
    {
        var remoteRows = (await remote.ListDrugLabelsAsync()).ToDictionary(x => x.Label.Key);
        var localRows = local.ListDrugLabelsSync().ToDictionary(x => x.Label.Key);
        foreach (var key in localRows.Keys.Union(remoteRows.Keys))
        {
            localRows.TryGetValue(key, out var l);
            remoteRows.TryGetValue(key, out var r);
            switch (SyncRules.Record(l?.UpdatedUtc, l?.Dirty ?? false, r?.UpdatedUtc))
            {
                case SyncAction.Push:
                    await remote.UpsertDrugLabelAsync(l!.Label, l.UpdatedUtc, Environment.MachineName);
                    local.MarkDrugLabelClean(key, l.UpdatedUtc);
                    break;
                case SyncAction.Pull:
                    local.ApplyRemoteDrugLabel(r!.Label, r.UpdatedUtc);
                    break;
                default:
                    if (l is { Dirty: true }) local.MarkDrugLabelClean(key, l.UpdatedUtc);
                    break;
            }
        }
    }

    /// <summary>
    /// Server-first lookup of one drug label: read MySQL directly (and keep the local copy current);
    /// fall back to the local copy when the server is not set up or not answering.
    /// </summary>
    public async Task<DrugLabel?> GetDrugLabelAsync(string key)
    {
        var m = settings();
        if (CanReachNow)
        {
            try
            {
                if (!_schemaReady) await RunAsync(wait: true); // makes sure 003_drug_labels exists
                var remote = PrePackDb.Create(m.Host, m.Port, m.User, ConfigStore.Unprotect(m.PasswordEnc), m.Database);
                var r = await remote.GetDrugLabelAsync(key);
                var l = local.ListDrugLabelsSync().FirstOrDefault(x => x.Label.Key == key);
                if (r != null && SyncRules.Record(l?.UpdatedUtc, l?.Dirty ?? false, r.UpdatedUtc) == SyncAction.Pull)
                    local.ApplyRemoteDrugLabel(r.Label, r.UpdatedUtc);
            }
            catch (Exception e)
            {
                _lastError = e is UserError ? e.Message : "อ่านชื่อบนฉลากจาก server ไม่ได้: " + e.Message;
                _retryAfter = DateTime.UtcNow + Backoff;
            }
        }
        return local.GetDrugLabel(key);
    }

    private async Task SyncFactorsAsync(PrePackDb remote)
    {
        var (localTable, localVersion) = local.GetWorkFactors();
        var (remoteTable, remoteVersion) = await remote.ListWorkFactorsAsync();
        switch (SyncRules.Factors(localVersion, remoteVersion))
        {
            case SyncAction.Push:
                await remote.SaveWorkFactorsAsync(localTable, localVersion);
                break;
            case SyncAction.Pull when remoteTable.Count > 0:
                local.SaveWorkFactors(remoteTable, remoteVersion);
                break;
        }
    }

    private async Task PushLogsAsync(PrePackDb remote)
    {
        Dictionary<string, int>? ids = null;
        while (true)
        {
            var batch = local.UnsyncedLogs(LogBatch);
            if (batch.Count == 0) return;
            ids ??= (await remote.ListStaffAsync()).ToDictionary(x => x.Row.Staff.Uid, x => x.Id);
            foreach (var log in batch)
            {
                if (!ids.TryGetValue(log.StaffUid, out var staffId))
                    throw new UserError($"ไม่พบผู้บรรจุ {log.StaffName} ใน MySQL — ส่งบันทึกไม่ได้");
                await remote.InsertLogAsync(log, staffId);
                local.MarkSynced(log.ClientUid); // per row, only after the insert succeeded
            }
            if (batch.Count < LogBatch) return;
        }
    }
}
