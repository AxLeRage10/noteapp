using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
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
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private readonly string _repoOwner;
        private readonly string _repoName;
        private readonly string _versionFile;

        public UpdateService()
        {
            // Читаем из переменных окружения или используем дефолт
            _repoOwner   = Environment.GetEnvironmentVariable("NOTEAPP_GITHUB_OWNER") ?? "AxLeRage10";
            _repoName    = Environment.GetEnvironmentVariable("NOTEAPP_GITHUB_REPO")  ?? "noteapp";
            _versionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.json");

            // GitHub API требует User-Agent
            Http.DefaultRequestHeaders.UserAgent.Clear();
            Http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("NoteApp", GetCurrentVersion()));
            Http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        public string GetCurrentVersion()
        {
            try
            {
                var json = File.ReadAllText(_versionFile);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.GetProperty("version").GetString() ?? "1.0.0";
            }
            catch { return "1.0.0"; }
        }

        public async Task CheckAndNotifyAsync()
        {
            try
            {
                var info = await FetchLatestReleaseAsync();
                if (info == null)
                {
                    AppLogger.UpdateLog("Сервер обновлений недоступен или нет релизов.");
                    return;
                }

                var current = new Version(GetCurrentVersion());
                var remote  = new Version(info.Version);

                if (remote > current)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  ★ Доступно обновление: {info.Version}");
                    Console.ResetColor();
                    Console.WriteLine($"  Текущая версия: {current}");
                    Console.WriteLine($"  Новая версия:   {info.Version}");
                    if (!string.IsNullOrEmpty(info.ReleaseNotes))
                        Console.WriteLine($"  Что нового:     {info.ReleaseNotes}");
                    Console.WriteLine($"  Скачать:        {info.DownloadUrl}");
                    Console.WriteLine();
                    Console.WriteLine("  Для установки выполните: --update --force");
                }
                else
                {
                    Console.WriteLine($"  Обновлений нет. Установленная версия актуальна: {current}");
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
                var info = await FetchLatestReleaseAsync();
                if (info == null)
                    return OperationResult.Fail("Сервер обновлений недоступен или нет доступных релизов.");

                var current = new Version(GetCurrentVersion());
                var remote  = new Version(info.Version);

                if (remote <= current)
                    return OperationResult.Fail($"Обновлений нет. Версия актуальна: {current}");

                if (string.IsNullOrEmpty(info.DownloadUrl))
                    return OperationResult.Fail("В релизе нет файла для скачивания (.exe или .zip).");

                Console.WriteLine($"\n  Загрузка версии {info.Version}...");
                return await DownloadAndInstallAsync(info);
            }
            catch (Exception ex)
            {
                AppLogger.UpdateLog($"Ошибка обновления: {ex.Message}");
                return OperationResult.Fail($"Ошибка: {ex.Message}");
            }
        }

        // ─── Получить информацию о последнем релизе с GitHub ─────────
        private async Task<UpdateInfo?> FetchLatestReleaseAsync()
        {
            try
            {
                var url  = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
                var json = await Http.GetStringAsync(url);

                using var doc  = JsonDocument.Parse(json);
                var root       = doc.RootElement;

                // tag_name обычно "v1.2.0" — убираем 'v'
                var tagName = root.GetProperty("tag_name").GetString() ?? "0.0.0";
                var version = tagName.TrimStart('v');

                var releaseNotes = root.TryGetProperty("body", out var body)
                    ? body.GetString() ?? "" : "";

                // Ищем .exe или .zip среди assets
                string downloadUrl = "";
                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            break;
                        }
                    }
                }

                // Если нет assets — используем zipball (исходники)
                if (string.IsNullOrEmpty(downloadUrl))
                    downloadUrl = root.TryGetProperty("zipball_url", out var zip)
                        ? zip.GetString() ?? "" : "";

                return new UpdateInfo
                {
                    Version      = version,
                    DownloadUrl  = downloadUrl,
                    ReleaseNotes = releaseNotes.Length > 200
                        ? releaseNotes[..200] + "..." : releaseNotes,
                    MinRequiredVersion = "0.0.0"
                };
            }
            catch (HttpRequestException ex)
            {
                AppLogger.UpdateLog($"GitHub API недоступен: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                AppLogger.UpdateLog($"Ошибка разбора ответа GitHub: {ex.Message}");
                return null;
            }
        }

        // ─── Скачать и установить обновление ─────────────────────────
        private async Task<OperationResult> DownloadAndInstallAsync(UpdateInfo info)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"noteapp-update-{info.Version}.tmp");
            try
            {
                // Скачиваем с прогрессом
                var response = await Http.GetAsync(info.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var total    = response.Content.Headers.ContentLength ?? -1;
                var bytes    = await response.Content.ReadAsByteArrayAsync();

                await File.WriteAllBytesAsync(tempPath, bytes);

                Console.WriteLine($"  Загружено: {bytes.Length / 1024} КБ");

                // Обновляем version.json
                var newVersionJson = JsonSerializer.Serialize(new
                {
                    version     = info.Version,
                    releaseDate = DateTime.Today.ToString("yyyy-MM-dd"),
                    minRequiredVersion = "0.0.0"
                }, new JsonSerializerOptions { WriteIndented = true });

                await File.WriteAllTextAsync(_versionFile, newVersionJson);

                // Удаляем временный файл
                if (File.Exists(tempPath)) File.Delete(tempPath);

                AppLogger.UpdateLog($"Обновление {info.Version} успешно установлено.");
                return OperationResult.Ok(
                    $"Версия {info.Version} загружена. Обновите version.json и перезапустите приложение.");
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                AppLogger.UpdateLog($"Ошибка установки: {ex.Message}");
                return OperationResult.Fail($"Ошибка загрузки: {ex.Message}");
            }
        }
    }
}
