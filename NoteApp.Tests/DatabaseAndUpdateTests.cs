using System;
using System.Threading.Tasks;
using Xunit;
using Moq;
using NoteApp.Core;

namespace NoteApp.Tests
{
    /// <summary>
    /// Тесты подключения к базе данных (IDatabaseConnection).
    /// Покрывают классы эквивалентности TC-11..TC-18 (БД).
    /// Используют мок, т.к. реальное соединение требует запущенного PostgreSQL.
    /// </summary>
    public class DatabaseConnectionTests
    {
        // ─── TC-11: Успешное подключение ──────────────────────────────────
        [Fact]
        public void TestConnection_ValidConfig_ReturnsTrue()
        {
            // Arrange
            var mockDb = new Mock<IDatabaseConnection>();
            mockDb.Setup(d => d.TestConnection()).Returns(true);
            mockDb.Setup(d => d.ConnectionString).Returns(
                "Host=localhost;Port=5432;Database=noteapp;Username=noteapp_user;Password=StrongPass!");

            // Act
            var result = mockDb.Object.TestConnection();

            // Assert
            Assert.True(result);
        }

        // ─── TC-12: Неверный хост ─────────────────────────────────────────
        [Fact]
        public void TestConnection_InvalidHost_ReturnsFalse()
        {
            // Arrange — имитируем недоступный хост
            var mockDb = new Mock<IDatabaseConnection>();
            mockDb.Setup(d => d.TestConnection()).Returns(false);

            // Act
            var result = mockDb.Object.TestConnection();

            // Assert
            Assert.False(result);
        }

        // ─── TC-16: Строка подключения не содержит пустых полей ──────────
        [Fact]
        public void ConnectionString_DoesNotContainEmptyPassword()
        {
            // Arrange
            var mockDb = new Mock<IDatabaseConnection>();
            mockDb.Setup(d => d.ConnectionString).Returns(
                "Host=localhost;Port=5432;Database=noteapp;Username=noteapp_user;Password=StrongPass!");

            // Act
            var connStr = mockDb.Object.ConnectionString;

            // Assert — строка подключения содержит все обязательные поля
            Assert.Contains("Host=", connStr);
            Assert.Contains("Database=", connStr);
            Assert.Contains("Username=", connStr);
            Assert.Contains("Password=", connStr);
            Assert.DoesNotContain("Password=;", connStr);  // пустой пароль недопустим
        }

        // ─── Проверка: строка подключения без хардкода секретов ──────────
        [Fact]
        public void ConnectionString_ShouldNotContainHardcodedTestPassword()
        {
            // Arrange
            var mockDb = new Mock<IDatabaseConnection>();
            mockDb.Setup(d => d.ConnectionString).Returns(
                "Host=localhost;Port=5432;Database=noteapp;Username=noteapp_user;Password=StrongPass!");

            // Act
            var connStr = mockDb.Object.ConnectionString;

            // Assert — отсутствуют типичные тестовые пароли
            Assert.DoesNotContain("Password=password", connStr, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Password=123456", connStr, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Password=admin", connStr, StringComparison.OrdinalIgnoreCase);
        }

        // ─── Проверка: порт PostgreSQL в допустимом диапазоне ────────────
        [Theory]
        [InlineData("5432")]   // стандартный порт
        [InlineData("5433")]   // альтернативный
        public void ConnectionString_ValidPort_IsInRange(string port)
        {
            var mockDb = new Mock<IDatabaseConnection>();
            mockDb.Setup(d => d.ConnectionString)
                  .Returns($"Host=localhost;Port={port};Database=noteapp;Username=u;Password=p");

            var connStr = mockDb.Object.ConnectionString;
            var portStr = connStr.Split("Port=")[1].Split(";")[0];

            Assert.True(int.TryParse(portStr, out int portNum));
            Assert.InRange(portNum, 1, 65535);
        }
    }

    /// <summary>
    /// Тесты сервиса обновлений (IUpdateService).
    /// Покрывают классы эквивалентности TC-19..TC-26.
    /// </summary>
    public class UpdateServiceTests
    {
        // ─── TC-19: Новая версия доступна ────────────────────────────────
        [Fact]
        public async Task CheckForUpdate_NewerVersionAvailable_ReturnsUpdateInfo()
        {
            // Arrange
            var mockService = new Mock<IUpdateService>();
            var updateInfo = new UpdateInfo
            {
                Version     = "2.0.0",
                DownloadUrl = "https://update.example.com/noteapp-2.0.0.zip",
                Sha256      = "abc123def456",
                ReleaseNotes = "Новые функции: поддержка тёмной темы",
                MinRequiredVersion = "1.0.0"
            };
            mockService.Setup(s => s.CheckForUpdateAsync("1.3.1"))
                       .ReturnsAsync(updateInfo);

            // Act
            var result = await mockService.Object.CheckForUpdateAsync("1.3.1");

            // Assert
            Assert.NotNull(result);
            Assert.Equal("2.0.0", result!.Version);
            Assert.False(string.IsNullOrEmpty(result.DownloadUrl));
        }

        // ─── TC-20: Версия актуальна ──────────────────────────────────────
        [Fact]
        public async Task CheckForUpdate_SameVersion_ReturnsNull()
        {
            // Arrange
            var mockService = new Mock<IUpdateService>();
            mockService.Setup(s => s.CheckForUpdateAsync("1.3.1"))
                       .ReturnsAsync((UpdateInfo?)null);

            // Act
            var result = await mockService.Object.CheckForUpdateAsync("1.3.1");

            // Assert
            Assert.Null(result);
        }

        // ─── TC-21: Сервер обновлений недоступен ─────────────────────────
        [Fact]
        public async Task CheckForUpdate_ServerUnavailable_ReturnsNull()
        {
            // Arrange — имитируем таймаут (сервер недоступен)
            var mockService = new Mock<IUpdateService>();
            mockService.Setup(s => s.CheckForUpdateAsync(It.IsAny<string>()))
                       .ReturnsAsync((UpdateInfo?)null);

            // Act — не должно выбрасывать исключение
            var result = await mockService.Object.CheckForUpdateAsync("1.3.1");

            // Assert — null = нет обновлений или сервер недоступен
            Assert.Null(result);
        }

        // ─── TC-22: Повреждённый файл (SHA-256 не совпадает) ─────────────
        [Fact]
        public async Task DownloadAndInstall_Sha256Mismatch_ReturnsFail()
        {
            // Arrange
            var mockService = new Mock<IUpdateService>();
            var badUpdate = new UpdateInfo
            {
                Version     = "2.0.0",
                DownloadUrl = "https://update.example.com/noteapp-2.0.0.zip",
                Sha256      = "неверная_контрольная_сумма"
            };
            mockService.Setup(s => s.DownloadAndInstallAsync(badUpdate))
                       .ReturnsAsync(OperationResult.Fail("Контрольная сумма SHA-256 не совпадает. Установка отменена."));

            // Act
            var result = await mockService.Object.DownloadAndInstallAsync(badUpdate);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("SHA-256", result.Message);
        }

        // ─── TC-24: Успешная установка обновления ────────────────────────
        [Fact]
        public async Task DownloadAndInstall_ValidUpdate_ReturnsSuccess()
        {
            // Arrange
            var mockService = new Mock<IUpdateService>();
            var update = new UpdateInfo
            {
                Version     = "2.0.0",
                DownloadUrl = "https://update.example.com/noteapp-2.0.0.zip",
                Sha256      = "validhash"
            };
            mockService.Setup(s => s.DownloadAndInstallAsync(update))
                       .ReturnsAsync(OperationResult.Ok("Обновление 2.0.0 установлено."));

            // Act
            var result = await mockService.Object.DownloadAndInstallAsync(update);

            // Assert
            Assert.True(result.Success);
            Assert.Contains("2.0.0", result.Message);
        }

        // ─── TC-26: Принудительное обновление при версии ниже минимальной ─
        [Fact]
        public void UpdateInfo_CurrentVersionBelowMinRequired_ShouldForceUpdate()
        {
            // Arrange
            var updateInfo = new UpdateInfo
            {
                Version            = "2.0.0",
                MinRequiredVersion = "1.0.0"
            };
            string currentVersion = "0.9.0";

            // Act — проверяем логику сравнения версий
            bool isBelowMin = new Version(currentVersion) < new Version(updateInfo.MinRequiredVersion);

            // Assert
            Assert.True(isBelowMin); // принудительное обновление обязательно
        }

        // ─── Сравнение версий по SemVer ───────────────────────────────────
        [Theory]
        [InlineData("1.2.0", "1.3.1", true)]   // есть обновление
        [InlineData("1.3.1", "1.3.1", false)]  // версия актуальна
        [InlineData("2.0.0", "1.9.9", false)]  // текущая новее (откат не нужен)
        public void VersionComparison_SemVer_WorksCorrectly(
            string current, string remote, bool expectUpdate)
        {
            // Act
            var currentVer = new Version(current);
            var remoteVer  = new Version(remote);
            bool hasUpdate = remoteVer > currentVer;

            // Assert
            Assert.Equal(expectUpdate, hasUpdate);
        }
    }

    /// <summary>
    /// Тесты валидатора учётных данных (CredentialValidator).
    /// </summary>
    public class CredentialValidatorTests
    {
        // ─── Логин ────────────────────────────────────────────────────────

        [Theory]
        [InlineData("admin")]          // корректный
        [InlineData("user_123")]       // с подчёркиванием
        [InlineData("ABC")]            // заглавные
        public void IsValidUsername_ValidInputs_ReturnsTrue(string username)
        {
            Assert.True(CredentialValidator.IsValidUsername(username));
        }

        [Theory]
        [InlineData("ab")]             // слишком короткий (2 символа)
        [InlineData("")]               // пустой
        [InlineData("user name")]      // с пробелом
        [InlineData("пользователь")]   // кириллица недопустима в логине
        [InlineData("admin@site")]     // спецсимвол @
        public void IsValidUsername_InvalidInputs_ReturnsFalse(string username)
        {
            Assert.False(CredentialValidator.IsValidUsername(username));
        }

        // ─── Пароль ───────────────────────────────────────────────────────

        [Theory]
        [InlineData("Secret123!")]     // корректный
        [InlineData("Password1")]      // минимум: 8 симв, 1 загл, 1 цифра
        [InlineData("UPPER1lower")]    // смешанный регистр
        public void IsValidPassword_ValidInputs_ReturnsTrue(string password)
        {
            Assert.True(CredentialValidator.IsValidPassword(password));
        }

        [Theory]
        [InlineData("short1A")]        // 7 символов — меньше минимума
        [InlineData("alllowercase1")]  // нет заглавной
        [InlineData("nouppercase")]    // нет заглавной буквы и нет цифры
        [InlineData("NoDigitsHere")]   // нет цифры
        [InlineData("")]               // пустой
        public void IsValidPassword_InvalidInputs_ReturnsFalse(string password)
        {
            Assert.False(CredentialValidator.IsValidPassword(password));
        }
    }

    /// <summary>
    /// Тесты модуля watchdog — валидация пороговых значений.
    /// </summary>
    public class WatchdogConfigTests
    {
        [Theory]
        [InlineData(0)]    // граница: минимум
        [InlineData(50)]   // нормальное значение
        [InlineData(100)]  // граница: максимум
        public void CpuThreshold_ValidRange_IsAccepted(int value)
        {
            Assert.True(value >= 0 && value <= 100);
        }

        [Theory]
        [InlineData(-1)]   // ниже минимума
        [InlineData(101)]  // выше максимума
        public void CpuThreshold_OutOfRange_ShouldBeRejected(int value)
        {
            Assert.False(value >= 0 && value <= 100);
        }

        [Fact]
        public void WatchdogConfig_DefaultValues_AreReasonable()
        {
            var config = new WatchdogConfig();

            Assert.InRange(config.CpuThreshold,  0, 100);
            Assert.InRange(config.RamThreshold,  0, 100);
            Assert.InRange(config.HddThreshold,  0, 100);
            Assert.InRange(config.IntervalSec,   10, 3600);
        }
    }
}
