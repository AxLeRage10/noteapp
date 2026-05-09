using System;
using Npgsql;
using NoteApp.Core;

namespace NoteApp.Infrastructure
{
    /// <summary>
    /// Фабрика соединений с PostgreSQL.
    /// Параметры берутся исключительно из переменных окружения.
    /// </summary>
    public class DatabaseFactory
    {
        private readonly string _connectionString;

        public DatabaseFactory()
        {
            var host     = GetEnv("NOTEAPP_DB_HOST");
            var port     = GetEnv("NOTEAPP_DB_PORT", "5432");
            var db       = GetEnv("NOTEAPP_DB_NAME");
            var user     = GetEnv("NOTEAPP_DB_USER");
            var password = GetEnv("NOTEAPP_DB_PASSWORD");

            _connectionString =
                $"Host={host};Port={port};Database={db};Username={user};Password={password};" +
                "SSL Mode=Prefer;Trust Server Certificate=true;" +
                "Timeout=10;Command Timeout=30;Maximum Pool Size=10;";
        }

        public NpgsqlConnection CreateConnection()
        {
            var conn = new NpgsqlConnection(_connectionString);
            return conn;
        }

        public bool TestConnection()
        {
            try
            {
                using var conn = CreateConnection();
                conn.Open();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.DbError($"Ошибка подключения к БД: {ex.Message}");
                return false;
            }
        }

        private static string GetEnv(string key, string defaultValue = "")
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrEmpty(value) && string.IsNullOrEmpty(defaultValue))
                throw new InvalidOperationException($"Не задана переменная окружения: {key}");
            return value ?? defaultValue;
        }

        /// <summary>
        /// Создаёт схему БД при первом запуске (если таблицы отсутствуют).
        /// </summary>
        public void EnsureSchema()
        {
            try
            {
                using var conn = CreateConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS users (
                        id              SERIAL PRIMARY KEY,
                        username        VARCHAR(32) NOT NULL UNIQUE,
                        password_hash   TEXT NOT NULL,
                        role            VARCHAR(16) NOT NULL DEFAULT 'user',
                        is_blocked      BOOLEAN NOT NULL DEFAULT FALSE,
                        blocked_until   TIMESTAMPTZ,
                        failed_attempts INT NOT NULL DEFAULT 0,
                        lockout_end     TIMESTAMPTZ
                    );

                    CREATE TABLE IF NOT EXISTS notes (
                        id         SERIAL PRIMARY KEY,
                        user_id    INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                        content    VARCHAR(500) NOT NULL CHECK (char_length(content) >= 1),
                        created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                    );

                    CREATE TABLE IF NOT EXISTS watchdog_config (
                        id            SERIAL PRIMARY KEY,
                        cpu_threshold INT NOT NULL DEFAULT 85,
                        ram_threshold INT NOT NULL DEFAULT 90,
                        hdd_threshold INT NOT NULL DEFAULT 80,
                        interval_sec  INT NOT NULL DEFAULT 60
                    );

                    CREATE TABLE IF NOT EXISTS event_log (
                        id         SERIAL PRIMARY KEY,
                        event_type VARCHAR(32) NOT NULL,
                        level      VARCHAR(8)  NOT NULL,
                        message    TEXT NOT NULL,
                        created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                    );

                    -- Создаём дефолтную конфигурацию watchdog, если её нет
                    INSERT INTO watchdog_config (cpu_threshold, ram_threshold, hdd_threshold, interval_sec)
                    SELECT 85, 90, 80, 60
                    WHERE NOT EXISTS (SELECT 1 FROM watchdog_config);
                ";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                AppLogger.DbError($"Ошибка инициализации схемы: {ex.Message}");
                throw;
            }
        }
    }
}
