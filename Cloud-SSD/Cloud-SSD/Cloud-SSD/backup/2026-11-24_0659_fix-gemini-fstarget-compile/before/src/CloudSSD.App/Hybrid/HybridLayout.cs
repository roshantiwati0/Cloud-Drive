using System.Text.Json;

namespace CloudSSD.App.Hybrid;

/// <summary>What the fast drive remembers between runs (hybrid-state.json in the data folder).</summary>
public sealed class HybridState
{
    /// <summary>Links this app made in the fast drive: name → the cloud drive path it points at.</summary>
    public Dictionary<string, string> Links { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Cloud items whose link the person deleted in the fast drive: not shown again, and NOT deleted from the cloud.</summary>
    public HashSet<string> Hidden { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Local items and when their last complete upload ended.</summary>
    public Dictionary<string, DateTimeOffset> Uploaded { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static HybridState Load(string file)
    {
        try
        {
            if (File.Exists(file) && JsonSerializer.Deserialize<HybridState>(File.ReadAllText(file)) is { } s)
                return new HybridState
                {
                    Links = new(s.Links, StringComparer.OrdinalIgnoreCase),
                    Hidden = new(s.Hidden, StringComparer.OrdinalIgnoreCase),
                    Uploaded = new(s.Uploaded, StringComparer.OrdinalIgnoreCase),
                };
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { /* start again: links are found on disk anyway */ }
        return new HybridState();
    }

    public void Save(string file)
    {
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, file, overwrite: true);
    }
}

/// <summary>What one pass over the fast drive's folder did.</summary>
public sealed record LayoutResult(int LinksMade, int LinksFixed, int LinksRemoved, IReadOnlyList<string> NewlyHidden,
    IReadOnlyList<string> LocalItems, IReadOnlyList<string> Failed, bool CloudSeen)
{
    public bool Changed => LinksMade + LinksFixed + LinksRemoved + NewlyHidden.Count > 0;
}

/// <summary>
/// Keeps the fast drive's folder in step with the cloud drive: one link per item at the top of the cloud drive, real
/// folders and files for what was made on this PC. It only ever makes and removes LINKS. A real folder or file is
/// never deleted here, and nothing is deleted in the cloud: when the person deletes a link, the cloud item is only
/// hidden from the fast drive (a link that vanishes for any other reason must not cost anyone their cloud files).
/// </summary>
public static class HybridLayout
{
    /// <summary>Names Windows or File Explorer put into a drive's top folder by themselves.</summary>
    public static bool IsSystemName(string name) =>
        name.StartsWith(".cloudssd-", StringComparison.OrdinalIgnoreCase) ||   // the app's own short-lived folders (the speed check's test files)
        name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) || name.StartsWith("$RECYCLE", StringComparison.OrdinalIgnoreCase);

    public static bool IsLink(FileSystemInfo item) => (item.Attributes & FileAttributes.ReparsePoint) != 0;

    public static LayoutResult Reconcile(string hybridRoot, string cloudRoot, HybridState state)
    {
        var failed = new List<string>();
        var hidden = new List<string>();
        int made = 0, corrected = 0, removed = 0;
        Directory.CreateDirectory(hybridRoot);

        var local = new DirectoryInfo(hybridRoot).EnumerateFileSystemInfos()
            .Where(i => !IsSystemName(i.Name)).ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        var localItems = local.Values.Where(i => !IsLink(i)).Select(i => i.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        // The cloud drive may be away (starting, reconnecting, no internet). Then the links stay as they are.
        Dictionary<string, FileSystemInfo>? cloud = null;
        try
        {
            if (Directory.Exists(cloudRoot))
                cloud = new DirectoryInfo(cloudRoot).EnumerateFileSystemInfos()
                    .Where(i => !IsSystemName(i.Name)).ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed.Add("cloud drive: " + e.Message); }
        if (cloud is null) return new LayoutResult(0, 0, 0, hidden, localItems, failed, false);

        // An empty answer while links exist is far more likely a listing that failed (seen for real on this drive:
        // a refused listing was cached as "empty" for hours) than every cloud folder having been deleted at once.
        var trustRemovals = cloud.Count > 0 || state.Links.Count == 0;

        // 1. A link of ours that is gone although its cloud item is still there: the person deleted it. Hide, do not delete.
        foreach (var name in state.Links.Keys.ToList())
        {
            if (local.ContainsKey(name)) continue;
            state.Links.Remove(name);
            if (cloud.ContainsKey(name) && state.Hidden.Add(name)) hidden.Add(name);
        }

        // 2. Every cloud item gets a link, unless the PC has its own item of that name (that one wins) or it is hidden.
        foreach (var (name, item) in cloud)
        {
            var path = Path.Combine(hybridRoot, name);
            var target = item.FullName;
            if (local.TryGetValue(name, out var here))
            {
                if (!IsLink(here)) continue;
                if (string.Equals(here.LinkTarget, target, StringComparison.OrdinalIgnoreCase)) { state.Links[name] = target; continue; }
                if (!state.Links.ContainsKey(name)) continue; // a link someone else made: not ours to change
                // The cloud drive moved to another letter: point our link at the new place.
                try { RemoveLink(here); MakeLink(path, target, item is DirectoryInfo); state.Links[name] = target; corrected++; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed.Add($"{name}: {e.Message}"); }
                continue;
            }
            if (state.Hidden.Contains(name)) continue;
            try { MakeLink(path, target, item is DirectoryInfo); state.Links[name] = target; made++; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed.Add($"{name}: {e.Message}"); }
        }

        // 3. A link of ours whose cloud item is gone (deleted on the phone or another PC): the link goes too.
        if (trustRemovals)
            foreach (var (name, here) in local)
            {
                if (!IsLink(here) || cloud.ContainsKey(name) || !state.Links.ContainsKey(name)) continue;
                try { RemoveLink(here); state.Links.Remove(name); removed++; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed.Add($"{name}: {e.Message}"); }
            }

        // A hidden name whose cloud item no longer exists needs no memory any more.
        if (trustRemovals) state.Hidden.RemoveWhere(n => !cloud.ContainsKey(n));

        return new LayoutResult(made, corrected, removed, hidden, localItems, failed, true);
    }

    /// <summary>Shows a hidden cloud item in the fast drive again (the next pass makes its link).</summary>
    public static bool Unhide(HybridState state, string name) => state.Hidden.Remove(name);

    private static void MakeLink(string path, string target, bool directory)
    {
        if (directory) Directory.CreateSymbolicLink(path, target);
        else File.CreateSymbolicLink(path, target);
    }

    /// <summary>Removes the link itself, never what it points at.</summary>
    private static void RemoveLink(FileSystemInfo link)
    {
        if (!IsLink(link)) throw new IOException($"{link.Name} is not a link; it is left alone.");
        if (link is DirectoryInfo) Directory.Delete(link.FullName, recursive: false);
        else File.Delete(link.FullName);
    }
}
