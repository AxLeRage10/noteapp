using System;
using Serilog;
using Serilog.Events;

namespace NoteApp.Infrastructure
{
    public static class AppLogger
    {
        private static string _logDir = "";

        public static void Configure()
        {
            _logDir = Environment.GetEnvironmentVariable("NOTEAPP_LOG_DIR")
                      ?? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

            System.IO.Directory.CreateDirectory(_logDir);

            // Serilog с RollingInterval.Day сам добавляет дату к имени:
            // noteapp-.log → noteapp-20260509.log
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console(
                    restrictedToMinimumLevel: LogEventLevel.Warning,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}")
                .WriteTo.File(
                    path: System.IO.Path.Combine(_logDir, "noteapp-.log"),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u4}] [{Type}] {Message:lj}{NewLine}{Exception}",
                    retainedFileCountLimit: 30)
                .CreateLogger();
        }

        // Возвращает точный путь к файлу журнала за сегодня
        public static string GetTodayLogPath()
        {
            // Serilog создаёт: noteapp-20260509.log
            var fileName = $"noteapp-{DateTime.Today:yyyyMMdd}.log";
            return System.IO.Path.Combine(_logDir, fileName);
        }

        public static void Auth(string message, bool isError = false)
        {
            if (isError) Log.Warning("[auth] {Msg}", message);
            else         Log.Information("[auth] {Msg}", message);
        }

        public static void UserOp(string message)    => Log.Information("[user] {Msg}", message);
        public static void WatchdogLog(string message, bool warn = false)
        {
            if (warn) Log.Warning("[watchdog] {Msg}", message);
            else      Log.Information("[watchdog] {Msg}", message);
        }
        public static void UpdateLog(string message) => Log.Information("[update] {Msg}", message);
        public static void DbError(string message)   => Log.Error("[db] {Msg}", message);
        public static void Fatal(Exception ex)       => Log.Fatal(ex, "[fatal] Критическая ошибка");
        public static void Close()                   => Log.CloseAndFlush();
    }
}
