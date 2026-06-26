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

CREATE TABLE IF NOT EXISTS templates (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    title       TEXT    NOT NULL,
    text        TEXT    NOT NULL,
    sort_order  INTEGER NOT NULL,
    created_at  INTEGER NOT NULL,
    updated_at  INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_tpl_sort ON templates(sort_order);
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

        public List<DisplayItem> SearchHistory(string keyword, int limit, bool onlyFavorites)
        {
            var list = new List<DisplayItem>(limit);
            lock (_lock)
            {
                string sql = "SELECT id, text, sort_order, is_favorite FROM history WHERE text LIKE @kw ESCAPE '\\'";
                if (onlyFavorites) sql += " AND is_favorite = 1";
                sql += " ORDER BY sort_order ASC LIMIT @limit;";

                using (var cmd = new SqliteCommand(sql, _conn))
                {
                    cmd.Parameters.AddWithValue("@kw", "%" + EscapeLike(keyword) + "%");
                    cmd.Parameters.AddWithValue("@limit", limit);
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
                    string text = r.GetString(1);
                    list.Add(new DisplayItem
                    {
                        Id = r.GetInt64(0),
                        IsTemplate = false,
                        DisplayText = text,
                        FullText = text,
                        SortOrder = r.GetInt32(2),
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
                    long now = ToUnix(DateTime.UtcNow);

                    // 既に同じテキストが存在する場合は削除して重複を防ぐ
                    long existingId = 0;
                    using (var cmd = new SqliteCommand("SELECT id FROM history WHERE text=@t LIMIT 1;", _conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@t", text);
                        var res = cmd.ExecuteScalar();
                        if (res != null) existingId = Convert.ToInt64(res);
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

                    long newId;
                    using (var cmd = new SqliteCommand(
                        @"INSERT INTO history(text, sort_order, is_favorite, created_at, updated_at)
                          VALUES(@t, @o, 0, @c, @u); SELECT last_insert_rowid();", _conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@t", text);
                        cmd.Parameters.AddWithValue("@o", newOrder);
                        cmd.Parameters.AddWithValue("@c", now);
                        cmd.Parameters.AddWithValue("@u", now);
                        newId = Convert.ToInt64(cmd.ExecuteScalar());
                    }

                    TrimHistoryExcess(tx);
                    tx.Commit();

                    return new DisplayItem
                    {
                        Id = newId, IsTemplate = false, DisplayText = text, FullText = text,
                        SortOrder = newOrder, IsFavorite = false
                    };
                }
            }
        }

        private void TrimHistoryExcess(SqliteTransaction tx)
        {
            int count;
            using (var cmd = new SqliteCommand("SELECT COUNT(*) FROM history;", _conn, tx))
                count = Convert.ToInt32(cmd.ExecuteScalar());
            
            if (count <= MaxItems) return;

            int over = count - MaxItems;
            using (var cmd = new SqliteCommand(
                @"DELETE FROM history WHERE id IN (
                      SELECT id FROM history WHERE is_favorite = 0
                      ORDER BY sort_order DESC LIMIT @n );", _conn, tx))
            {
                cmd.Parameters.AddWithValue("@n", over);
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

        public void ReorderHistory(IReadOnlyList<long> orderedIds)
        {
            ReorderTable("history", orderedIds);
        }

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

        // ========================== 定型文機能 ==========================

        public List<DisplayItem> LoadTemplates()
        {
            var list = new List<DisplayItem>();
            lock (_lock)
            {
                using (var cmd = new SqliteCommand("SELECT id, title, text, sort_order FROM templates ORDER BY sort_order ASC;", _conn))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new DisplayItem
                        {
                            Id = r.GetInt64(0), IsTemplate = true,
                            DisplayText = r.GetString(1), FullText = r.GetString(2),
                            SortOrder = r.GetInt32(3), IsFavorite = false
                        });
                    }
                }
            }
            return list;
        }

        public DisplayItem AddTemplate(string title, string text)
        {
            lock (_lock)
            {
                using (var tx = _conn.BeginTransaction())
                {
                    int maxOrder = 0;
                    using (var cmd = new SqliteCommand("SELECT IFNULL(MAX(sort_order), -1) FROM templates;", _conn, tx))
                        maxOrder = Convert.ToInt32(cmd.ExecuteScalar());

                    long now = ToUnix(DateTime.UtcNow);
                    long newId;
                    int newOrder = maxOrder + 1;

                    using (var cmd = new SqliteCommand(
                        @"INSERT INTO templates(title, text, sort_order, created_at, updated_at)
                          VALUES(@title, @txt, @o, @c, @u); SELECT last_insert_rowid();", _conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@title", title);
                        cmd.Parameters.AddWithValue("@txt", text);
                        cmd.Parameters.AddWithValue("@o", newOrder);
                        cmd.Parameters.AddWithValue("@c", now);
                        cmd.Parameters.AddWithValue("@u", now);
                        newId = Convert.ToInt64(cmd.ExecuteScalar());
                    }
                    tx.Commit();

                    return new DisplayItem
                    {
                        Id = newId, IsTemplate = true, DisplayText = title, FullText = text, SortOrder = newOrder
                    };
                }
            }
        }

        public void UpdateTemplate(long id, string title, string text)
        {
            lock (_lock)
            {
                using (var cmd = new SqliteCommand("UPDATE templates SET title=@title, text=@txt, updated_at=@u WHERE id=@id;", _conn))
                {
                    cmd.Parameters.AddWithValue("@title", title);
                    cmd.Parameters.AddWithValue("@txt", text);
                    cmd.Parameters.AddWithValue("@u", ToUnix(DateTime.UtcNow));
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void DeleteTemplate(long id)
        {
            lock (_lock)
            {
                using (var cmd = new SqliteCommand("DELETE FROM templates WHERE id=@id;", _conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void ReorderTemplates(IReadOnlyList<long> orderedIds)
        {
            ReorderTable("templates", orderedIds);
        }

        // ========================== 共通ユーティリティ ==========================

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

        public void Dispose()
        {
            _conn?.Close();
            _conn?.Dispose();
        }
    }
}
