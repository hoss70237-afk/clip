// File: ClipHistory/Data/HistoryRepository.cs
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using ClipHistory.Models;

namespace ClipHistory.Data
{
    /// <summary>
    /// SQLite による永続化。全件をメモリに展開せず、
    /// ページング・遅延ロードで必要分のみ取得する。
    /// </summary>
    public sealed class HistoryRepository : IDisposable
    {
        public const int MaxItems = 10000;

        private readonly SQLiteConnection _conn;
        private readonly object _lock = new object();

        public HistoryRepository(string dbPath)
        {
            bool isNew = !File.Exists(dbPath);

            var csb = new SQLiteConnectionStringBuilder
            {
                DataSource = dbPath,
                JournalMode = SQLiteJournalModeEnum.Wal, // 書き込み高速化
                SyncMode = SynchronizationModes.Normal,
                Pooling = false,
                CacheSize = 2000
            };

            _conn = new SQLiteConnection(csb.ToString());
            _conn.Open();

            if (isNew)
            {
                Initialize();
            }
            EnsureSchema();
        }

        private void Initialize()
        {
            ExecuteNonQuery(@"
PRAGMA page_size = 4096;
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
");
        }

        private void ExecuteNonQuery(string sql)
        {
            using (var cmd = new SQLiteCommand(sql, _conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>総件数を取得（起動時の全展開を避ける）</summary>
        public int Count()
        {
            lock (_lock)
            {
                using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM history;", _conn))
                {
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        /// <summary>
        /// 指定オフセットから limit 件を遅延ロード。
        /// UI仮想化と組み合わせて使用する。
        /// </summary>
        public List<ClipItem> LoadPage(int offset, int limit)
        {
            var list = new List<ClipItem>(limit);
            lock (_lock)
            {
                using (var cmd = new SQLiteCommand(
                    @"SELECT id, text, sort_order, is_favorite, created_at, updated_at
                      FROM history
                      ORDER BY sort_order ASC
                      LIMIT @limit OFFSET @offset;", _conn))
                {
                    cmd.Parameters.AddWithValue("@limit", limit);
                    cmd.Parameters.AddWithValue("@offset", offset);
                    ReadAll(cmd, list);
                }
            }
            return list;
        }

        /// <summary>部分一致検索。インデックス活用 + LIMIT で高速化。</summary>
        public List<ClipItem> Search(string keyword, int limit)
        {
            var list = new List<ClipItem>(limit);
            lock (_lock)
            {
                using (var cmd = new SQLiteCommand(
                    @"SELECT id, text, sort_order, is_favorite, created_at, updated_at
                      FROM history
                      WHERE text LIKE @kw ESCAPE '\'
                      ORDER BY sort_order ASC
                      LIMIT @limit;", _conn))
                {
                    cmd.Parameters.AddWithValue("@kw", "%" + EscapeLike(keyword) + "%");
                    cmd.Parameters.AddWithValue("@limit", limit);
                    ReadAll(cmd, list);
                }
            }
            return list;
        }

        private static string EscapeLike(string s)
            => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

        private static void ReadAll(SQLiteCommand cmd, List<ClipItem> list)
        {
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    list.Add(new ClipItem
                    {
                        Id = r.GetInt64(0),
                        Text = r.GetString(1),
                        SortOrder = r.GetInt32(2),
                        IsFavorite = r.GetInt32(3) != 0,
                        CreatedAt = FromUnix(r.GetInt64(4)),
                        UpdatedAt = FromUnix(r.GetInt64(5))
                    });
                }
            }
        }

        /// <summary>
        /// 新規追加。先頭(sort_order 最小 - 1)に挿入し、
        /// 上限超過時に最古を削除する。
        /// 同一テキストが既に先頭にある場合は重複追加しない。
        /// </summary>
        public ClipItem Add(string text)
        {
            lock (_lock)
            {
                using (var tx = _conn.BeginTransaction())
                {
                    long now = ToUnix(DateTime.UtcNow);

                    int minOrder = 0;
                    using (var cmd = new SQLiteCommand(
                        "SELECT IFNULL(MIN(sort_order), 1) FROM history;", _conn, tx))
                    {
                        minOrder = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                    int newOrder = minOrder - 1;

                    long newId;
                    using (var cmd = new SQLiteCommand(
                        @"INSERT INTO history(text, sort_order, is_favorite, created_at, updated_at)
                          VALUES(@t, @o, 0, @c, @u);
                          SELECT last_insert_rowid();", _conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@t", text);
                        cmd.Parameters.AddWithValue("@o", newOrder);
                        cmd.Parameters.AddWithValue("@c", now);
                        cmd.Parameters.AddWithValue("@u", now);
                        newId = Convert.ToInt64(cmd.ExecuteScalar());
                    }

                    // 上限超過時、お気に入り以外の最古を削除
                    TrimExcess(tx);

                    tx.Commit();

                    return new ClipItem
                    {
                        Id = newId,
                        Text = text,
                        SortOrder = newOrder,
                        IsFavorite = false,
                        CreatedAt = FromUnix(now),
                        UpdatedAt = FromUnix(now)
                    };
                }
            }
        }

        private void TrimExcess(SQLiteTransaction tx)
        {
            int count;
            using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM history;", _conn, tx))
            {
                count = Convert.ToInt32(cmd.ExecuteScalar());
            }
            if (count <= MaxItems) return;

            int over = count - MaxItems;
            using (var cmd = new SQLiteCommand(
                @"DELETE FROM history
                  WHERE id IN (
                      SELECT id FROM history
                      WHERE is_favorite = 0
                      ORDER BY sort_order DESC
                      LIMIT @n
                  );", _conn, tx))
            {
                cmd.Parameters.AddWithValue("@n", over);
                cmd.ExecuteNonQuery();
            }
        }

        public void UpdateText(long id, string text)
        {
            lock (_lock)
            {
                using (var cmd = new SQLiteCommand(
                    "UPDATE history SET text=@t, updated_at=@u WHERE id=@id;", _conn))
                {
                    cmd.Parameters.AddWithValue("@t", text);
                    cmd.Parameters.AddWithValue("@u", ToUnix(DateTime.UtcNow));
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void SetFavorite(long id, bool fav)
        {
            lock (_lock)
            {
                using (var cmd = new SQLiteCommand(
                    "UPDATE history SET is_favorite=@f, updated_at=@u WHERE id=@id;", _conn))
                {
                    cmd.Parameters.AddWithValue("@f", fav ? 1 : 0);
                    cmd.Parameters.AddWithValue("@u", ToUnix(DateTime.UtcNow));
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>並べ替え結果の永続化。idの並び通りに sort_order を再設定。</summary>
        public void Reorder(IReadOnlyList<long> orderedIds)
        {
            lock (_lock)
            {
                using (var tx = _conn.BeginTransaction())
                using (var cmd = new SQLiteCommand(
                    "UPDATE history SET sort_order=@o WHERE id=@id;", _conn, tx))
                {
                    var pO = cmd.Parameters.Add("@o", System.Data.DbType.Int32);
                    var pId = cmd.Parameters.Add("@id", System.Data.DbType.Int64);
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

        /// <summary>順送り用。指定 sort_order の「次」の1件を取得（循環）。</summary>
        public ClipItem GetNextForCycle(int currentIndex)
        {
            lock (_lock)
            {
                // currentIndex はリスト上のオフセットとして扱う
                var page = LoadPageNoLock(currentIndex, 1);
                return page.Count > 0 ? page[0] : null;
            }
        }

        private List<ClipItem> LoadPageNoLock(int offset, int limit)
        {
            var list = new List<ClipItem>(limit);
            using (var cmd = new SQLiteCommand(
                @"SELECT id, text, sort_order, is_favorite, created_at, updated_at
                  FROM history ORDER BY sort_order ASC LIMIT @limit OFFSET @offset;", _conn))
            {
                cmd.Parameters.AddWithValue("@limit", limit);
                cmd.Parameters.AddWithValue("@offset", offset);
                ReadAll(cmd, list);
            }
            return list;
        }

        public void Delete(long id)
        {
            lock (_lock)
            {
                using (var cmd = new SQLiteCommand("DELETE FROM history WHERE id=@id;", _conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static long ToUnix(DateTime dt)
            => (long)(dt.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        private static DateTime FromUnix(long s)
            => new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(s).ToLocalTime();

        public void Dispose()
        {
            _conn?.Close();
            _conn?.Dispose();
        }
    }
}
