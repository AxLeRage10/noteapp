using System;
using BCrypt.Net;

namespace NoteApp.Core
{
    public class AuthService
    {
        private const int MaxFailedAttempts = 3;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

        private readonly IUserRepository _userRepo;

        public AuthService(IUserRepository userRepo)
        {
            _userRepo = userRepo;
        }

        public AuthResult Login(string username, string password)
        {
            // Базовая валидация входных данных
            if (string.IsNullOrWhiteSpace(username))
                return new AuthResult { Success = false, Message = "Логин не может быть пустым." };

            if (string.IsNullOrWhiteSpace(password))
                return new AuthResult { Success = false, Message = "Пароль не может быть пустым." };

            var user = _userRepo.GetByUsername(username);

            // Пользователь не найден — возвращаем то же сообщение, что и при неверном пароле
            if (user == null)
                return new AuthResult { Success = false, Message = "Неверный логин или пароль." };

            // Проверка блокировки по lockout (брутфорс)
            if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
            {
                return new AuthResult
                {
                    Success = false,
                    Message = $"Учётная запись заблокирована до {user.LockoutEnd.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}."
                };
            }

            // Проверка административной блокировки
            if (user.IsBlocked)
            {
                if (!user.BlockedUntil.HasValue || user.BlockedUntil.Value > DateTime.UtcNow)
                {
                    return new AuthResult
                    {
                        Success = false,
                        Message = user.BlockedUntil.HasValue
                            ? $"Учётная запись заблокирована до {user.BlockedUntil.Value:yyyy-MM-dd}."
                            : "Учётная запись заблокирована бессрочно."
                    };
                }
                // Дата разблокировки наступила — снимаем блокировку
                user.IsBlocked = false;
                user.BlockedUntil = null;
            }

            // Проверка пароля
            bool passwordCorrect = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);

            if (!passwordCorrect)
            {
                int newFailedCount = user.FailedAttempts + 1;
                DateTime? newLockout = null;

                if (newFailedCount >= MaxFailedAttempts)
                {
                    newLockout = DateTime.UtcNow.Add(LockoutDuration);
                    _userRepo.UpdateFailedAttempts(username, 0, newLockout);
                    return new AuthResult
                    {
                        Success = false,
                        Message = $"Учётная запись заблокирована на 5 минут после {MaxFailedAttempts} неудачных попыток."
                    };
                }

                _userRepo.UpdateFailedAttempts(username, newFailedCount, null);
                int remaining = MaxFailedAttempts - newFailedCount;
                return new AuthResult
                {
                    Success = false,
                    Message = $"Неверный логин или пароль. Осталось попыток: {remaining}."
                };
            }

            // Успешная авторизация — сбрасываем счётчик
            _userRepo.UpdateFailedAttempts(username, 0, null);

            return new AuthResult
            {
                Success = true,
                Message = $"Авторизация успешна. Добро пожаловать, {user.Username} [{RoleName(user.Role)}].",
                User = user,
                Token = GenerateToken(user)
            };
        }

        private static string GenerateToken(User user)
        {
            // HMAC-токен (упрощённая реализация для тестов)
            var payload = $"{user.Username}:{user.Role}:{DateTime.UtcNow.Ticks}";
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload));
        }

        private static string RoleName(UserRole role) => role switch
        {
            UserRole.Admin    => "Администратор",
            UserRole.Operator => "Оператор",
            UserRole.User     => "Пользователь",
            _ => "Неизвестно"
        };
    }

    // ─── Сервис заметок ───────────────────────────────────────────────────
    public class NoteService
    {
        private readonly INoteRepository _noteRepo;

        public NoteService(INoteRepository noteRepo)
        {
            _noteRepo = noteRepo;
        }

        public (bool Success, string Message, int NoteId) AddNote(User currentUser, string content)
        {
            var (isValid, error) = NoteValidator.Validate(content);
            if (!isValid)
                return (false, error, 0);

            var note = new Note
            {
                UserId    = currentUser.Id,
                Content   = content,
                CreatedAt = DateTime.UtcNow
            };

            int id = _noteRepo.SaveNote(note);
            return (true, $"Заметка сохранена. ID: {id}.", id);
        }

        public System.Collections.Generic.List<Note> GetNotes(
            User currentUser,
            DateTime? from = null,
            DateTime? to = null,
            int limit = 100)
        {
            int targetUserId = currentUser.Id;
            return _noteRepo.GetByUserId(targetUserId, from, to, limit);
        }

        public (bool Success, string Message, Note? Note) GetNoteById(User currentUser, int noteId)
        {
            var note = _noteRepo.GetById(noteId);
            if (note == null)
                return (false, $"Заметка с ID {noteId} не найдена.", null);

            // Пользователь видит только свои заметки; admin — любые
            if (currentUser.Role != UserRole.Admin && note.UserId != currentUser.Id)
                return (false, "Нет доступа к заметке другого пользователя.", null);

            return (true, string.Empty, note);
        }
    }
}
