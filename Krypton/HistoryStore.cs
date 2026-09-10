using Microsoft.Data.Sqlite;

namespace Krypton;

// SQLite backing store for omnibox completion hosts:
// %LocalAppData%\Krypton\history.db, table `hosts` with a UNIQUE domain.
// One connection per store, used only under MainForm's history lock: the
// connection is not thread-safe, and FrameLoadEnd learns on CEF threads
// while typing reads happen on the UI thread.
internal sealed class HistoryStore : IDisposable
{
    private readonly SqliteConnection _db;
    private bool _disposed;

    public HistoryStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _db = new SqliteConnection($"Data Source={path}");
        _db.Open();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS hosts (
              domain TEXT PRIMARY KEY,
              visits INTEGER NOT NULL DEFAULT 1,
              last_visit INTEGER NOT NULL DEFAULT (strftime('%s','now')),
              priority INTEGER NOT NULL DEFAULT 99
            );
            """;
        cmd.ExecuteNonQuery();
    }

    // Seeds in the given order (priority 0..n); existing rows untouched.
    public void Seed(IEnumerable<string> seeds)
    {
        using var tx = _db.BeginTransaction();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO hosts (domain, priority) VALUES ($d, $p);";
        var d = cmd.CreateParameter();
        d.ParameterName = "$d";
        cmd.Parameters.Add(d);
        var p = cmd.CreateParameter();
        p.ParameterName = "$p";
        cmd.Parameters.Add(p);
        int i = 0;
        foreach (string s in seeds)
        {
            d.Value = s;
            p.Value = i++;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // Seeds first (in order), then learned hosts in learn order.
    public List<string> LoadAll()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT domain FROM hosts ORDER BY priority ASC, rowid ASC;";
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(r.GetString(0));
        }
        return list;
    }

    // New hosts insert; duplicates hit the UNIQUE constraint and just bump
    // visits/last_visit instead of bloating the file.
    public void Upsert(string domain)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO hosts (domain, priority) VALUES ($d, 99)
            ON CONFLICT(domain) DO UPDATE SET
              visits = visits + 1,
              last_visit = strftime('%s','now');
            """;
        cmd.Parameters.AddWithValue("$d", domain);
        cmd.ExecuteNonQuery();
    }

    // One-time import of legacy history.txt lines; returns rows added.
    public int ImportLines(IEnumerable<string> lines)
    {
        int added = 0;
        using var tx = _db.BeginTransaction();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO hosts (domain, priority) VALUES ($d, 99);";
        var d = cmd.CreateParameter();
        d.ParameterName = "$d";
        cmd.Parameters.Add(d);
        foreach (string line in lines)
        {
            d.Value = line;
            added += cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return added;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _db.Dispose();
        }
    }
}
