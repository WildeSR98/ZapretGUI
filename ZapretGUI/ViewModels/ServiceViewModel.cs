using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Application = System.Windows.Application;

using ZapretGUI.Views;

namespace ZapretGUI.ViewModels;

/// <summary>
/// Feature ViewModel для вкладки "Сервис".
/// Изолирует логику Game Filter, IPSet и zapret-service от MainViewModel.
/// </summary>
public sealed partial class ServiceViewModel : ObservableObject
{
    private readonly Func<string> _getEngineDir;
    private readonly Func<string?> _getSelectedProfileDisplayName;
    private readonly Action<string> _addAppLog;

    private static readonly Dictionary<string, string> _protocolToFile = new()
    {
        ["TCP и UDP"] = "all",
        ["TCP"] = "tcp",
        ["UDP"] = "udp"
    };

    private static readonly Dictionary<string, string> _fileToProtocol = new(StringComparer.OrdinalIgnoreCase)
    {
        ["all"] = "TCP и UDP",
        ["tcp"] = "TCP",
        ["udp"] = "UDP"
    };

    public ObservableCollection<string> ServiceLogs { get; } = new();
    public List<string> GameFilterProtocols { get; } = ["TCP и UDP", "TCP", "UDP"];

    [ObservableProperty] private bool gameFilterEnabled;
    [ObservableProperty] private string gameFilterProtocol = "TCP и UDP";
    [ObservableProperty] private string ipSetMode = "—";
    [ObservableProperty] private string zapretServiceStatus = "—";
    [ObservableProperty] private bool isServiceBusy;

    private string EngineDir => _getEngineDir();
    private string GameFilterFlagPath => Path.Combine(EngineDir, "utils", "game_filter.enabled");
    private string IpSetFilePath => Path.Combine(EngineDir, "lists", "ipset-all.txt");
    private string IpSetBackupPath => Path.Combine(EngineDir, "lists", "ipset-all.txt.backup");

    partial void OnGameFilterProtocolChanged(string value)
    {
        if (GameFilterEnabled)
        {
            try
            {
                var utilsDir = Path.GetDirectoryName(GameFilterFlagPath)!;
                Directory.CreateDirectory(utilsDir);
                File.WriteAllText(GameFilterFlagPath, ProtocolToFileValue(value));
                AddLog($"🎮 Game Filter протокол изменён на {value}");
            }
            catch (Exception ex)
            {
                AddLog($"❌ Ошибка обновления Game Filter: {ex.Message}");
            }
        }
    }

    public ServiceViewModel(
        Func<string> getEngineDir,
        Func<string?> getSelectedProfileDisplayName,
        Action<string> addAppLog)
    {
        _getEngineDir = getEngineDir;
        _getSelectedProfileDisplayName = getSelectedProfileDisplayName;
        _addAppLog = addAppLog;
    }

    private string ProtocolToFileValue(string protocol) =>
        _protocolToFile.TryGetValue(protocol, out var v) ? v : "udp";

    private string FileValueToProtocol(string fileValue) =>
        _fileToProtocol.TryGetValue(fileValue, out var v) ? v : "UDP";

    private void AddLog(string message)
    {
        ServiceLogs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (ServiceLogs.Count > 50)
            ServiceLogs.RemoveAt(0);
    }

    public void Refresh()
    {
        // Game Filter
        if (File.Exists(GameFilterFlagPath))
        {
            GameFilterEnabled = true;
            try
            {
                var content = File.ReadAllText(GameFilterFlagPath).Trim();
                GameFilterProtocol = FileValueToProtocol(content);
            }
            catch { }
        }
        else
        {
            GameFilterEnabled = false;
        }

        // IPSet mode
        if (!File.Exists(IpSetFilePath))
        {
            IpSetMode = "—";
        }
        else
        {
            try
            {
                var lines = File.ReadAllLines(IpSetFilePath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
                if (lines.Length == 0)
                    IpSetMode = "any";
                else if (lines.Length == 1 && lines[0].Trim() == "203.0.113.113/32")
                    IpSetMode = "none";
                else
                    IpSetMode = "loaded";
            }
            catch { IpSetMode = "—"; }
        }

        // Zapret service — via P/Invoke
        var state = ZapretGUI.Core.Services.WinServiceManager.GetState("zapret");
        ZapretServiceStatus = state switch
        {
            ZapretGUI.Core.Services.WinServiceManager.ServiceState.Running => "✅ Запущена",
            ZapretGUI.Core.Services.WinServiceManager.ServiceState.Stopped => "⏹ Остановлена",
            ZapretGUI.Core.Services.WinServiceManager.ServiceState.Starting => "⏳ Запускается...",
            ZapretGUI.Core.Services.WinServiceManager.ServiceState.Stopping => "⚠️ Останавливается...",
            ZapretGUI.Core.Services.WinServiceManager.ServiceState.NotInstalled => "❌ Не установлена",
            _ => "❓ Неизвестно"
        };
    }

    [RelayCommand]
    private void ToggleGameFilter()
    {
        try
        {
            var utilsDir = Path.GetDirectoryName(GameFilterFlagPath)!;
            Directory.CreateDirectory(utilsDir);

            if (File.Exists(GameFilterFlagPath))
            {
                File.Delete(GameFilterFlagPath);
                GameFilterEnabled = false;
                AddLog("🎮 Game Filter выключен");
                _addAppLog("Game Filter выключен");
            }
            else
            {
                File.WriteAllText(GameFilterFlagPath, ProtocolToFileValue(GameFilterProtocol));
                GameFilterEnabled = true;
                AddLog($"🎮 Game Filter включён ({GameFilterProtocol})");
                _addAppLog($"Game Filter включён ({GameFilterProtocol})");
            }

            AddLog("⚠️ Перезапустите zapret для применения изменений");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CycleIpSetMode()
    {
        try
        {
            var listsDir = Path.GetDirectoryName(IpSetFilePath)!;
            Directory.CreateDirectory(listsDir);

            if (IpSetMode == "loaded")
            {
                if (File.Exists(IpSetBackupPath)) File.Delete(IpSetBackupPath);
                if (File.Exists(IpSetFilePath)) File.Move(IpSetFilePath, IpSetBackupPath);
                File.WriteAllText(IpSetFilePath, "203.0.113.113/32\r\n");
                IpSetMode = "none";
                AddLog("🔒 IPSet → none (фильтрация отключена)");
            }
            else if (IpSetMode == "none")
            {
                File.WriteAllText(IpSetFilePath, "");
                IpSetMode = "any";
                AddLog("🌐 IPSet → any (все адреса)");
            }
            else
            {
                if (File.Exists(IpSetBackupPath))
                {
                    if (File.Exists(IpSetFilePath)) File.Delete(IpSetFilePath);
                    File.Move(IpSetBackupPath, IpSetFilePath);
                    IpSetMode = "loaded";
                    AddLog("📋 IPSet → loaded (список восстановлен)");
                }
                else
                {
                    AddLog("⚠️ Нет бэкапа IPSet. Обновите список через кнопку ниже");
                    return;
                }
            }

            AddLog("⚠️ Перезапустите zapret для применения изменений");
            Refresh();
        }
        catch (Exception ex)
        {
            AddLog($"❌ Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task UpdateIpSetList()
    {
        IsServiceBusy = true;
        AddLog("⬇️ Скачиваем ipset-all.txt...");

        try
        {
            var url = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/ipset-service.txt";
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "ZapretGUI");
            var content = await http.GetStringAsync(url);

            var listsDir = Path.GetDirectoryName(IpSetFilePath)!;
            Directory.CreateDirectory(listsDir);
            await File.WriteAllTextAsync(IpSetFilePath, content);

            Application.Current.Dispatcher.Invoke(() =>
            {
                AddLog($"✅ IPSet обновлён ({content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length} записей)");
                Refresh();
            });
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AddLog($"❌ Ошибка скачивания IPSet: {ex.Message}");
            });
        }
        finally
        {
            Application.Current.Dispatcher.Invoke(() => IsServiceBusy = false);
        }
    }

    [RelayCommand]
    private async Task UpdateHostsFile()
    {
        IsServiceBusy = true;
        AddLog("⬇️ Проверяем hosts файл...");

        try
        {
            var hostsUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/hosts";
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "ZapretGUI");
            var newContent = await http.GetStringAsync(hostsUrl);
            var newLines = newContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

            if (!File.Exists(hostsPath))
            {
                Application.Current.Dispatcher.Invoke(() => AddLog("❌ Файл hosts не найден"));
                return;
            }

            var currentHosts = await File.ReadAllTextAsync(hostsPath);
            var firstLine = newLines.FirstOrDefault()?.Trim() ?? "";
            var lastLine = newLines.LastOrDefault()?.Trim() ?? "";

            if (currentHosts.Contains(firstLine) && currentHosts.Contains(lastLine))
            {
                Application.Current.Dispatcher.Invoke(() => AddLog("✅ Hosts файл актуален"));
                return;
            }

            var tempPath = Path.GetTempFileName();
            await File.WriteAllTextAsync(tempPath, currentHosts + "\n" + newContent);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c copy /Y \"{tempPath}\" \"{hostsPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true
            });

            Application.Current.Dispatcher.Invoke(() =>
            {
                AddLog($"✅ Hosts обновлён ({newLines.Length} записей добавлено)");
                Refresh();
            });
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AddLog($"❌ Ошибка обновления hosts: {ex.Message}");
            });
        }
        finally
        {
            Application.Current.Dispatcher.Invoke(() => IsServiceBusy = false);
        }
    }

    [RelayCommand]
    private void InstallZapretService()
    {
        var profileName = _getSelectedProfileDisplayName();
        if (profileName is null)
        {
            AddLog("❌ Сначала выберите профиль");
            return;
        }

        AddLog($"🔧 Установка службы zapret с профилем «{profileName}»...");

        try
        {
            var serviceBat = Path.Combine(EngineDir, "service.bat");
            var profileBat = Path.Combine(EngineDir, $"{profileName}.bat");

            // Prefer P/Invoke if profile bat exists
            if (File.Exists(profileBat))
            {
                var binPath = $"\"{Path.Combine(EngineDir, "bin", "winws.exe")}\"";
                // Read strategy args from bat file
                var batContent = File.ReadAllText(profileBat);
                var argsLine = batContent.Split('\n')
                    .Select(l => l.Trim())
                    .FirstOrDefault(l => l.Contains("winws.exe") || l.Contains("--wf-tcp") || l.Contains("--filter-tcp"));

                if (argsLine != null)
                {
                    // Extract args after winws.exe 
                    var idx = argsLine.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase);
                    var args = idx >= 0 ? argsLine[(idx + "winws.exe".Length)..].Trim() : argsLine;
                    args = args.TrimEnd('\\', '^').Trim();
                    binPath = $"\"{Path.Combine(EngineDir, "bin", "winws.exe")}\" {args}";
                }

                var ok = ZapretGUI.Core.Services.WinServiceManager.Install(
                    "zapret", "Zapret DPI Bypass", "DPI bypass service managed by ZapretGUI",
                    binPath, autoStart: true);

                if (ok)
                {
                    AddLog("✅ Служба zapret установлена и запущена");
                    _addAppLog($"Служба zapret установлена с профилем «{profileName}»");
                }
                else
                {
                    AddLog("❌ Не удалось установить службу (нет прав администратора?)");
                }
                Refresh();
                return;
            }

            // Fallback to service.bat
            if (!File.Exists(serviceBat))
            {
                AddLog("❌ service.bat не найден в engine/");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{serviceBat}\" admin",
                WorkingDirectory = EngineDir,
                UseShellExecute = true,
                Verb = "runas"
            });

            AddLog("✅ service.bat запущен с правами администратора");
        }
        catch (Exception ex)
        {
            AddLog($"❌ Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ForceStopZapretService()
    {
        if (!CustomDialog.Show(
            "⚠️ Подтверждение остановки",
            "Вы действительно хотите принудительно остановить службу zapret?\n\nВсе активные соединения через zapret будут прерваны.",
            "Остановить", "Отмена", isDanger: true)) return;

        AddLog("⏹ Принудительная остановка службы zapret...");

        try
        {
            // Try P/Invoke first
            ZapretGUI.Core.Services.WinServiceManager.Stop("zapret");
            Thread.Sleep(1000);

            // Kill winws.exe processes as fallback
            foreach (var p in Process.GetProcessesByName("winws"))
            {
                try { p.Kill(true); } catch { }
            }

            AddLog("✅ Служба zapret остановлена");
            _ = Task.Delay(2000).ContinueWith(_ =>
                Application.Current.Dispatcher.Invoke(Refresh));
        }
        catch (Exception ex)
        {
            AddLog($"❌ Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RemoveZapretService()
    {
        if (!CustomDialog.Show(
            "⚠️ Удаление службы",
            "Удалить службу zapret из Windows?\n\nЭто не удалит файлы программы, только системную службу.",
            "Удалить", "Отмена", isDanger: true)) return;

        AddLog("🗑 Удаление службы zapret...");
        try
        {
            var ok = ZapretGUI.Core.Services.WinServiceManager.Remove("zapret");
            if (ok)
                AddLog("✅ Служба zapret удалена");
            else
                AddLog("❌ Не удалось удалить (нет прав администратора?)");
            Refresh();
        }
        catch (Exception ex)
        {
            AddLog($"❌ Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private void StartZapretService()
    {
        AddLog("▶️ Запуск службы zapret...");
        try
        {
            var ok = ZapretGUI.Core.Services.WinServiceManager.Start("zapret");
            if (ok)
                AddLog("✅ Служба запущена");
            else
                AddLog("❌ Не удалось запустить (не установлена или нет прав?)");
            _ = Task.Delay(1500).ContinueWith(_ =>
                Application.Current.Dispatcher.Invoke(Refresh));
        }
        catch (Exception ex)
        {
            AddLog($"❌ Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RefreshServiceInfo()
    {
        Refresh();
        AddLog("🔄 Статус обновлён");
    }
}
