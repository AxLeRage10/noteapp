using System;
using NoteApp.Core;
using NoteApp.Infrastructure;

namespace NoteApp.Services
{
    public class UserManagementService
    {
        private readonly UserRepository _users;

        public UserManagementService(UserRepository users) => _users = users;

        public OperationResult AddUser(string username, string password, string role)
        {
            if (!CredentialValidator.IsValidUsername(username))
                return OperationResult.Fail("Логин: 3-32 символа, только латиница, цифры и _.");
            if (!CredentialValidator.IsValidPassword(password))
                return OperationResult.Fail("Пароль: мин. 8 символов, 1 заглавная буква, 1 цифра.");

            var parsedRole = role.ToLower() switch {
                "admin"    => (UserRole?)UserRole.Admin,
                "operator" => UserRole.Operator,
                "user"     => UserRole.User,
                _          => null
            };
            if (parsedRole == null)
                return OperationResult.Fail("Допустимые роли: admin, operator, user.");

            if (_users.UsernameExists(username))
                return OperationResult.Fail($"Пользователь '{username}' уже существует.");

            var user = new User
            {
                Username     = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Role         = parsedRole.Value
            };
            _users.SaveUser(user);
            AppLogger.UserOp($"Создан пользователь: {username} [{parsedRole}]");
            return OperationResult.Ok($"Пользователь '{username}' успешно создан. Роль: {AuthService.RoleName(parsedRole.Value)}.");
        }

        public OperationResult RemoveUser(string targetUsername, string currentUsername)
        {
            if (targetUsername.Equals(currentUsername, StringComparison.OrdinalIgnoreCase))
                return OperationResult.Fail("Нельзя удалить собственную учётную запись.");
            if (!_users.UsernameExists(targetUsername))
                return OperationResult.Fail($"Пользователь '{targetUsername}' не найден.");

            _users.DeleteUser(targetUsername);
            AppLogger.UserOp($"Удалён пользователь: {targetUsername}");
            return OperationResult.Ok($"Пользователь '{targetUsername}' удалён.");
        }

        public OperationResult BlockUser(string targetUsername, string currentUsername, DateTime? until)
        {
            if (targetUsername.Equals(currentUsername, StringComparison.OrdinalIgnoreCase))
                return OperationResult.Fail("Нельзя заблокировать собственную учётную запись.");

            var user = _users.GetByUsername(targetUsername);
            if (user == null)
                return OperationResult.Fail($"Пользователь '{targetUsername}' не найден.");

            if (until.HasValue && until.Value <= DateTime.UtcNow)
                return OperationResult.Fail("Дата блокировки должна быть в будущем.");

            user.IsBlocked    = true;
            user.BlockedUntil = until;
            _users.SaveUser(user);

            var msg = until.HasValue
                ? $"Пользователь '{targetUsername}' заблокирован до {until.Value:yyyy-MM-dd}."
                : $"Пользователь '{targetUsername}' заблокирован бессрочно.";
            AppLogger.UserOp(msg);
            return OperationResult.Ok(msg);
        }

        public void PrintUsers()
        {
            var users = _users.GetAll();
            Console.WriteLine($"{"Логин",-20} {"Роль",-16} {"Статус",-30}");
            Console.WriteLine(new string('-', 68));
            foreach (var u in users)
            {
                var status = u.IsBlocked
                    ? (u.BlockedUntil.HasValue ? $"Заблокирован до {u.BlockedUntil:yyyy-MM-dd}" : "Заблокирован бессрочно")
                    : "Активен";
                Console.WriteLine($"{u.Username,-20} {AuthService.RoleName(u.Role),-16} {status,-30}");
            }
        }
    }
}
