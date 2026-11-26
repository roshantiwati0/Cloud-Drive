using Microsoft.Data.Sqlite;

namespace CloudSSD.App.Mirror;

/// <summary>A file the mirror knows: where it is, how big, when changed (UTC ticks), and its tier.</summary>
public sealed record MirrorFile(string Path, long Size, long Modified, int Tier);

/// <summary>How far one tier is: what is on the PC, what is in the cloud, what could not be copied.</summary>
public sealed record MirrorTierTotal(int Tier, long Files, long Bytes, long UploadedFiles, long UploadedBytes, long Failed);

/// <summary>
/// What the mirror remembers about every file (a SQLite file in the app's data folder): its size and time as last
/// seen on the disk, and as last copied to the cloud. A file whose two pairs differ still has to go.
/// </summary>
public sealed class MirrorState : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly object _lock = new();

    public MirrorState(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        _db.Open();
        Exec("""
            PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;
            CREATE TABLE IF NOT EXISTS f(
              path TEXT PRIMARY KEY, dir TEXT NOT NULL, size INTEGER NOT NULL, mtime INTEGER NOT NULL, tier INTEGER NOT NULL,
              upsize INTEGER NOT NULL DEFAULT -1, upmtime INTEGER NOT NULL DEFAULT 0,
              err TEXT, errat INTEGER NOT NULL DEFAULT 0, seen INTEGER NOT NULL DEFAULT 0) WITHOUT ROWID;
            CREATE INDEX IF NOT EXISTS f_todo ON f(tier, size) WHERE upsize <> size OR upmtime <> mtime;
            CREATE INDEX IF NOT EXISTS f_dir ON f(dir);
            CREATE TABLE IF NOT EXISTS meta(k TEXT PRIMARY KEY, v TEXT NOT NULL);
            """);
    }

    private void Exec(string sql)
    {
        using var c = _db.CreateCommand();
        c.CommandText = sql;
        c.ExecuteNonQuery();
    }

    public string? Meta(string key)
    {
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = "SELECT v FROM meta WHERE k = $k";
            c.Parameters.AddWithValue("$k", key);
            return c.ExecuteScalar() as string;
        }
    }

    public void SetMeta(string key, string value)
    {
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = "INSERT INTO meta(k, v) VALUES($k, $v) ON CONFLICT(k) DO UPDATE SET v = excluded.v";
            c.Parameters.AddWithValue("$k", key);
            c.Parameters.AddWithValue("$v", value);
            c.ExecuteNonQuery();
        }
    }

    /// <summary>A new look at the disk begins: its number marks every file seen in it.</summary>
    public long NewScan()
    {
        long gen = long.TryParse(Meta("scan"), out var g) ? g + 1 : 1;
        SetMeta("scan", gen.ToString());
        return gen;
    }

    /// <summary>Files seen on the disk in scan <paramref name="scan"/>. A changed file loses the note of an earlier failure.</summary>
    public void Seen(IReadOnlyList<MirrorFile> files, long scan)
    {
        if (files.Count == 0) return;
        lock (_lock)
        {
            using var tx = _db.BeginTransaction();
            using var c = _db.CreateCommand();
            c.Transaction = tx;
            c.CommandText = """
                INSERT INTO f(path, dir, size, mtime, tier, seen) VALUES($p, $d, $s, $m, $t, $g)
                ON CONFLICT(path) DO UPDATE SET
                  err = CASE WHEN f.size <> excluded.size OR f.mtime <> excluded.mtime THEN NULL ELSE f.err END,
                  size = excluded.size, mtime = excluded.mtime, tier = excluded.tier, seen = excluded.seen
                """;
            var p = c.Parameters.Add("$p", SqliteType.Text);
            var d = c.Parameters.Add("$d", SqliteType.Text);
            var s = c.Parameters.Add("$s", SqliteType.Integer);
            var m = c.Parameters.Add("$m", SqliteType.Integer);
            var t = c.Parameters.Add("$t", SqliteType.Integer);
            c.Parameters.AddWithValue("$g", scan);
            foreach (var f in files)
            {
                p.Value = f.Path;
                d.Value = Path.GetDirectoryName(f.Path) ?? "";
                s.Value = f.Size;
                m.Value = f.Modified;
                t.Value = f.Tier;
                c.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    /// <summary>
    /// Files that were not seen in scan <paramref name="scan"/> although their folder was read whole in it: they are
    /// gone from the PC. A folder that could not be read (or was not looked at) says nothing about its files.
    /// </summary>
    public IReadOnlyList<MirrorFile> Missing(long scan, IReadOnlyCollection<string> foldersReadWhole)
    {
        var gone = new List<MirrorFile>();
        if (foldersReadWhole.Count == 0) return gone;
        lock (_lock)
        {
            using var tx = _db.BeginTransaction();
            using (var c = _db.CreateCommand())
            {
                c.Transaction = tx;
                c.CommandText = "CREATE TEMP TABLE IF NOT EXISTS whole(dir TEXT PRIMARY KEY) WITHOUT ROWID; DELETE FROM whole;";
                c.ExecuteNonQuery();
            }
            using (var c = _db.CreateCommand())
            {
                c.Transaction = tx;
                c.CommandText = "INSERT OR IGNORE INTO whole(dir) VALUES($d)";
                var d = c.Parameters.Add("$d", SqliteType.Text);
                foreach (var dir in foldersReadWhole) { d.Value = dir; c.ExecuteNonQuery(); }
            }
            using (var c = _db.CreateCommand())
            {
                c.Transaction = tx;
                c.CommandText = "SELECT path, upsize, upmtime, tier FROM f WHERE seen < $g AND dir IN (SELECT dir FROM whole)";
                c.Parameters.AddWithValue("$g", scan);
                using var r = c.ExecuteReader();
                while (r.Read()) gone.Add(new MirrorFile(r.GetString(0), r.GetInt64(1), r.GetInt64(2), r.GetInt32(3)));
            }
            tx.Commit();
        }
        return gone;
    }

    /// <summary>Drops files from the list (they are gone from the PC and their copies were dealt with).</summary>
    public void Forget(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        lock (_lock)
        {
            using var tx = _db.BeginTransaction();
            using var c = _db.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "DELETE FROM f WHERE path = $p";
            var p = c.Parameters.Add("$p", SqliteType.Text);
            foreach (var path in paths) { p.Value = path; c.ExecuteNonQuery(); }
            tx.Commit();
        }
    }

    /// <summary>
    /// The next files to copy: lowest tier first, small before big; at most <paramref name="count"/> files and about
    /// <paramref name="maxBytes"/>. A file that failed is offered again only after <paramref name="retryFailedBefore"/>
    /// (a time in UTC ticks: failures older than it).
    /// </summary>
    public IReadOnlyList<MirrorFile> Next(int maxTier, int count, long maxBytes, long retryFailedBefore)
    {
        var list = new List<MirrorFile>();
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = """
                SELECT path, size, mtime, tier FROM f
                WHERE (upsize <> size OR upmtime <> mtime) AND tier <= $t AND (err IS NULL OR errat < $e)
                ORDER BY tier, size LIMIT $n
                """;
            c.Parameters.AddWithValue("$t", maxTier);
            c.Parameters.AddWithValue("$e", retryFailedBefore);
            c.Parameters.AddWithValue("$n", count);
            using var r = c.ExecuteReader();
            long bytes = 0;
            while (r.Read())
            {
                var f = new MirrorFile(r.GetString(0), r.GetInt64(1), r.GetInt64(2), r.GetInt32(3));
                if (list.Count > 0 && bytes + f.Size > maxBytes) break;
                list.Add(f);
                bytes += f.Size;
            }
        }
        return list;
    }

    /// <summary>These files are in the cloud as they were seen (size and time).</summary>
    public void Uploaded(IReadOnlyList<MirrorFile> files)
    {
        if (files.Count == 0) return;
        lock (_lock)
        {
            using var tx = _db.BeginTransaction();
            using var c = _db.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "UPDATE f SET upsize = $s, upmtime = $m, err = NULL, errat = 0 WHERE path = $p";
            var p = c.Parameters.Add("$p", SqliteType.Text);
            var s = c.Parameters.Add("$s", SqliteType.Integer);
            var m = c.Parameters.Add("$m", SqliteType.Integer);
            foreach (var f in files) { p.Value = f.Path; s.Value = f.Size; m.Value = f.Modified; c.ExecuteNonQuery(); }
            tx.Commit();
        }
    }

    /// <summary>A file could not be copied: why (in-use, denied, missing, other) and when (UTC ticks).</summary>
    public void Failed(string path, string reason, long at)
    {
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = "UPDATE f SET err = $e, errat = $a WHERE path = $p";
            c.Parameters.AddWithValue("$e", reason);
            c.Parameters.AddWithValue("$a", at);
            c.Parameters.AddWithValue("$p", path);
            c.ExecuteNonQuery();
        }
    }

    /// <summary>Per tier: what is on the PC, what of it is in the cloud, and how many files could not be copied.</summary>
    public IReadOnlyList<MirrorTierTotal> Totals()
    {
        var list = new List<MirrorTierTotal>();
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = """
                SELECT tier, COUNT(*), COALESCE(SUM(size), 0),
                  COALESCE(SUM(CASE WHEN upsize = size AND upmtime = mtime THEN 1 ELSE 0 END), 0),
                  COALESCE(SUM(CASE WHEN upsize = size AND upmtime = mtime THEN size ELSE 0 END), 0),
                  COALESCE(SUM(CASE WHEN err IS NOT NULL AND (upsize <> size OR upmtime <> mtime) THEN 1 ELSE 0 END), 0)
                FROM f GROUP BY tier ORDER BY tier
                """;
            using var r = c.ExecuteReader();
            while (r.Read()) list.Add(new MirrorTierTotal(r.GetInt32(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4), r.GetInt64(5)));
        }
        return list;
    }

    /// <summary>How many files could not be copied, by reason.</summary>
    public IReadOnlyDictionary<string, long> FailedByReason()
    {
        var map = new Dictionary<string, long>();
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = "SELECT err, COUNT(*) FROM f WHERE err IS NOT NULL AND (upsize <> size OR upmtime <> mtime) GROUP BY err";
            using var r = c.ExecuteReader();
            while (r.Read()) map[r.GetString(0)] = r.GetInt64(1);
        }
        return map;
    }

    /// <summary>How many files have a copy in the cloud.</summary>
    public long UploadedCount()
    {
        lock (_lock)
        {
            using var c = _db.CreateCommand();
            c.CommandText = "SELECT COUNT(*) FROM f WHERE upsize >= 0";
            return (long)(c.ExecuteScalar() ?? 0L);
        }
    }

    public void Dispose()
    {
        lock (_lock) _db.Dispose();
    }
}
