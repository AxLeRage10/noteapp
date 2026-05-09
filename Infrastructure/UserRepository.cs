using System;
using System.Collections.Generic;
using Npgsql;
using NoteApp.Core;

namespace NoteApp.Infrastructure
{
    public class UserRepository
    {
        private readonly DatabaseFactory _db;
        public UserRepository(DatabaseFactory db) => _db = db;

        public User? GetByUsername(string username)
        {
            try
            {
                using var conn = _db.CreateConnection();
                conn.Open();
                using var cmd = new NpgsqlCommand(
                    "SELECT id, username, password_hash, role, is_blocked, blocked_until, " +
                    "failed_attempts, lockout_end FROM users WHERE username = @u", conn);
                cmd.Parameters.AddWithValue("u", username);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) return null;
                return MapUser(reader);
            }
            catch (Exception ex) { AppLogger.DbError(ex.Message); return null; }
        }

        public void UpdateFailedAttempts(string username, int count, DateTime? lockoutEnd)
        {
            try
            {
                using var conn = _db.CreateConnection();
                conn.Open();
                using var cmd = new NpgsqlCommand(
                    "UPDATE users SET failed_attempts=@c, lockout_end=@l WHERE username=@u", conn);
                cmd.Parameters.AddWithValue("c", count);
                cmd.Parameters.AddWithValue("l", (object?)lockoutEnd ?? DBNull.Value);
                cmd.Parameters.AddWithValue("u", username);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { AppLogger.DbError(ex.Message); }
        }

        public void SaveUser(User user)
        {
            try
            {
                using var conn = _db.CreateConnection();
                conn.Open();
                using var cmd = new NpgsqlCommand(@"
                    INSERT INTO users (username, password_hash, role, is_blocked, blocked_until)
                    VALUES (@u, @ph, @r, @b, @bu)
                    ON CONFLICT (username) DO UPDATE
                    SET password_hash=EXCLUDED.password_hash,
                        role=EXCLUDED.role,
                        is_blocked=EXCLUDED.is_blocked,
                        blocked_until=EXCLUDED.blocked_until", conn);
                cmd.Parameters.AddWithValue("u",  user.Username);
                cmd.Parameters.AddWithValue("ph", user.PasswordHash);
                cmd.Parameters.AddWithValue("r",  user.Role.ToString().ToLower());
                cmd.Parameters.AddWithValue("b",  user.IsBlocked);
                cmd.Parameters.AddWithValue("bu", (object?)user.BlockedUntil ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { AppLogger.DbError(ex.Message); throw; }
        }

        public void DeleteUser(string username)
        {
            try
            {
                using var conn = _db.CreateConnection();
                conn.Open();
                using var cmd = new NpgsqlCommand("DELETE FROM users WHERE username=@u", conn);
                cmd.Parameters.AddWithValue("u", username);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { AppLogger.DbError(ex.Message); throw; }
        }

        public bool UsernameExists(string username)
        {
            try
            {
                using var conn = _db.CreateConnection();
                conn.Open();
                using var cmd = new NpgsqlCommand(
                    "SELECT COUNT(1) FROM users WHERE username=@u", conn);
                cmd.Parameters.AddWithValue("u", username);
                return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
            }
            catch (Exception ex) { AppLogger.DbError(ex.Message); return false; }
        }

        public List<User> GetAll()
        {
            var result = new List<User>();
            try
            {
                using var conn = _db.CreateConnection();
                conn.Open();
                using var cmd = new NpgsqlCommand(
                    "SELECT id, username, password_hash, role, is_blocked, blocked_until, " +
                    "failed_attempts, lockout_end FROM users ORDER BY username", conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) result.Add(MapUser(reader));
            }
            catch (Exception ex) { AppLogger.DbError(ex.Message); }
            return result;
        }

        private static User MapUser(NpgsqlDataReader r) => new()
        {
            Id             = r.GetInt32(0),
            Username       = r.GetString(1),
            PasswordHash   = r.GetString(2),
            Role           = r.GetString(3) switch {
                "admin"    => UserRole.Admin,
                "operator" => UserRole.Operator,
                _          => UserRole.User
            },
            IsBlocked      = r.GetBoolean(4),
            BlockedUntil   = r.IsDBNull(5) ? null : r.GetDateTime(5),
            FailedAttempts = r.GetInt32(6),
            LockoutEnd     = r.IsDBNull(7) ? null : r.GetDateTime(7)
        };
    }
}
