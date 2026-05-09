using System.Text.RegularExpressions;

namespace NoteApp.Core
{
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

    public static class CredentialValidator
    {
        private static readonly Regex UsernameRegex = new(@"^[a-zA-Z0-9_]{3,32}$", RegexOptions.Compiled);
        private static readonly Regex PasswordRegex  = new(@"^(?=.*[A-Z])(?=.*\d).{8,}$", RegexOptions.Compiled);

        public static bool IsValidUsername(string u) =>
            !string.IsNullOrEmpty(u) && UsernameRegex.IsMatch(u);

        public static bool IsValidPassword(string p) =>
            !string.IsNullOrEmpty(p) && PasswordRegex.IsMatch(p);
    }

    public static class InputValidator
    {
        public static bool TryParseThreshold(string? value, out int result)
        {
            result = 0;
            if (!int.TryParse(value, out result)) return false;
            return result >= 0 && result <= 100;
        }

        public static bool TryParseLimit(string? value, out int result)
        {
            result = 100;
            if (!int.TryParse(value, out result)) return false;
            return result >= 1 && result <= 1000;
        }

        public static bool TryParseDate(string? value, out DateTime result)
        {
            result = DateTime.MinValue;
            return DateTime.TryParseExact(value, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out result);
        }
    }
}
