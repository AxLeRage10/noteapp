using System;
using Xunit;
using Moq;
using BCrypt.Net;
using NoteApp.Core;

namespace NoteApp.Tests
{
    /// <summary>
    /// Тесты модуля авторизации (AuthService).
    /// Покрывают классы эквивалентности TC-01..TC-10 из чек-листа.
    /// </summary>
    public class AuthServiceTests
    {
        // ─── Вспомогательные методы ──────────────────────────────────────

        /// <summary>Создаёт тестового пользователя с BCrypt-хэшем пароля.</summary>
        private static User CreateUser(
            string username   = "admin",
            string password   = "Secret123!",
            UserRole role     = UserRole.Admin,
            bool isBlocked    = false,
            DateTime? blockedUntil  = null,
            int failedAttempts      = 0,
            DateTime? lockoutEnd    = null)
        {
            return new User
            {
                Id             = 1,
                Username       = username,
                PasswordHash   = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 4), // workFactor=4 ускоряет тесты
                Role           = role,
                IsBlocked      = isBlocked,
                BlockedUntil   = blockedUntil,
                FailedAttempts = failedAttempts,
                LockoutEnd     = lockoutEnd
            };
        }

        /// <summary>Создаёт mock репозитория с одним пользователем.</summary>
        private static (Mock<IUserRepository> Mock, AuthService Service) BuildService(User? user)
        {
            var mockRepo = new Mock<IUserRepository>();
            mockRepo.Setup(r => r.GetByUsername(It.IsAny<string>())).Returns(user);
            mockRepo.Setup(r => r.UpdateFailedAttempts(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime?>()));
            var service = new AuthService(mockRepo.Object);
            return (mockRepo, service);
        }

        // ─── TC-01: Успешный вход администратора ─────────────────────────
        [Fact]
        public void Login_ValidAdminCredentials_ReturnsSuccess()
        {
            // Arrange
            var user = CreateUser("admin", "Secret123!", UserRole.Admin);
            var (_, service) = BuildService(user);

            // Act
            var result = service.Login("admin", "Secret123!");

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.User);
            Assert.Equal(UserRole.Admin, result.User!.Role);
            Assert.Contains("Авторизация успешна", result.Message);
        }

        // ─── TC-02: Успешный вход оператора ──────────────────────────────
        [Fact]
        public void Login_ValidOperatorCredentials_ReturnsOperatorRole()
        {
            // Arrange
            var user = CreateUser("operator1", "Pass456!", UserRole.Operator);
            var (_, service) = BuildService(user);

            // Act
            var result = service.Login("operator1", "Pass456!");

            // Assert
            Assert.True(result.Success);
            Assert.Equal(UserRole.Operator, result.User!.Role);
        }

        // ─── TC-03: Успешный вход пользователя ───────────────────────────
        [Fact]
        public void Login_ValidUserCredentials_ReturnsUserRole()
        {
            // Arrange
            var user = CreateUser("ivanov", "Pass789!", UserRole.User);
            var (_, service) = BuildService(user);

            // Act
            var result = service.Login("ivanov", "Pass789!");

            // Assert
            Assert.True(result.Success);
            Assert.Equal(UserRole.User, result.User!.Role);
        }

        // ─── TC-04: Неверный пароль ───────────────────────────────────────
        [Fact]
        public void Login_WrongPassword_ReturnsFailureWithRemainingAttempts()
        {
            // Arrange
            var user = CreateUser("admin", "Secret123!");
            var (_, service) = BuildService(user);

            // Act
            var result = service.Login("admin", "wrongpassword");

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Неверный логин или пароль", result.Message);
            Assert.Contains("Осталось попыток", result.Message);
        }

        // ─── TC-05: Несуществующий пользователь ──────────────────────────
        [Fact]
        public void Login_UnknownUsername_ReturnsGenericError()
        {
            // Arrange — репозиторий возвращает null
            var (_, service) = BuildService(null);

            // Act
            var result = service.Login("nobody", "Test123!");

            // Assert — сообщение не должно раскрывать, что пользователь не найден
            Assert.False(result.Success);
            Assert.Equal("Неверный логин или пароль.", result.Message);
        }

        // ─── TC-06: Заблокированный пользователь (admin-блокировка) ──────
        [Fact]
        public void Login_AdminBlockedUser_ReturnsBlockedMessage()
        {
            // Arrange
            var user = CreateUser("ivanov", "Pass789!", isBlocked: true);
            var (_, service) = BuildService(user);

            // Act
            var result = service.Login("ivanov", "Pass789!");

            // Assert
            Assert.False(result.Success);
            Assert.Contains("заблокирована", result.Message);
        }

        // ─── TC-07: Блокировка с датой разблокировки в будущем ───────────
        [Fact]
        public void Login_BlockedWithFutureDate_ReturnsBlockedWithDate()
        {
            // Arrange
            var futureDate = DateTime.UtcNow.AddDays(30);
            var user = CreateUser("ivanov", "Pass789!", isBlocked: true, blockedUntil: futureDate);
            var (_, service) = BuildService(user);

            // Act
            var result = service.Login("ivanov", "Pass789!");

            // Assert
            Assert.False(result.Success);
            Assert.Contains("заблокирована до", result.Message);
        }

        // ─── TC-08: Автоматическая блокировка после 3 неудачных попыток ──
        [Fact]
        public void Login_ThreeFailedAttempts_TriggersLockout()
        {
            // Arrange — пользователь уже имеет 2 неудачные попытки
            var user = CreateUser("admin", "Secret123!", failedAttempts: 2);
            var (mockRepo, service) = BuildService(user);

            // Act — третья неудачная попытка
            var result = service.Login("admin", "wrongpass");

            // Assert
            Assert.False(result.Success);
            Assert.Contains("заблокирована на 5 минут", result.Message);

            // Проверяем, что UpdateFailedAttempts вызван с lockoutEnd != null
            mockRepo.Verify(
                r => r.UpdateFailedAttempts("admin", 0, It.Is<DateTime?>(d => d.HasValue)),
                Times.Once);
        }

        // ─── TC-09: Пользователь в lockout (брутфорс-блокировка) ─────────
        [Fact]
        public void Login_UserInLockout_ReturnsLockoutMessage()
        {
            // Arrange — lockoutEnd в будущем
            var user = CreateUser("admin", "Secret123!", lockoutEnd: DateTime.UtcNow.AddMinutes(4));
            var (_, service) = BuildService(user);

            // Act — даже правильный пароль не поможет
            var result = service.Login("admin", "Secret123!");

            // Assert
            Assert.False(result.Success);
            Assert.Contains("заблокирована до", result.Message);
        }

        // ─── TC-10: Пустой логин ──────────────────────────────────────────
        [Fact]
        public void Login_EmptyUsername_ReturnsValidationError()
        {
            // Arrange
            var (_, service) = BuildService(null);

            // Act
            var result = service.Login("", "Secret123!");

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Логин не может быть пустым", result.Message);
        }

        // ─── TC-11: Пустой пароль ─────────────────────────────────────────
        [Fact]
        public void Login_EmptyPassword_ReturnsValidationError()
        {
            // Arrange
            var user = CreateUser();
            var (_, service) = BuildService(user);

            // Act
            var result = service.Login("admin", "");

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Пароль не может быть пустым", result.Message);
        }

        // ─── TC-12: Успешный сброс счётчика после успешного входа ────────
        [Fact]
        public void Login_SuccessAfterFailures_ResetsFailedAttempts()
        {
            // Arrange — у пользователя есть 1 неудачная попытка
            var user = CreateUser("admin", "Secret123!", failedAttempts: 1);
            var (mockRepo, service) = BuildService(user);

            // Act — теперь верный пароль
            var result = service.Login("admin", "Secret123!");

            // Assert
            Assert.True(result.Success);
            mockRepo.Verify(
                r => r.UpdateFailedAttempts("admin", 0, null),
                Times.Once);
        }

        // ─── TC-13: Токен генерируется при успешном входе ────────────────
        [Fact]
        public void Login_Success_GeneratesNonEmptyToken()
        {
            // Arrange
            var user = CreateUser();
            var (_, service) = BuildService(user);

            // Act
            var result = service.Login("admin", "Secret123!");

            // Assert
            Assert.True(result.Success);
            Assert.False(string.IsNullOrEmpty(result.Token));
        }
    }
}
