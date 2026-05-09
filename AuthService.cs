using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NoteApp.Core;
using NoteApp.Infrastructure;

namespace NoteApp.Services
{
    public class AuthService
    {
        private const int MaxFailed = 3;
        private static readonly TimeSpan LockoutTime = TimeSpan.FromMinutes(5);
        private static readonly string SessionPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NoteApp", "session.dat");

        private readonly UserRepository _users;

        public AuthService(UserRepository users) => _users = users;

        public AuthResult Login(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username))
                return Fail("Логин не может быть пустым.");
            if (string.IsNullOrWhiteSpace(password))
                return Fail("Пароль не может быть пустым.");

            var user = _users.GetByUsername(username);
            if (user == null) return Fail("Неверный логин или пароль.");

            // Проверка lockout (брутфорс)
            if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
                return Fail($"Учётная запись заблокирована до {user.LockoutEnd.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}.");

            // Административная блокировка
            if (user.IsBlocked)
            {
                if (!user.BlockedUntil.HasValue || user.BlockedUntil.Value > DateTime.UtcNow)
                    return Fail(user.BlockedUntil.HasValue
                        ? $"Учётная запись заблокирована до {user.BlockedUntil.Value:yyyy-MM-dd}."
                        : "Учётная запись заблокирована бессрочно.");
            }

            if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                int newCount = user.FailedAttempts + 1;
                if (newCount >= MaxFailed)
                {
                    var lockUntil = DateTime.UtcNow.Add(LockoutTime);
                    _users.UpdateFailedAttempts(username, 0, lockUntil);
                    AppLogger.Auth($"Блокировка учётной записи: {username} (3 неудачные попытки)", true);
                    return Fail($"Учётная запись заблокирована на 5 минут после {MaxFailed} неудачных попыток.");
                }
                _users.UpdateFailedAttempts(username, newCount, null);
                AppLogger.Auth($"Неудачная попытка входа: {username}", true);
                return Fail($"Неверный логин или пароль. Осталось попыток: {MaxFailed - newCount}.");
            }

            _users.UpdateFailedAttempts(username, 0, null);
            var token = CreateSessionToken(user);
            SaveSession(token);
            AppLogger.Auth($"Успешный вход: {username} [{user.Role}]");

            return new AuthResult
            {
                Success = true,
                Message = $"Авторизация успешна. Добро пожаловать, {user.Username} [{RoleName(user.Role)}].",
                User = user, Token = token
            };
        }

        public void Logout(string username)
        {
            if (File.Exists(SessionPath)) File.Delete(SessionPath);
            Console.WriteLine($"Сессия завершена. До свидания, {username}.");
        }

        public User? GetCurrentUser()
        {
            try
            {
                if (!File.Exists(SessionPath)) return null;
                var data = File.ReadAllText(SessionPath).Trim().Split('|');
                if (data.Length < 3) return null;

                var secret = Environment.GetEnvironmentVariable("NOTEAPP_SESSION_SECRET") ?? "default-secret";
                var payload = $"{data[0]}|{data[1]}";
                var expected = ComputeHmac(payload, secret);
                if (expected != data[2]) { File.Delete(SessionPath); return null; }

                return _users.GetByUsername(data[0]);
            }
            catch { return null; }
        }

        private static string CreateSessionToken(User user)
        {
            var secret = Environment.GetEnvironmentVariable("NOTEAPP_SESSION_SECRET") ?? "default-secret";
            var payload = $"{user.Username}|{user.Role}";
            var hmac = ComputeHmac(payload, secret);
            return $"{payload}|{hmac}";
        }

        private static void SaveSession(string token)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SessionPath)!);
            File.WriteAllText(SessionPath, token);
        }

        private static string ComputeHmac(string payload, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToBase64String(hash);
        }

        private static AuthResult Fail(string msg) => new() { Success = false, Message = msg };

        public static string RoleName(UserRole role) => role switch
        {
            UserRole.Admin    => "Администратор",
            UserRole.Operator => "Оператор",
            _                 => "Пользователь"
        };
    }
}
