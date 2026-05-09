using System;
using System.Linq;
using System.Threading.Tasks;
using dotenv.net;
using NoteApp.Core;
using NoteApp.Infrastructure;
using NoteApp.Services;

namespace NoteApp
{
    class Program
    {
        static AuthService?           _auth;
        static NoteService?           _noteSvc;
        static UserManagementService? _userMgmt;
        static WatchdogService?       _watchdog;
        static UpdateService?         _updater;

        static async Task<int> Main(string[] args)
        {
            DotEnv.Load();
            AppLogger.Configure();
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.Title = "NoteApp — Система заметок";

            try
            {
                var db       = new DatabaseFactory();
                var userRepo = new UserRepository(db);
                var noteRepo = new NoteRepository(db);

                db.EnsureSchema();

                _auth     = new AuthService(userRepo);
                _noteSvc  = new NoteService(noteRepo);
                _userMgmt = new UserManagementService(userRepo);
                _watchdog = new WatchdogService(db);
                _updater  = new UpdateService();

                // ─── CLI-режим: переданы аргументы ───────────────────
                if (args.Length > 0)
                    return await RunCli(args);

                // ─── Интерактивный режим: авторизация при старте ──────
                return await RunInteractive();
            }
            catch (Exception ex)
            {
                AppLogger.Fatal(ex);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n  [FATAL] {ex.Message}");
                Console.ResetColor();
                Console.ReadKey();
                return 2;
            }
            finally
            {
                AppLogger.Close();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  ИНТЕРАКТИВНЫЙ РЕЖИМ
        // ═══════════════════════════════════════════════════════════════
        static async Task<int> RunInteractive()
        {
            PrintBanner();

            // Авторизация при старте
            User? currentUser = null;
            while (currentUser == null)
            {
                Console.Write("  Логин:  ");
                var username = Console.ReadLine()?.Trim() ?? "";

                if (username.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                    username.Equals("выход", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("\n  До свидания!\n");
                    return 0;
                }

                Console.Write("  Пароль: ");
                var password = ReadPassword();

                var result = _auth!.Login(username, password);
                if (result.Success)
                {
                    currentUser = result.User;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\n  ✓ {result.Message}");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n  ✗ {result.Message}");
                    Console.ResetColor();
                    Console.WriteLine();
                }
            }

            // После входа — показать список доступных команд
            PrintHelp(currentUser);

            // Цикл ввода команд
            while (true)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"  [{currentUser.Username}]> ");
                Console.ResetColor();

                var input = Console.ReadLine()?.Trim() ?? "";
                if (string.IsNullOrEmpty(input)) continue;

                // Разбиваем строку на аргументы (учитываем кавычки)
                var cmdArgs = ParseArgs(input);

                if (cmdArgs.Length == 0) continue;

                // Выход из аккаунта
                if (cmdArgs[0].Equals("--logout", StringComparison.OrdinalIgnoreCase))
                {
                    _auth.Logout(currentUser.Username);
                    Console.WriteLine();
                    currentUser = null;
                    // Снова авторизация
                    return await RunInteractive();
                }

                // Выход из программы
                if (cmdArgs[0].Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[0].Equals("выход", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("\n  До свидания!\n");
                    return 0;
                }

                // Справка
                if (cmdArgs[0].Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[0].Equals("help", StringComparison.OrdinalIgnoreCase) ||
                    cmdArgs[0].Equals("?", StringComparison.OrdinalIgnoreCase))
                {
                    PrintHelp(currentUser);
                    continue;
                }

                // Выполняем команду
                await ExecuteCommand(cmdArgs, currentUser);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  CLI-РЕЖИМ (запуск с аргументами)
        // ═══════════════════════════════════════════════════════════════
        static async Task<int> RunCli(string[] args)
        {
            if (Has(args, "--help") || Has(args, "-h"))
            {
                PrintHelpCli();
                return 0;
            }

            if (Has(args, "--login"))
            {
                var username = Get(args, "--login") ?? "";
                var password = Get(args, "--password") ?? Get(args, "-p") ?? "";
                var result   = _auth!.Login(username, password);
                Console.WriteLine(result.Message);
                return result.Success ? 0 : 1;
            }

            if (Has(args, "--logout"))
            {
                var user = _auth!.GetCurrentUser();
                if (user == null) { Console.WriteLine("Нет активной сессии."); return 1; }
                _auth.Logout(user.Username);
                return 0;
            }

            var current = _auth!.GetCurrentUser();
            if (current == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  Ошибка: требуется авторизация.");
                Console.WriteLine("  Выполните: NoteApp.exe --login <логин> --password <пароль>");
                Console.ResetColor();
                return 1;
            }

            return await ExecuteCommand(args, current) ? 0 : 1;
        }

        // ═══════════════════════════════════════════════════════════════
        //  ВЫПОЛНЕНИЕ КОМАНДЫ
        // ═══════════════════════════════════════════════════════════════
        static async Task<bool> ExecuteCommand(string[] args, User current)
        {
            // --addNewNote
            if (Has(args, "--addNewNote"))
            {
                var text = Get(args, "--addNewNote") ?? "";
                var r = _noteSvc!.AddNote(current, text);
                PrintResult(r.Success, r.Message);
                return r.Success;
            }

            // --listNotes
            if (Has(args, "--listNotes"))
            {
                DateTime? from = null, to = null; int limit = 50;
                if (Get(args, "--from")  is { } f && InputValidator.TryParseDate(f, out var fd)) from = fd;
                if (Get(args, "--to")    is { } t && InputValidator.TryParseDate(t, out var td)) to   = td;
                if (Get(args, "--limit") is { } l) InputValidator.TryParseLimit(l, out limit);
                var notes = _noteSvc!.GetNotes(current, from, to, limit);
                NoteService.PrintNotes(notes);
                return true;
            }

            // --getNote
            if (Has(args, "--getNote"))
            {
                if (!int.TryParse(Get(args, "--id"), out int id))
                { PrintResult(false, "ID должен быть целым числом."); return false; }
                var r = _noteSvc!.GetNoteById(current, id, out var note);
                if (!r.Success) { PrintResult(false, r.Message); return false; }
                NoteService.PrintNote(note!);
                return true;
            }

            // --addUser
            if (Has(args, "--addUser"))
            {
                if (!CheckRole(current, UserRole.Admin)) return false;
                var r = _userMgmt!.AddUser(
                    Get(args, "--username") ?? "",
                    Get(args, "--password") ?? Get(args, "-p") ?? "",
                    Get(args, "--role")     ?? "");
                PrintResult(r.Success, r.Message);
                return r.Success;
            }

            // --removeUser
            if (Has(args, "--removeUser"))
            {
                if (!CheckRole(current, UserRole.Admin)) return false;
                var r = _userMgmt!.RemoveUser(Get(args, "--username") ?? "", current.Username);
                PrintResult(r.Success, r.Message);
                return r.Success;
            }

            // --blockUser
            if (Has(args, "--blockUser"))
            {
                if (!CheckRole(current, UserRole.Admin)) return false;
                DateTime? until = null;
                if (Get(args, "--until") is { } u && InputValidator.TryParseDate(u, out var ud)) until = ud;
                var r = _userMgmt!.BlockUser(Get(args, "--username") ?? "", current.Username, until);
                PrintResult(r.Success, r.Message);
                return r.Success;
            }

            // --listUsers
            if (Has(args, "--listUsers"))
            {
                if (!CheckRole(current, UserRole.Admin)) return false;
                _userMgmt!.PrintUsers();
                return true;
            }

            // --watchdog
            if (Has(args, "--watchdog"))
            {
                bool isAdminOrOp = current.Role == UserRole.Admin || current.Role == UserRole.Operator;
                if (Has(args, "--start"))
                {
                    if (!isAdminOrOp) { PrintResult(false, "Недостаточно прав."); return false; }
                    _watchdog!.Start();
                    return true;
                }
                if (Has(args, "--stop"))
                {
                    if (!isAdminOrOp) { PrintResult(false, "Недостаточно прав."); return false; }
                    _watchdog!.Stop();
                    return true;
                }
                if (Has(args, "--status"))
                {
                    if (!isAdminOrOp) { PrintResult(false, "Недостаточно прав."); return false; }
                    _watchdog!.PrintStatus();
                    return true;
                }
                if (Has(args, "--set-threshold"))
                {
                    if (!CheckRole(current, UserRole.Admin)) return false;
                    if (!InputValidator.TryParseThreshold(Get(args, "--cpu"), out int cpu) ||
                        !InputValidator.TryParseThreshold(Get(args, "--ram"), out int ram) ||
                        !InputValidator.TryParseThreshold(Get(args, "--hdd"), out int hdd))
                    { PrintResult(false, "Значения CPU/RAM/HDD: целые числа от 0 до 100."); return false; }
                    var r = _watchdog!.SetThresholds(cpu, ram, hdd);
                    PrintResult(r.Success, r.Message);
                    return r.Success;
                }
                PrintResult(false, "Неизвестный флаг watchdog. Введите --help.");
                return false;
            }

            // --logs
            if (Has(args, "--logs"))
            {
                if (current.Role == UserRole.User)
                { PrintResult(false, "Недостаточно прав для просмотра журналов."); return false; }
                var type = Get(args, "--type") ?? "all";
                if (current.Role == UserRole.Operator && type != "watchdog")
                { PrintResult(false, "Оператор может просматривать только журнал watchdog."); return false; }

                int limit = 30;
                if (Get(args, "--limit") is { } ll) InputValidator.TryParseLimit(ll, out limit);
                var logFile = AppLogger.GetTodayLogPath();
                if (System.IO.File.Exists(logFile))
                {
                    var lines = ReadLogFile(logFile);
                    // Фильтрация по типу если указан
                    var filtered = type == "all"
                        ? lines
                        : lines.Where(l => l.Contains($"[{type}]")).ToArray();
                    int start = Math.Max(0, filtered.Length - limit);
                    Console.WriteLine($"\n  Файл: {logFile}");
                    Console.WriteLine($"  Тип: {type}. Последние {Math.Min(limit, filtered.Length)} записей:\n");
                    for (int i = start; i < filtered.Length; i++)
                        Console.WriteLine("  " + filtered[i]);
                    if (filtered.Length == 0)
                        Console.WriteLine($"  Записей типа [{type}] не найдено.");
                }
                else PrintResult(false, $"Файл журнала не найден: {logFile}");
                return true;
            }

            // --update
            if (Has(args, "--update"))
            {
                if (Has(args, "--force"))
                {
                    if (!CheckRole(current, UserRole.Admin)) return false;
                    var r = await _updater!.ForceUpdateAsync();
                    PrintResult(r.Success, r.Message);
                    return r.Success;
                }
                await _updater!.CheckAndNotifyAsync();
                return true;
            }

            PrintResult(false, $"Неизвестная команда: {args[0]}. Введите --help для справки.");
            return false;
        }

        // ═══════════════════════════════════════════════════════════════
        //  ВЫВОД СПРАВКИ
        // ═══════════════════════════════════════════════════════════════
        static void PrintBanner()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine();
            Console.WriteLine("  ╔══════════════════════════════════════════════╗");
            Console.WriteLine("  ║       NoteApp v1.0 — Система заметок         ║");
            Console.WriteLine("  ║       VPN-инфраструктура / Windows            ║");
            Console.WriteLine("  ╚══════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  Введите логин и пароль для входа.");
            Console.WriteLine("  Для выхода введите: exit");
            Console.ResetColor();
            Console.WriteLine();
        }

        static void PrintHelp(User user)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  ─── Доступные команды ───────────────────────────────────────");
            Console.ResetColor();

            PrintCmd("--addNewNote \"<текст>\"",                     "Добавить заметку");
            PrintCmd("--listNotes [--from ГГГГ-ММ-ДД] [--limit N]", "Список заметок");
            PrintCmd("--getNote --id <N>",                           "Просмотр заметки по ID");

            if (user.Role == UserRole.Admin || user.Role == UserRole.Operator)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  ─── Watchdog ────────────────────────────────────────────────");
                Console.ResetColor();
                PrintCmd("--watchdog --start",                           "Запустить мониторинг");
                PrintCmd("--watchdog --stop",                            "Остановить мониторинг");
                PrintCmd("--watchdog --status",                          "Метрики CPU / RAM / HDD");
                if (user.Role == UserRole.Admin)
                    PrintCmd("--watchdog --set-threshold --cpu N --ram N --hdd N", "Настроить пороги");
            }

            if (user.Role == UserRole.Admin)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  ─── Пользователи ────────────────────────────────────────────");
                Console.ResetColor();
                PrintCmd("--listUsers",                                      "Список пользователей");
                PrintCmd("--addUser --username X --password Y --role Z",     "Добавить пользователя");
                PrintCmd("--removeUser --username X",                        "Удалить пользователя");
                PrintCmd("--blockUser --username X [--until ГГГГ-ММ-ДД]",  "Заблокировать");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  ─── Прочее ──────────────────────────────────────────────────");
                Console.ResetColor();
                PrintCmd("--logs [--type auth|watchdog|db|user] [--limit N]", "Журнал событий");
            }

            PrintCmd("--update",   "Проверить обновления");
            PrintCmd("--logout",   "Выйти из аккаунта");
            PrintCmd("exit",       "Закрыть программу");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  ─────────────────────────────────────────────────────────────");
            Console.ResetColor();
        }

        static void PrintHelpCli()
        {
            Console.WriteLine("NoteApp v1.0 — Консольная система заметок\n");
            Console.WriteLine("Запуск без аргументов  → авторизация и работа в сессии\n");
            Console.WriteLine("КОМАНДЫ (CLI-режим):");
            Console.WriteLine("  --login <user> --password <pwd>                   Авторизация");
            Console.WriteLine("  --logout                                           Завершение сессии");
            Console.WriteLine("  --addNewNote \"<текст>\"                             Добавить заметку");
            Console.WriteLine("  --listNotes [--from ГГГГ-ММ-ДД] [--limit N]       Список заметок");
            Console.WriteLine("  --getNote --id <N>                                Просмотр заметки");
            Console.WriteLine("  --addUser --username X --password Y --role Z       Добавить польз.");
            Console.WriteLine("  --removeUser --username X                          Удалить польз.");
            Console.WriteLine("  --blockUser --username X [--until ГГГГ-ММ-ДД]    Заблокировать");
            Console.WriteLine("  --listUsers                                        Список польз.");
            Console.WriteLine("  --watchdog --start|--stop|--status                Watchdog");
            Console.WriteLine("  --watchdog --set-threshold --cpu N --ram N --hdd N");
            Console.WriteLine("  --logs [--type auth|watchdog|db|user] [--limit N] Журнал");
            Console.WriteLine("  --update [--force]                                 Обновление");
        }

        // ═══════════════════════════════════════════════════════════════
        //  УТИЛИТЫ
        // ═══════════════════════════════════════════════════════════════
        static void PrintCmd(string cmd, string desc)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"  {cmd}");
            Console.ResetColor();
            int pad = Math.Max(1, 52 - cmd.Length);
            Console.WriteLine($"{new string(' ', pad)}{desc}");
        }

        static void PrintResult(bool success, string message)
        {
            Console.WriteLine();
            Console.ForegroundColor = success ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine($"  {(success ? "✓" : "✗")} {message}");
            Console.ResetColor();
        }

        static bool CheckRole(User user, UserRole required)
        {
            if (user.Role == required || (required == UserRole.Operator && user.Role == UserRole.Admin))
                return true;
            PrintResult(false, "Недостаточно прав для выполнения этой операции.");
            return false;
        }

        static string ReadPassword()
        {
            var pwd = "";
            ConsoleKeyInfo key;
            while ((key = Console.ReadKey(true)).Key != ConsoleKey.Enter)
            {
                if (key.Key == ConsoleKey.Backspace && pwd.Length > 0)
                { pwd = pwd[..^1]; Console.Write("\b \b"); }
                else if (key.Key != ConsoleKey.Backspace)
                { pwd += key.KeyChar; Console.Write("*"); }
            }
            Console.WriteLine();
            return pwd;
        }

        // Разбор строки команды с учётом кавычек: --addNewNote "текст с пробелами"
        static string[] ParseArgs(string input)
        {
            var args = new System.Collections.Generic.List<string>();
            var current = "";
            bool inQuotes = false;

            foreach (char c in input)
            {
                if (c == '"') { inQuotes = !inQuotes; continue; }
                if (c == ' ' && !inQuotes)
                {
                    if (!string.IsNullOrEmpty(current)) { args.Add(current); current = ""; }
                    continue;
                }
                current += c;
            }
            if (!string.IsNullOrEmpty(current)) args.Add(current);
            return args.ToArray();
        }


        // Читаем лог-файл пока Serilog держит его открытым
        static string[] ReadLogFile(string path)
        {
            using var stream = new System.IO.FileStream(
                path,
                System.IO.FileMode.Open,
                System.IO.FileAccess.Read,
                System.IO.FileShare.ReadWrite);
            using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
            var lines = new System.Collections.Generic.List<string>();
            string? line;
            while ((line = reader.ReadLine()) != null)
                lines.Add(line);
            return lines.ToArray();
        }

        static bool Has(string[] args, string flag) =>
            args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

        static string? Get(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
