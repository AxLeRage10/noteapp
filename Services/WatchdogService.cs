using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NoteApp.Core;
using NoteApp.Infrastructure;

namespace NoteApp.Services
{
    public class WatchdogService
    {
        private Thread?         _thread;
        private bool            _running;
        private WatchdogConfig  _config;
        private readonly DatabaseFactory _db;

        public WatchdogService(DatabaseFactory db)
        {
            _db     = db;
            _config = LoadConfig();
        }

        public void Start()
        {
            if (_running) { Console.WriteLine("Watchdog уже запущен."); return; }
            _running = true;
            _thread  = new Thread(MonitorLoop) { IsBackground = true, Name = "WatchdogThread" };
            _thread.Start();
            AppLogger.WatchdogLog("Watchdog запущен.");
            Console.WriteLine($"Watchdog активен. Интервал опроса: {_config.IntervalSec} сек.");
        }

        public void Stop()
        {
            _running = false;
            AppLogger.WatchdogLog("Watchdog остановлен.");
            Console.WriteLine("Watchdog остановлен.");
        }

        public void PrintStatus()
        {
            var (cpu, ram, hdd) = GetMetrics();
            Console.WriteLine($"Watchdog {(_running ? "активен" : "остановлен")}. Интервал: {_config.IntervalSec} сек.");
            Console.WriteLine($"{"Метрика",-8} {"Текущее",-10} {"Порог",-8} Статус");
            Console.WriteLine(new string('-', 40));
            PrintMetric("CPU",  cpu,  _config.CpuThreshold);
            PrintMetric("RAM",  ram,  _config.RamThreshold);
            PrintMetric("HDD",  hdd,  _config.HddThreshold);
        }

        public OperationResult SetThresholds(int cpu, int ram, int hdd, int? interval = null)
        {
            if (cpu < 0 || cpu > 100 || ram < 0 || ram > 100 || hdd < 0 || hdd > 100)
                return OperationResult.Fail("Пороговые значения должны быть в диапазоне 0-100.");

            _config.CpuThreshold = cpu;
            _config.RamThreshold = ram;
            _config.HddThreshold = hdd;
            if (interval.HasValue)
            {
                if (interval.Value < 10 || interval.Value > 3600)
                    return OperationResult.Fail("Интервал опроса: 10-3600 секунд.");
                _config.IntervalSec = interval.Value;
            }

            SaveConfig();
            AppLogger.WatchdogLog($"Пороги обновлены: CPU={cpu}% RAM={ram}% HDD={hdd}%");
            return OperationResult.Ok($"Пороговые значения обновлены: CPU={cpu}% RAM={ram}% HDD={hdd}%.");
        }

        // ─── Приватные методы ─────────────────────────────────────────────

        private void MonitorLoop()
        {
            while (_running)
            {
                try
                {
                    var (cpu, ram, hdd) = GetMetrics();
                    if (cpu > _config.CpuThreshold)
                        AppLogger.WatchdogLog($"Превышен порог CPU: {cpu}% (порог: {_config.CpuThreshold}%)", true);
                    if (ram > _config.RamThreshold)
                        AppLogger.WatchdogLog($"Превышен порог RAM: {ram}% (порог: {_config.RamThreshold}%)", true);
                    if (hdd > _config.HddThreshold)
                        AppLogger.WatchdogLog($"Превышен порог HDD: {hdd}% (порог: {_config.HddThreshold}%)", true);
                }
                catch (Exception ex) { AppLogger.DbError($"Watchdog ошибка: {ex.Message}"); }

                Thread.Sleep(_config.IntervalSec * 1000);
            }
        }

        private static (int Cpu, int Ram, int Hdd) GetMetrics()
        {
            // CPU через PerformanceCounter (Windows)
            int cpu = 0;
            try
            {
                using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                cpuCounter.NextValue(); // первое чтение всегда 0
                Thread.Sleep(500);
                cpu = (int)cpuCounter.NextValue();
            }
            catch { cpu = 0; }

            // RAM
            int ram = 0;
            try
            {
                using var ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                var totalRamMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024;
                var availMb    = (long)ramCounter.NextValue();
                ram = totalRamMb > 0 ? (int)(100 - availMb * 100 / totalRamMb) : 0;
            }
            catch { ram = 0; }

            // HDD — диск C:
            int hdd = 0;
            try
            {
                var drive = new DriveInfo("C");
                hdd = (int)((drive.TotalSize - drive.AvailableFreeSpace) * 100 / drive.TotalSize);
            }
            catch { hdd = 0; }

            return (cpu, ram, hdd);
        }

        private static void PrintMetric(string name, int value, int threshold)
        {
            var status = value > threshold ? "WARN" : "OK";
            Console.WriteLine($"{name,-8} {value + "%",-10} {threshold + "%",-8} [{status}]");
        }

        private WatchdogConfig LoadConfig()
        {
            try
            {
                using var conn = _db.CreateConnection();
                conn.Open();
                using var cmd = new Npgsql.NpgsqlCommand(
                    "SELECT cpu_threshold, ram_threshold, hdd_threshold, interval_sec FROM watchdog_config LIMIT 1", conn);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                    return new WatchdogConfig {
                        CpuThreshold = r.GetInt32(0), RamThreshold = r.GetInt32(1),
                        HddThreshold = r.GetInt32(2), IntervalSec  = r.GetInt32(3)
                    };
            }
            catch { /* использовать дефолты */ }
            return new WatchdogConfig();
        }

        private void SaveConfig()
        {
            try
            {
                using var conn = _db.CreateConnection();
                conn.Open();
                using var cmd = new Npgsql.NpgsqlCommand(
                    "UPDATE watchdog_config SET cpu_threshold=@c, ram_threshold=@r, hdd_threshold=@h, interval_sec=@i", conn);
                cmd.Parameters.AddWithValue("c", _config.CpuThreshold);
                cmd.Parameters.AddWithValue("r", _config.RamThreshold);
                cmd.Parameters.AddWithValue("h", _config.HddThreshold);
                cmd.Parameters.AddWithValue("i", _config.IntervalSec);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { AppLogger.DbError(ex.Message); }
        }
    }
}
