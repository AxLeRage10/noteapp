using System;
using System.Collections.Generic;
using NoteApp.Core;
using NoteApp.Infrastructure;

namespace NoteApp.Services
{
    public class NoteService
    {
        private readonly NoteRepository _notes;

        public NoteService(NoteRepository notes) => _notes = notes;

        public OperationResult AddNote(User user, string content)
        {
            var (isValid, error) = NoteValidator.Validate(content);
            if (!isValid) return OperationResult.Fail(error);

            var note = new Note { UserId = user.Id, Content = content };
            int id = _notes.SaveNote(note);
            return OperationResult.Ok($"Заметка сохранена. ID: {id}. Дата: {DateTime.Now:yyyy-MM-dd HH:mm:ss}.");
        }

        public List<Note> GetNotes(User user, DateTime? from, DateTime? to, int limit)
        {
            return user.Role == UserRole.Admin
                ? _notes.GetAll(from, to, limit)
                : _notes.GetByUserId(user.Id, from, to, limit);
        }

        public OperationResult GetNoteById(User user, int id, out Note? note)
        {
            note = _notes.GetById(id);
            if (note == null) return OperationResult.Fail($"Заметка с ID {id} не найдена.");
            if (user.Role != UserRole.Admin && note.UserId != user.Id)
                return OperationResult.Fail("Нет доступа к заметке другого пользователя.");
            return OperationResult.Ok();
        }

        public static void PrintNotes(List<Note> notes)
        {
            if (notes.Count == 0) { Console.WriteLine("Заметок не найдено."); return; }
            Console.WriteLine($"{"ID",-6} {"Дата",-22} {"Автор",-16} Текст");
            Console.WriteLine(new string('-', 80));
            foreach (var n in notes)
            {
                var text = n.Content.Length > 40 ? n.Content[..40] + "..." : n.Content;
                Console.WriteLine($"{n.Id,-6} {n.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {n.Username,-16} {text}");
            }
        }

        public static void PrintNote(Note note)
        {
            Console.WriteLine($"ID:      {note.Id}");
            Console.WriteLine($"Автор:   {note.Username}");
            Console.WriteLine($"Дата:    {note.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Текст:   {note.Content}");
        }
    }
}
