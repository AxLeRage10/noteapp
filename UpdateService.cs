using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using NoteApp.Core;
using NoteApp.Infrastructure;

namespace NoteApp.Services
{
    public class UpdateService
    {
        private static readonly HttpClient Http = new()
            { Timeout = TimeSpan.FromSeconds(5) };

        private readonly string _updateUrl;
        private readonly string _versionFile;

        public UpdateService()
        {
            _updateUrl   = Environment.GetEnvironmentVariable("NOTEAPP_UPDATE_URL")
                           ?? "https://update.example.com";
            _versionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.json");
        }

        public string GetCurrentVersion()
        {
            try
            {
                var json = File.ReadAllText(_versionFile);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.GetProperty("version").GetString() ?? "0.0.0";
            }
            catch { return "0.0.0"; }
        }

        public async Task CheckAndNotifyAsync()
        {
            try
            {
                var currentVer = GetCurrentVersion();
                var info = await FetchUpdateInfoAsync();
                if (info == null) return;

                var remote  = new Version(info.Version);
                var current = new Version(currentVer);
                var minReq  = new Version(info.MinRequiredVersion);

                if (current < minReq)
                {
                    Console.WriteLine($"Критическое обновление: версия {currentVer} устарела.");
                    Console.WriteLine($"Минимально допустимая: {info.MinRequiredVersion}.");
                    Console.WriteLine("Выполняется принудительное обновление...");
                    await DownloadAndInstallAsync(info);
                    return;
                }

                if (remote > current)
                {
                    Console.WriteLine($"Текущая версия: {currentVer}");
                    Console.WriteLine($"Доступна версия: {info.Version}");
                    Console.WriteLine($"Примечания: {info.ReleaseNotes}");
                    Console.WriteLine("Установить обновление? (д/н): ");
                }
                else
                {
                    Console.WriteLine($"Обновлений не найдено. Установленная версия актуальна: {currentVer}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.UpdateLog($"Ошибка проверки обновлений: {ex.Message}");
            }
        }

        public async Task<OperationResult> ForceUpdateAsync()
        {
            try
            {
                var info = await FetchUpdateInfoAsync();
                if (info == null)
                    return OperationResult.Fail("Сервер обновлений недоступен или версия актуальна.");

                var current = new Version(GetCurrentVersion());
                if (new Version(info.Version) <= current)
                    return OperationResult.Fail($"Обновлений не найдено. Версия актуальна: {current}");

                return await DownloadAndInstallAsync(info);
            }
            catch (Exception ex)
            {
                AppLogger.UpdateLog($"Ошибка принудительного обновления: {ex.Message}");
                return OperationResult.Fail(ex.Message);
            }
        }

        private async Task<UpdateInfo?> FetchUpdateInfoAsync()
        {
            try
            {
                var json = await Http.GetStringAsync($"{_updateUrl}/latest.json");
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                return new UpdateInfo
                {
                    Version            = root.GetProperty("version").GetString()            ?? "",
                    DownloadUrl        = root.GetProperty("downloadUrl").GetString()        ?? "",
                    Sha256             = root.GetProperty("sha256").GetString()             ?? "",
                    ReleaseNotes       = root.GetProperty("releaseNotes").GetString()       ?? "",
                    MinRequiredVersion = root.TryGetProperty("minRequiredVersion", out var m)
                                        ? m.GetString() ?? "0.0.0" : "0.0.0"
                };
            }
            catch (Exception ex)
            {
                AppLogger.UpdateLog($"Сервер обновлений недоступен: {ex.Message}");
                return null;
            }
        }

        private async Task<OperationResult> DownloadAndInstallAsync(UpdateInfo info)
        {
            var tempPath = Path.GetTempFileName();
            try
            {
                Console.Write("Загрузка обновления...");
                var bytes = await Http.GetByteArrayAsync(info.DownloadUrl);
                await File.WriteAllBytesAsync(tempPath, bytes);
                Console.WriteLine(" Готово.");

                // Проверка SHA-256
                using var sha = SHA256.Create();
                var hash = Convert.ToHexString(sha.ComputeHash(bytes)).ToLower();
                if (hash != info.Sha256.ToLower())
                {
                    File.Delete(tempPath);
                    var msg = "Контрольная сумма SHA-256 не совпадает. Установка отменена.";
                    AppLogger.UpdateLog(msg);
                    return OperationResult.Fail(msg);
                }

                // Обновление version.json
                var newVer = JsonSerializer.Serialize(new { version = info.Version,
                    releaseDate = DateTime.Today.ToString("yyyy-MM-dd") });
                await File.WriteAllTextAsync(_versionFile, newVer);

                AppLogger.UpdateLog($"Обновление {info.Version} успешно установлено.");
                return OperationResult.Ok($"Обновление {info.Version} установлено. Перезапустите приложение.");
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                AppLogger.UpdateLog($"Ошибка установки: {ex.Message}");
                return OperationResult.Fail($"Ошибка установки обновления: {ex.Message}");
            }
        }
    }
}
