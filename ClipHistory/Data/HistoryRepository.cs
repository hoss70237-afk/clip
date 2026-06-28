// File: ClipHistory/Data/HistoryRepository.cs
using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.IO;
using ClipHistory.Models;

namespace ClipHistory.Data
{
    public sealed class HistoryRepository : IDisposable
    {
        public const int MaxItems = 10000;
        private readonly SqliteConnection _conn;
        private readonly object _lock = new object();

        public HistoryRepository(string dbPath)
        {
            bool isNew = !File.Exists(dbPath);
            var csb = new SqliteConnectionStringBuilder { DataSource = dbPath };
            _conn = new SqliteConnection(csb.ToString());
            _conn.Open();

            if (isNew) Initialize();
            EnsureSchema();
        }

        private void Initialize()
        {
            ExecuteNonQuery(@"
PRAGMA page_size = 4096;
PRAGMA cache_size = 2000;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
");
        }

        private void EnsureSchema()
        {
            ExecuteNonQuery(@"
CREATE TABLE IF NOT EXISTS history (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    text        TEXT    NOT NULL,
    sort_order  INTEGER NOT NULL,
    is_favorite INTEGER NOT NULL DEFAULT 0,
    created_at  INTEGER NOT NULL,
    updated_at  INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_sort  ON history(sort_order);
CREATE INDEX IF NOT EXISTS idx_text  ON history(text);

CREATE TABLE IF NOT EXISTS template_sets (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT    NOT NULL,
    sort_order  INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS template_items (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    set_id      INTEGER NOT NULL,
    text        TEXT    NOT NULL,
    sort_order  INTEGER NOT NULL,
    created_at  INTEGER NOT NULL,
    updated_at  INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_tpl_set ON template_items(set_id);
");
        }

        private void ExecuteNonQuery(string sql)
        {
            using (var cmd = new SqliteCommand(sql, _conn)) cmd.ExecuteNonQuery();
        }

        // ========================== 履歴機能 ==========================
        public int CountHistory()
        {
            lock (_lock)
            {
                using (var cmd = new SqliteCommand("SELECT COUNT(*) FROM history;", _conn))
                    return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public List<DisplayItem> LoadHistoryPage(int offset, int limit, bool onlyFavorites)
        {
            var list = new List<DisplayItem>(limit);
            lock (_lock)
            {
                string sql = "SELECT id, text, sort_order, is_favorite FROM history";
                if (onlyFavorites) sql += " WHERE is_favorite = 1";
                sql += " ORDER BY sort_order ASC LIMIT @limit OFFSET @offset;";

                using (var cmd = new SqliteCommand(sql, _conn))
                {
                    cmd.Parameters.AddWithValue("@limit", limit);
                    cmd.Parameters.AddWithValue("@offset", offset);
                    ReadHistory(cmd, list);
                }
            }
            return list;
        }

        public List<DisplayItem> SearchHistory(string keyword, int limit, int offset, bool onlyFavorites)
        {
            var list = new List<DisplayItem>(limit);
            lock (_lock)
            {
                string sql = "SELECT id, text, sort_order, is_favorite FROM history WHERE text LIKE @kw ESCAPE '\\'";
                if (onlyFavorites) sql += " AND is_favorite = 1";
                sql += " ORDER BY sort_order ASC LIMIT @limit OFFSET @offset;";

                using (var cmd = new SqliteCommand(sql, _conn))
                {
                    cmd.Parameters.AddWithValue("@kw", "%" + EscapeLike(keyword) + "%");
                    cmd.Parameters.AddWithValue("@limit", limit);
                    cmd.Parameters.AddWithValue("@offset", offset);
                    ReadHistory(cmd, list);
                }
            }
            return list;
        }

        private static void ReadHistory(SqliteCommand cmd, List<DisplayItem> list)
        {
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    list.Add(new DisplayItem
                    {
                        Id = r.GetInt64(0), IsTemplate = false,
                        FullText = r.GetString(1), SortOrder = r.GetInt32(2),
                        IsFavorite = r.GetInt32(3) != 0
                    });
                }
            }
        }

        public DisplayItem AddHistory(string text)
        {
            lock (_lock)
            {
                using (var tx = _conn.BeginTransaction())
                {
                    long existingId = 0;
                    int existingFav = 0;
                    using (var cmd = new SqliteCommand("SELECT id, is_favorite FROM history WHERE text=@t LIMIT 1;", _conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@t", text);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                existingId = reader.GetInt64(0);
                                existingFav = reader.GetInt32(1);
                            }
                        }
                    }

                    if (existingId > 0)
                    {
                        using (var cmd = new SqliteCommand("DELETE FROM history WHERE id=@id;", _conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@id", existingId);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    int minOrder = 0;
                    using (var cmd = new SqliteCommand("SELECT IFNULL(MIN(sort_order), 1) FROM history;", _conn, tx))
                        minOrder = Convert.ToInt32(cmd.ExecuteScalar());
                    
                    int newOrder = minOrder - 1;
                    long now = ToUnix(DateTime.UtcNow);
                    long newId;
                    using (var cmd = new SqliteCommand(
                        @"INSERT INTO history(text, sort_order, is_favorite, created_at, updated_at)
                          VALUES(@t, @o, @f, @c, @u); SELECT last_insert_rowid();", _conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@t", text);
                        cmd.Parameters.AddWithValue("@o", newOrder);
                        cmd.Parameters.AddWithValue("@f", existingFav);
                        cmd.Parameters.AddWithValue("@c", now);
                        cmd.Parameters.AddWithValue("@u", now);
                        newId = Convert.ToInt64(cmd.ExecuteScalar());
                    }

                    TrimHistoryExcess(tx);
                    tx.Commit();
                    return new DisplayItem { Id = newId, IsTemplate = false, FullText = text, SortOrder = newOrder, IsFavorite = existingFav != 0 };
                }
            }
        }

        private void TrimHistoryExcess(SqliteTransaction tx)
        {
            using (var cmd = new SqliteCommand("SELECT COUNT(*) FROM history;", _conn, tx))
            {
                if (Convert.ToInt32(cmd.ExecuteScalar()) <= MaxItems) return;
            }

            using (var cmd = new SqliteCommand(
                @"DELETE FROM history WHERE id IN (
                      SELECT id FROM history WHERE is_favorite = 0
                      ORDER BY sort_order DESC LIMIT (SELECT COUNT(*)-@m FROM history) );", _conn, tx))
            {
                cmd.Parameters.AddWithValue("@m", MaxItems);
                cmd.ExecuteNonQuery();
            }
        }

        public void UpdateHistoryText(long id, string text)
        {
            lock (_lock)
            {
                using (var cmd = new SqliteCommand("UPDATE history SET text=@t, updated_at=@u WHERE id=@id;", _conn))
                {
                    cmd.Parameters.AddWithValue("@t", text);
                    cmd.Parameters.AddWithValue("@u", ToUnix(DateTime.UtcNow));
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void SetHistoryFavorite(long id, bool fav)
        {
            lock (_lock)
            {
                using (var cmd = new SqliteCommand("UPDATE history SET is_favorite=@f, updated_at=@u WHERE id=@id;", _conn))
                {
                    cmd.Parameters.AddWithValue("@f", fav ? 1 : 0);
                    cmd.Parameters.AddWithValue("@u", ToUnix(DateTime.UtcNow));
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void ReorderHistory(IReadOnlyList<long> orderedIds) => ReorderTable("history", orderedIds);

        public void DeleteHistory(long id)
        {
            lock (_lock)
            {
                using (var cmd = new SqliteCommand("DELETE FROM history WHERE id=@id;", _conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public DisplayItem GetNextForCycle(int currentIndex)
        {
            lock (_lock)
            {
                var list = new List<DisplayItem>(1);
                using (var cmd = new SqliteCommand("SELECT id, text, sort_order, is_favorite FROM history ORDER BY sort_order ASC LIMIT 1 OFFSET @offset;", _conn))
                {
                    cmd.Parameters.AddWithValue("@offset", currentIndex);
                    ReadHistory(cmd, list);
                }
                return list.Count > 0 ? list[0] : null;
            }
        }

        // ========================== 定型文（セット）機能 ==========================
        public List<TemplateSet> LoadTemplateSets()
        {
            var list = new List<TemplateSet>();
            lock (_lock)
            {
                using (var cmd = new SqliteCommand("SELECT id, name FROM template_sets ORDER BY sort_order ASC;", _conn))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) list.Add(new TemplateSet { Id = r.GetInt64(0), Name = r.GetString(1) });
                }
            }
            return list;
        }

        public TemplateSet AddTemplateSet(string name)
        {
            lock (_lock)
            {
                using (var tx = _conn.BeginTransaction())
                {
                    int order = 0;
                    using (var cmd = new SqliteCommand("SELECT IFNULL(MAX(sort_order), -1) FROM template_sets;", _conn, tx))
                        order = Convert.ToInt32(cmd.ExecuteScalar()) + 1;

                    long newId;
                    using (var cmd = new SqliteCommand("INSERT INTO template_sets(name, sort_order) VALUES(@n, @o); SELECT last_insert_rowid();", _conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@n", name);
                        cmd.Parameters.AddWithValue("@o", order);
                        newId = Convert.ToInt64(cmd.ExecuteScalar());
                    }
                    tx.Commit();
                    return new TemplateSet { Id = newId, Name = name };
                }
            }
        }

        public void DeleteTemplateSet(long setId)
        {
            lock (_lock)
            {
                using (var tx = _conn.BeginTransaction())
                {
                    using (var cmd = new SqliteCommand("DELETE FROM template_items WHERE set_id=@id;", _conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@id", setId);
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = new SqliteCommand("DELETE FROM template_sets WHERE id=@id;", _conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@id", setId);
                        cmd.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
            }
        }

        public List<DisplayItem> LoadTemplatesBySet(long setId)
        {
            var list = new List<DisplayItem>();
            lock (_lock)
            {
                using (var cmd = new SqliteCommand("SELECT id, text, sort_order FROM template_items WHERE set_id=@s ORDER BY sort_order ASC;", _conn))
                {
                    cmd.Parameters.AddWithValue("@s", setId);
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            list.Add(new DisplayItem
                            {
                                Id = r.GetInt64(0), IsTemplate = true, FullText = r.GetString(1),
                                SortOrder = r.GetInt32(2), IsFavorite = false
                            });
                        }
                    }
                }
            }
            return list;
        }

        public DisplayItem AddTemplateItem(long setId, string text)
        {
            lock (_lock)
            {
                using (var tx = _conn.BeginTransaction())
                {
                    int order = 0;
                    using (var cmd = new SqliteCommand("SELECT IFNULL(MAX(sort_order), -1) FROM template_items WHERE set_id=@s;", _conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@s", setId);
                        order = Convert.ToInt32(cmd.ExecuteScalar()) + 1;
                    }

                    long now = ToUnix(DateTime.UtcNow);
                    long newId;
                    using (var cmd = new SqliteCommand(
                        @"INSERT INTO template_items(set_id, text, sort_order, created_at, updated_at)
                          VALUES(@s, @t, @o, @c, @u); SELECT last_insert_rowid();", _conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@s", setId);
                        cmd.Parameters.AddWithValue("@t", text);
                        cmd.Parameters.AddWithValue("@o", order);
                        cmd.Parameters.AddWithValue("@c", now);
                        cmd.Parameters.AddWithValue("@u", now);
                        newId = Convert.ToInt64(cmd.ExecuteScalar());
                    }
                    tx.Commit();
                    return new DisplayItem { Id = newId, IsTemplate = true, FullText = text, SortOrder = order };
                }
            }
        }

        public void UpdateTemplateItem(long id, string text)
        {
            lock (_lock)
            {
                using (var cmd = new SqliteCommand("UPDATE template_items SET text=@t, updated_at=@u WHERE id=@id;", _conn))
                {
                    cmd.Parameters.AddWithValue("@t", text);
                    cmd.Parameters.AddWithValue("@u", ToUnix(DateTime.UtcNow));
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void DeleteTemplateItem(long id)
        {
            lock (_lock)
            {
                using (var cmd = new SqliteCommand("DELETE FROM template_items WHERE id=@id;", _conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void ReorderTemplates(IReadOnlyList<long> orderedIds) => ReorderTable("template_items", orderedIds);

        private void ReorderTable(string table, IReadOnlyList<long> orderedIds)
        {
            lock (_lock)
            {
                using (var tx = _conn.BeginTransaction())
                using (var cmd = new SqliteCommand($"UPDATE {table} SET sort_order=@o WHERE id=@id;", _conn, tx))
                {
                    var pO = cmd.Parameters.Add("@o", SqliteType.Integer);
                    var pId = cmd.Parameters.Add("@id", SqliteType.Integer);
                    for (int i = 0; i < orderedIds.Count; i++)
                    {
                        pO.Value = i;
                        pId.Value = orderedIds[i];
                        cmd.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
            }
        }

        private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        private static long ToUnix(DateTime dt) => (long)(dt.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        public void Dispose() { _conn?.Close(); _conn?.Dispose(); }
    }
}
