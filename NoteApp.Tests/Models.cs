using System;
using System.Text.RegularExpressions;

namespace NoteApp.Core
{
    // ─── Роли ────────────────────────────────────────────────────────────
    public enum UserRole { Admin, Operator, User }

    // ─── Модели ──────────────────────────────────────────────────────────
    public class User
    {
        public int    Id           { get; set; }
        public string Username     { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public UserRole Role       { get; set; }
        public bool   IsBlocked    { get; set; }
        public DateTime? BlockedUntil { get; set; }
        public int    FailedAttempts { get; set; }
        public DateTime? LockoutEnd  { get; set; }
    }

    public class Note
    {
        public int    Id        { get; set; }
        public int    UserId    { get; set; }
        public string Content   { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class UpdateInfo
    {
        public string Version        { get; set; } = string.Empty;
        public string DownloadUrl    { get; set; } = string.Empty;
        public string Sha256         { get; set; } = string.Empty;
        public string ReleaseNotes   { get; set; } = string.Empty;
        public string MinRequiredVersion { get; set; } = "0.0.0";
    }

    public class WatchdogConfig
    {
        public int CpuThreshold  { get; set; } = 85;
        public int RamThreshold  { get; set; } = 90;
        public int HddThreshold  { get; set; } = 80;
        public int IntervalSec   { get; set; } = 60;
    }

    // ─── Результаты операций ─────────────────────────────────────────────
    public class OperationResult
    {
        public bool   Success { get; set; }
        public string Message { get; set; } = string.Empty;

        public static OperationResult Ok(string msg = "")    => new() { Success = true,  Message = msg };
        public static OperationResult Fail(string msg = "")  => new() { Success = false, Message = msg };
    }

    public class AuthResult : OperationResult
    {
        public User?   User   { get; set; }
        public string  Token  { get; set; } = string.Empty;
    }

    // ─── Валидация заметок ───────────────────────────────────────────────
    public static class NoteValidator
    {
        private const int MaxLength = 500;

        public static (bool IsValid, string Error) Validate(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (false, "Текст заметки не может быть пустым.");

            if (text.Length > MaxLength)
                return (false, $"Текст заметки превышает {MaxLength} символов (текущая длина: {text.Length}).");

            return (true, string.Empty);
        }
    }

    // ─── Валидация учётных данных ────────────────────────────────────────
    public static class CredentialValidator
    {
        private static readonly Regex UsernameRegex = new Regex(
            @"^[a-zA-Z0-9_]{3,32}$", RegexOptions.Compiled);

        private static readonly Regex PasswordRegex = new Regex(
            @"^(?=.*[A-Z])(?=.*\d).{8,}$", RegexOptions.Compiled);

        public static bool IsValidUsername(string username)
            => !string.IsNullOrEmpty(username) && UsernameRegex.IsMatch(username);

        public static bool IsValidPassword(string password)
            => !string.IsNullOrEmpty(password) && PasswordRegex.IsMatch(password);
    }

    // ─── Интерфейс репозитория пользователей ────────────────────────────
    public interface IUserRepository
    {
        User?   GetByUsername(string username);
        void    UpdateFailedAttempts(string username, int count, DateTime? lockoutEnd);
        void    SaveUser(User user);
        void    DeleteUser(string username);
        bool    UsernameExists(string username);
        System.Collections.Generic.List<User> GetAll();
    }

    // ─── Интерфейс репозитория заметок ───────────────────────────────────
    public interface INoteRepository
    {
        int  SaveNote(Note note);
        System.Collections.Generic.List<Note> GetByUserId(int userId, DateTime? from = null, DateTime? to = null, int limit = 100);
        Note? GetById(int noteId);
    }

    // ─── Интерфейс сервиса обновлений ────────────────────────────────────
    public interface IUpdateService
    {
        System.Threading.Tasks.Task<UpdateInfo?> CheckForUpdateAsync(string currentVersion);
        System.Threading.Tasks.Task<OperationResult> DownloadAndInstallAsync(UpdateInfo info);
    }

    // ─── Интерфейс БД ────────────────────────────────────────────────────
    public interface IDatabaseConnection
    {
        bool TestConnection();
        string ConnectionString { get; }
    }
}
