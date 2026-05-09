using System;
using System.Collections.Generic;
using Npgsql;
using NoteApp.Core;

namespace NoteApp.Infrastructure
{
    public class NoteRepository
    {
        private readonly DatabaseFactory _db;
        public NoteRepository(DatabaseFactory db) => _db = db;

        public int SaveNote(Note note)
        {
            try
            {
                using var conn = _db.CreateConnection();
                conn.Open();
                using var cmd = new NpgsqlCommand(
                    "INSERT INTO notes (user_id, content, created_at) VALUES (@u, @c, @t) RETURNING id", conn);
                cmd.Parameters.AddWithValue("u", note.UserId);
                cmd.Parameters.AddWithValue("c", note.Content);
                cmd.Parameters.AddWithValue("t", DateTime.UtcNow);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch (Exception ex) { AppLogger.DbError(ex.Message); throw; }
        }

        public List<Note> GetByUserId(int userId, DateTime? from, DateTime? to, int limit)
        {
            var notes = new List<Note>();
            try
            {
                using var conn = _db.CreateConnection();
                conn.Open();
                var sql = @"SELECT n.id, n.user_id, u.username, n.content, n.created_at
                            FROM notes n JOIN users u ON u.id = n.user_id
                            WHERE n.user_id = @uid
                              AND (@from::TIMESTAMPTZ IS NULL OR n.created_at >= @from)
                              AND (@to::TIMESTAMPTZ   IS NULL OR n.created_at <= @to)
                            ORDER BY n.created_at DESC
                            LIMIT @lim";
                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("uid",  userId);
                cmd.Parameters.AddWithValue("from", (object?)from ?? DBNull.Value);
                cmd.Parameters.AddWithValue("to",   (object?)to   ?? DBNull.Value);
                cmd.Parameters.AddWithValue("lim",  limit);
                using var r = cmd.ExecuteReader();
                while (r.Read()) notes.Add(new Note {
                    Id = r.GetInt32(0), UserId = r.GetInt32(1),
                    Username = r.GetString(2), Content = r.GetString(3),
                    CreatedAt = r.GetDateTime(4)
                });
            }
            catch (Exception ex) { AppLogger.DbError(ex.Message); }
            return notes;
        }

        public Note? GetById(int id)
        {
            try
            {
                using var conn = _db.CreateConnection();
                conn.Open();
                using var cmd = new NpgsqlCommand(
                    "SELECT n.id, n.user_id, u.username, n.content, n.created_at " +
                    "FROM notes n JOIN users u ON u.id=n.user_id WHERE n.id=@id", conn);
                cmd.Parameters.AddWithValue("id", id);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;
                return new Note {
                    Id = r.GetInt32(0), UserId = r.GetInt32(1),
                    Username = r.GetString(2), Content = r.GetString(3),
                    CreatedAt = r.GetDateTime(4)
                };
            }
            catch (Exception ex) { AppLogger.DbError(ex.Message); return null; }
        }

        public List<Note> GetAll(DateTime? from, DateTime? to, int limit)
        {
            var notes = new List<Note>();
            try
            {
                using var conn = _db.CreateConnection();
                conn.Open();
                var sql = @"SELECT n.id, n.user_id, u.username, n.content, n.created_at
                            FROM notes n JOIN users u ON u.id = n.user_id
                            WHERE (@from::TIMESTAMPTZ IS NULL OR n.created_at >= @from)
                              AND (@to::TIMESTAMPTZ   IS NULL OR n.created_at <= @to)
                            ORDER BY n.created_at DESC LIMIT @lim";
                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("from", (object?)from ?? DBNull.Value);
                cmd.Parameters.AddWithValue("to",   (object?)to   ?? DBNull.Value);
                cmd.Parameters.AddWithValue("lim",  limit);
                using var r = cmd.ExecuteReader();
                while (r.Read()) notes.Add(new Note {
                    Id = r.GetInt32(0), UserId = r.GetInt32(1),
                    Username = r.GetString(2), Content = r.GetString(3),
                    CreatedAt = r.GetDateTime(4)
                });
            }
            catch (Exception ex) { AppLogger.DbError(ex.Message); }
            return notes;
        }
    }
}
