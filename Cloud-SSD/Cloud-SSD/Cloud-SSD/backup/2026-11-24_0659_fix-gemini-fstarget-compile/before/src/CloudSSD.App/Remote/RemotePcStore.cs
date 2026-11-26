using System.Security.Cryptography;
using System.Text.Json;
using CloudSSD.Diagnostics;

namespace CloudSSD.App.Remote;

/// <summary>
/// The other PCs this one is paired with: remote\pcs.json (keys protected with DPAPI, never in plain text), this
/// install's id, and one folder per PC for what is kept from it (lists, thumbnails, offline copies).
/// </summary>
public sealed class RemotePcStore
{
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private readonly object _lock = new();
    private readonly string _dir;
    private Data _data = new();

    private sealed class Data
    {
        public List<RemotePc> Pcs { get; set; } = new();
        public string? ActivePcId { get; set; }
    }

    public RemotePcStore(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(dir);
        Load();
    }

    public string Directory_ => _dir;
    private string FilePath => Path.Combine(_dir, "pcs.json");

    /// <summary>The ids another PC sends are used in folder names: 8–32 lowercase hex only.</summary>
    public static bool IsValidPcId(string? id) => id is { Length: >= 8 and <= 32 } && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public IReadOnlyList<RemotePc> Pcs { get { lock (_lock) return _data.Pcs.ToList(); } }

    public string? ActivePcId
    {
        get { lock (_lock) return _data.ActivePcId; }
        set { lock (_lock) _data.ActivePcId = value; Save(); }
    }

    public RemotePc? Get(string pcId) { lock (_lock) return _data.Pcs.FirstOrDefault(p => p.PcId == pcId); }

    /// <summary>A random id for this install, sent when pairing so pairing again updates the same entry on the other PC.</summary>
    public string InstallId
    {
        get
        {
            var file = Path.Combine(_dir, "install-id");
            try
            {
                if (File.Exists(file)) { var s = File.ReadAllText(file).Trim(); if (s.Length == 32) return s; }
                var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                File.WriteAllText(file, id);
                return id;
            }
            catch (IOException) { return "laptop-" + Environment.MachineName.ToLowerInvariant(); }
        }
    }

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                _data = File.Exists(FilePath) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath), Json) ?? new Data() : new Data();
                _data.Pcs.RemoveAll(p => !IsValidPcId(p.PcId));
            }
            catch (Exception e) when (e is JsonException or IOException)
            {
                // A damaged file is kept aside, not lost.
                try { File.Copy(FilePath, FilePath + ".bad", overwrite: true); } catch (IOException) { }
                _data = new Data();
            }
        }
    }

    /// <summary>
    /// Writes a cache file whole or not at all. Two writers racing on the same file (the page and the background refresh)
    /// each use their own temporary file; the later one wins and neither fails — a cache that is not written is no error.
    /// </summary>
    internal static void WriteCache(string file, string text)
    {
        var tmp = file + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(tmp, text);
            File.Move(tmp, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(tmp); } catch (Exception d) when (d is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Written to a temporary file and swapped in, so a crash never leaves half a file.</summary>
    public void Save()
    {
        lock (_lock)
        {
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_data, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
    }

    public void Upsert(RemotePc pc)
    {
        if (!IsValidPcId(pc.PcId)) throw new ArgumentException("Invalid PC id.");
        lock (_lock)
        {
            var i = _data.Pcs.FindIndex(p => p.PcId == pc.PcId);
            if (i >= 0) _data.Pcs[i] = pc; else _data.Pcs.Add(pc);
        }
        Save();
    }

    public void Remove(string pcId)
    {
        lock (_lock)
        {
            _data.Pcs.RemoveAll(p => p.PcId == pcId);
            if (_data.ActivePcId == pcId) _data.ActivePcId = _data.Pcs.FirstOrDefault()?.PcId;
        }
        Save();
        try
        {
            var d = PcDir(pcId);
            if (Directory.Exists(d))
            {
                // Copies kept offline are read-only, which a plain recursive delete refuses.
                foreach (var f in Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(d, true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    public string? Token(RemotePc pc) => SecretStore.Unprotect(pc.TokenProtected);
    public static string Protect(string token) => SecretStore.Protect(token);

    /// <summary>The folder kept for one PC; refuses an id that could point anywhere else.</summary>
    public string PcDir(string pcId)
    {
        if (!IsValidPcId(pcId)) throw new ArgumentException("Invalid PC id.");
        return Path.Combine(_dir, pcId);
    }
}
