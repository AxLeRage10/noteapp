using System;
using System.Collections.Generic;
using Xunit;
using Moq;
using NoteApp.Core;

namespace NoteApp.Tests
{
    /// <summary>
    /// Тесты валидатора текста заметок (NoteValidator).
    /// </summary>
    public class NoteValidatorTests
    {
        // ─── Допустимые классы ────────────────────────────────────────

        [Fact]
        public void Validate_NormalRussianText_ReturnsValid()
        {
            var (isValid, error) = NoteValidator.Validate("Перезапустить шлюз на узле");
            Assert.True(isValid);
            Assert.Empty(error);
        }

        [Fact]
        public void Validate_NormalEnglishText_ReturnsValid()
        {
            var (isValid, _) = NoteValidator.Validate("Restart VPN gateway node");
            Assert.True(isValid);
        }

        [Fact]
        public void Validate_MixedText_ReturnsValid()
        {
            var (isValid, _) = NoteValidator.Validate("Перезапуск VPN service gateway-01");
            Assert.True(isValid);
        }

        // Граничное значение: 1 символ
        [Fact]
        public void Validate_SingleCharacter_ReturnsValid()
        {
            var (isValid, _) = NoteValidator.Validate("А");
            Assert.True(isValid);
        }

        // Граничное значение: ровно 500 символов
        [Fact]
        public void Validate_ExactlyFiveHundredChars_ReturnsValid()
        {
            var text = new string('А', 500);
            var (isValid, _) = NoteValidator.Validate(text);
            Assert.True(isValid);
        }

        [Fact]
        public void Validate_TextWithDigitsAndPunctuation_ReturnsValid()
        {
            var (isValid, _) = NoteValidator.Validate("Сервер 01 перезапущен! Время: 14:30.");
            Assert.True(isValid);
        }

        [Fact]
        public void Validate_TextWithYoLetter_ReturnsValid()
        {
            var (isValid, _) = NoteValidator.Validate("Подключение к ёмкому серверу восстановлено");
            Assert.True(isValid);
        }

        // ─── Недопустимые классы ──────────────────────────────────────

        [Fact]
        public void Validate_EmptyString_ReturnsInvalid()
        {
            var (isValid, error) = NoteValidator.Validate("");
            Assert.False(isValid);
            Assert.Contains("пустым", error);
        }

        [Fact]
        public void Validate_WhitespaceOnly_ReturnsInvalid()
        {
            var (isValid, error) = NoteValidator.Validate("     ");
            Assert.False(isValid);
            Assert.Contains("пустым", error);
        }

        [Fact]
        public void Validate_NullInput_ReturnsInvalid()
        {
            var (isValid, _) = NoteValidator.Validate(null);
            Assert.False(isValid);
        }

        // Граничное значение: 501 символ — недопустимо
        [Fact]
        public void Validate_FiveHundredAndOneChars_ReturnsInvalid()
        {
            var text = new string('А', 501);
            var (isValid, error) = NoteValidator.Validate(text);
            Assert.False(isValid);
            Assert.Contains("501", error);
        }

        [Fact]
        public void Validate_VeryLongString_ReturnsInvalidWithLength()
        {
            var text = new string('Б', 1000);
            var (isValid, error) = NoteValidator.Validate(text);
            Assert.False(isValid);
            Assert.Contains("1000", error);
        }
    }

    /// <summary>
    /// Тесты сервиса заметок (NoteService).
    /// </summary>
    public class NoteServiceTests
    {
        private static User MakeUser(int id = 1, UserRole role = UserRole.User)
            => new User { Id = id, Username = $"user{id}", Role = role };

        private static (Mock<INoteRepository> Mock, NoteService Service) BuildService()
        {
            var mock = new Mock<INoteRepository>();
            var service = new NoteService(mock.Object);
            return (mock, service);
        }

        [Fact]
        public void AddNote_ValidText_SavesAndReturnsId()
        {
            var (mockRepo, service) = BuildService();
            mockRepo.Setup(r => r.SaveNote(It.IsAny<Note>())).Returns(42);
            var user = MakeUser();

            var (success, message, id) = service.AddNote(user, "Перезапустить шлюз");

            Assert.True(success);
            Assert.Equal(42, id);
            Assert.Contains("42", message);
            mockRepo.Verify(r => r.SaveNote(It.IsAny<Note>()), Times.Once);
        }

        [Fact]
        public void AddNote_EnglishText_SavesSuccessfully()
        {
            var (mockRepo, service) = BuildService();
            mockRepo.Setup(r => r.SaveNote(It.IsAny<Note>())).Returns(1);
            var user = MakeUser();

            var (success, _, _) = service.AddNote(user, "Restart VPN gateway");

            Assert.True(success);
            mockRepo.Verify(r => r.SaveNote(It.IsAny<Note>()), Times.Once);
        }

        [Fact]
        public void AddNote_EmptyText_ReturnsError()
        {
            var (_, service) = BuildService();
            var user = MakeUser();

            var (success, message, _) = service.AddNote(user, "");

            Assert.False(success);
            Assert.Contains("пустым", message);
        }

        [Fact]
        public void AddNote_TooLongText_ReturnsLengthError()
        {
            var (_, service) = BuildService();
            var user = MakeUser();
            var longText = new string('А', 501);

            var (success, message, _) = service.AddNote(user, longText);

            Assert.False(success);
            Assert.Contains("501", message);
        }

        [Fact]
        public void AddNote_SetsCorrectUserId()
        {
            var (mockRepo, service) = BuildService();
            Note? capturedNote = null;
            mockRepo.Setup(r => r.SaveNote(It.IsAny<Note>()))
                    .Callback<Note>(n => capturedNote = n)
                    .Returns(1);
            var user = MakeUser(id: 7);

            service.AddNote(user, "Тестовая заметка");

            Assert.NotNull(capturedNote);
            Assert.Equal(7, capturedNote!.UserId);
        }

        [Fact]
        public void GetNoteById_OwnNote_ReturnsNote()
        {
            var (mockRepo, service) = BuildService();
            var user = MakeUser(id: 1);
            var note = new Note { Id = 5, UserId = 1, Content = "Моя заметка" };
            mockRepo.Setup(r => r.GetById(5)).Returns(note);

            var (success, _, returnedNote) = service.GetNoteById(user, 5);

            Assert.True(success);
            Assert.Equal("Моя заметка", returnedNote!.Content);
        }

        [Fact]
        public void GetNoteById_OtherUserNote_RegularUser_ReturnsForbidden()
        {
            var (mockRepo, service) = BuildService();
            var user = MakeUser(id: 1, role: UserRole.User);
            var note = new Note { Id = 10, UserId = 2, Content = "Чужая заметка" };
            mockRepo.Setup(r => r.GetById(10)).Returns(note);

            var (success, message, _) = service.GetNoteById(user, 10);

            Assert.False(success);
            Assert.Contains("Нет доступа", message);
        }

        [Fact]
        public void GetNoteById_OtherUserNote_AdminUser_ReturnsNote()
        {
            var (mockRepo, service) = BuildService();
            var admin = MakeUser(id: 1, role: UserRole.Admin);
            var note = new Note { Id = 10, UserId = 2, Content = "Чужая заметка" };
            mockRepo.Setup(r => r.GetById(10)).Returns(note);

            var (success, _, returnedNote) = service.GetNoteById(admin, 10);

            Assert.True(success);
            Assert.NotNull(returnedNote);
        }

        [Fact]
        public void GetNoteById_NonExistentId_ReturnsNotFound()
        {
            var (mockRepo, service) = BuildService();
            mockRepo.Setup(r => r.GetById(999)).Returns((Note?)null);
            var user = MakeUser();

            var (success, message, _) = service.GetNoteById(user, 999);

            Assert.False(success);
            Assert.Contains("не найдена", message);
        }
    }
}
