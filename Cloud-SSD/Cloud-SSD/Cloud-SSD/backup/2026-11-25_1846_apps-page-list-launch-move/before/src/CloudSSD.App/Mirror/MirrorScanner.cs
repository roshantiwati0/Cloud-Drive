using CloudSSD.Core;

namespace CloudSSD.App.Mirror;

/// <summary>
/// What one look at the disk found. <see cref="Gone"/> are files that have a copy in the cloud and are no longer on
/// the PC; when there are suspiciously many, <see cref="GoneHeld"/> is set and nothing is done about them until the
/// person says so (a disk that did not show up must not empty the cloud copy).
/// </summary>
public sealed record MirrorScanResult(long Scan, long Files, long Bytes, int FoldersNotRead, IReadOnlyList<MirrorFile> Gone, bool GoneHeld);

/// <summary>Walks the PC's folders and tells <see cref="MirrorState"/> what is there now.</summary>
public static class MirrorScanner
{
    /// <summary>More files gone at once than this, and more than a fifth of all copies: held for a question.</summary>
    public const int GoneWithoutAsking = 500;

    /// <summary>
    /// Walks <paramref name="roots"/> (drive roots for a full look; the profile folder for a quick one). Folders above
    /// <paramref name="maxTier"/> are not entered. Never follows links, never enters what <see cref="MirrorScope"/> leaves out.
    /// </summary>
    public static MirrorScanResult Scan(MirrorState state, MirrorPlaces places, MirrorSettings settings, IEnumerable<string> roots, int maxTier,
        CancellationToken ct = default)
    {
        long scan = state.NewScan();
        long files = 0, bytes = 0;
        int notRead = 0;
        var readWhole = new List<string>();
        var batch = new List<MirrorFile>(4096);
        var stack = new Stack<string>();
        foreach (var r in roots.Reverse()) stack.Push(r.Length > 3 ? r.TrimEnd('\\', '/') : r);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            var inside = new List<MirrorFile>();
            var folders = new List<string>();
            bool leftSome = false;   // files of a tier this look does not cover: the folder then says nothing about what is gone
            try
            {
                foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    if ((e.Attributes & FileAttributes.Directory) != 0)
                    {
                        if (MirrorScope.SkipFolder(e.FullName, e.Attributes, places, settings)) continue;
                        if (MirrorScope.TierOf(e.FullName, places) > maxTier && !MayHoldLowerTier(e.FullName, places)) continue;
                        folders.Add(e.FullName);
                    }
                    else if (e is FileInfo f)
                    {
                        if (MirrorScope.SkipFile(f.FullName, f.Attributes)) continue;
                        int tier = MirrorScope.TierOf(f.FullName, places);
                        if (tier > maxTier) { leftSome = true; continue; }
                        inside.Add(new MirrorFile(f.FullName, f.Length, f.LastWriteTimeUtc.Ticks, tier));
                    }
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                // Windows does not let this folder be read (or it went away while reading): its files are left as they
                // were known, and nothing in it counts as gone.
                notRead++;
                continue;
            }
            if (!leftSome) readWhole.Add(dir);
            foreach (var f in inside) { files++; bytes += f.Size; }
            batch.AddRange(inside);
            if (batch.Count >= 4096) { state.Seen(batch, scan); batch.Clear(); }
            for (int i = folders.Count - 1; i >= 0; i--) stack.Push(folders[i]);
        }
        state.Seen(batch, scan);

        var gone = state.Missing(scan, readWhole);
        var withCopy = gone.Where(g => g.Size >= 0).ToList();
        bool held = withCopy.Count > GoneWithoutAsking && withCopy.Count > state.UploadedCount() / 5;
        // Files that never got a copy are simply forgotten; the others are the caller's to move to the cloud's trash.
        state.Forget(gone.Where(g => g.Size < 0).Select(g => g.Path).ToList());
        return new MirrorScanResult(scan, files, bytes, notRead, withCopy, held);
    }

    /// <summary>A folder of a higher tier that still has lower-tier folders inside (C:\Users holds the profile; a drive root holds everything).</summary>
    private static bool MayHoldLowerTier(string folder, MirrorPlaces p) =>
        MirrorScope.IsUnder(p.Profile, folder) || p.KnownFolders.Any(k => MirrorScope.IsUnder(k, folder));
}
