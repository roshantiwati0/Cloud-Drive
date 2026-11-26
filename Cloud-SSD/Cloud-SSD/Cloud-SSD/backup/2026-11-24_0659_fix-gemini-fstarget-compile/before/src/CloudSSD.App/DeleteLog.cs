using System.Text.Json;

namespace CloudSSD.App;

/// <summary>
/// One thing a device deleted on this PC. <see cref="Bin"/> says where it went: "recycle" (this PC's Recycle Bin, so
/// it can be restored from here, <see cref="OriginalPath"/> is where it was), "cloud" (the cloud account's trash) or
/// "gone" (deleted for good from the Space page).
/// </summary>
public sealed record DeletedItem(
    string Id, DateTimeOffset At, string DeviceId, string DeviceName, string Name, string Where, bool Folder, string Bin,
    string? OriginalPath, DateTimeOffset? RestoredAt = null);

/// <summary>
/// What paired devices deleted on this PC (newest last in the file), so the person at the PC can see it per device
/// and bring back what is still in the Recycle Bin. Kept 60 days and at most 500 entries.
/// </summary>
public sealed class DeleteLog
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(60);
    public const int MaxEntries = 500;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly string _file;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _lock = new();
    private List<DeletedItem>? _items;

    public DeleteLog(string file, Func<DateTimeOffset> now)
    {
        _file = file;
        _now = now;
    }

    /// <summary>Raised after something was added or restored.</summary>
    public event Action? Changed;

    public DeletedItem Add(string deviceId, string deviceName, string name, string where, bool folder, string bin, string? originalPath)
    {
        var item = new DeletedItem(Guid.NewGuid().ToString("N")[..12], _now(), deviceId, deviceName, name, where, folder, bin, originalPath);
        lock (_lock)
        {
            var list = Load();
            list.Add(item);
            var cutoff = _now() - KeepFor;
            list.RemoveAll(i => i.At < cutoff);
            if (list.Count > MaxEntries) list.RemoveRange(0, list.Count - MaxEntries);
            Save(list);
        }
        Changed?.Invoke();
        return item;
    }

    /// <summary>A device's deletes, newest first.</summary>
    public IReadOnlyList<DeletedItem> ForDevice(string deviceId)
    {
        lock (_lock) return Load().Where(i => i.DeviceId == deviceId).OrderByDescending(i => i.At).ToList();
    }

    public int CountFor(string deviceId)
    {
        lock (_lock) return Load().Count(i => i.DeviceId == deviceId);
    }

    /// <summary>
    /// Brings a deleted thing back from this PC's Recycle Bin (the newest copy deleted from that place). Returns where
    /// it is now; throws when it is not in the bin any more (emptied, or restored by hand).
    /// </summary>
    public string Restore(string id, string? binDirectory = null)
    {
        DeletedItem item;
        lock (_lock)
        {
            item = Load().FirstOrDefault(i => i.Id == id) ?? throw new KeyNotFoundException("Nothing like that was deleted.");
        }
        if (item.Bin != "recycle" || item.OriginalPath is null)
            throw new InvalidOperationException("It went to the cloud account's trash; restore it there.");
        var inBin = RecycleBin.Find(item.OriginalPath, binDirectory)
            // The copy this device deleted: the one closest to the time it was logged.
            .OrderBy(b => Math.Abs((b.DeletedUtc - item.At.UtcDateTime).TotalSeconds)).FirstOrDefault()
            ?? throw new FileNotFoundException("It is no longer in the Recycle Bin.", item.OriginalPath);
        var at = RecycleBin.Restore(inBin);
        lock (_lock)
        {
            var list = Load();
            int i = list.FindIndex(x => x.Id == id);
            if (i >= 0) list[i] = list[i] with { RestoredAt = _now() };
            Save(list);
        }
        Changed?.Invoke();
        return at;
    }

    /// <summary>Is it still in the Recycle Bin (so Restore can work)?</summary>
    public static bool InBin(DeletedItem item, string? binDirectory = null) =>
        item.Bin == "recycle" && item.RestoredAt is null && item.OriginalPath is not null && RecycleBin.Find(item.OriginalPath, binDirectory).Count > 0;

    /// <summary>When the file was last read or written here: changed on disk since (another Cloud-SSD process), read again.</summary>
    private DateTime _stamp;

    private DateTime Stamp() => File.Exists(_file) ? File.GetLastWriteTimeUtc(_file) : DateTime.MinValue;

    private List<DeletedItem> Load()
    {
        if (_items is not null && Stamp() == _stamp) return _items;
        try
        {
            _stamp = Stamp();
            _items = File.Exists(_file) ? JsonSerializer.Deserialize<List<DeletedItem>>(File.ReadAllText(_file), Json) ?? [] : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _items ??= [];
        }
        return _items;
    }

    private void Save(List<DeletedItem> list)
    {
        _items = list;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(list, Json));
            File.Move(tmp, _file, overwrite: true);
            _stamp = Stamp();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Kept in memory; written with the next change.
        }
    }
}
