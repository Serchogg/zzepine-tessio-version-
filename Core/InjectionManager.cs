using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows; // Para MessageBox (WPF)
using GTAVInjector.Models;
using Microsoft.Win32;

namespace GTAVInjector.Core
{
    public enum InjectionResult
    {
        INJECT_OK,
        ERROR_OPEN_PROCESS,
        ERROR_DLL_NOTFOUND,
        ERROR_ALLOC,
        ERROR_WRITE,
        ERROR_CREATE_THREAD,
        ERROR_UNKNOWN
    }

    public static class InjectionManager
    {
        #region Windows API

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out UIntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr FindWindowA(string lpClassName, string lpWindowName);

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        private const uint PROCESS_CREATE_THREAD = 0x0002;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint PROCESS_VM_READ = 0x0010;

        private const uint MEM_COMMIT = 0x00001000;
        private const uint MEM_RESERVE = 0x00002000;
        private const uint PAGE_READWRITE = 4;

        #endregion

        // Constante compartida
        private const string DOCUMENTS_FOLDER = "GTA GGS Launcher";

        // Cache para IsGameRunning
        private static bool _lastCheckResult = false;
        private static ulong _lastCheckTime = 0;

        #region Proceso y Ventana

        /// <summary>
        /// Obtiene el PID del proceso de GTA V (Legacy o Enhanced).
        /// </summary>
        private static bool GetGameProcessId(out int pid)
        {
            pid = -1;
            string processName = SettingsManager.Settings.GameType == GameType.Legacy
                ? "GTA5.exe"
                : "GTA5_Enhanced.exe";

            try
            {
                var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName));
                foreach (var p in processes)
                {
                    try
                    {
                        if (string.Equals(p.ProcessName, Path.GetFileNameWithoutExtension(processName), StringComparison.OrdinalIgnoreCase))
                        {
                            pid = p.Id;
                            p.Dispose();
                            return true;
                        }
                    }
                    finally
                    {
                        p?.Dispose();
                    }
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Verifica si el juego está en ejecución (con caché de 3 segundos).
        /// </summary>
        public static bool IsGameRunning(bool noTime = false)
        {
            ulong now = GetTickCount64();
            if (_lastCheckTime == 0 || noTime || (now - _lastCheckTime) > 3000)
            {
                _lastCheckResult = GetGameProcessId(out _);
                _lastCheckTime = now;
            }
            return _lastCheckResult;
        }

        /// <summary>
        /// Verifica si la ventana del juego está visible.
        /// </summary>
        public static bool IsGameWindowVisible()
        {
            string className = SettingsManager.Settings.GameType == GameType.Legacy
                ? "grcWindow"
                : "sgaWindow";

            return FindWindowA(className, null) != IntPtr.Zero;
        }

        #endregion

        #region Lanzamiento y Terminación

        public static void LaunchGame()
        {
            if (IsGameRunning(true))
            {
                MessageBox.Show("El juego ya está ejecutándose.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var gameType = SettingsManager.Settings.GameType;
            var launcherType = SettingsManager.Settings.LauncherType;

            try
            {
                switch (launcherType)
                {
                    case LauncherType.Rockstar:
                        LaunchRockstar(gameType);
                        break;
                    case LauncherType.EpicGames:
                        LaunchEpicGames(gameType);
                        break;
                    case LauncherType.Steam:
                        LaunchSteam(gameType);
                        break;
                    default:
                        throw new NotSupportedException($"Tipo de launcher no soportado: {launcherType}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al intentar iniciar el juego:\n{ex.Message}",
                    "Error de lanzamiento", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void LaunchRockstar(GameType gameType)
        {
            try
            {
                string regKey = gameType == GameType.Legacy
                    ? @"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V"
                    : @"SOFTWARE\WOW6432Node\Rockstar Games\GTAV Enhanced";

                using var key = Registry.LocalMachine.OpenSubKey(regKey);
                string installPath = key?.GetValue("InstallFolder")?.ToString();

                if (string.IsNullOrEmpty(installPath))
                    throw new Exception("No se encontró la ruta de instalación de GTA V en el Registro.");

                string exePath = Path.Combine(installPath, "PlayGTAV.exe");

                if (!File.Exists(exePath))
                    throw new Exception($"PlayGTAV.exe no encontrado en: {exePath}");

                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al iniciar mediante Rockstar Launcher: {ex.Message}");
            }
        }

        private static void LaunchEpicGames(GameType gameType)
        {
            string appId = gameType == GameType.Legacy
                ? "9d2d0eb64d5c44529cece33fe2a46482"
                : "8769e24080ea413b8ebca3f1b8c50951";

            string uri = $"com.epicgames.launcher://apps/{appId}?action=launch&silent=true";

            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }

        private static void LaunchSteam(GameType gameType)
        {
            string appId = gameType == GameType.Legacy ? "271590" : "3240220";
            string uri = $"steam://run/{appId}";

            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }

        public static void KillGame()
        {
            if (!GetGameProcessId(out int pid))
            {
                MessageBox.Show("El juego no está ejecutándose.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill();
                process.WaitForExit(3000);
                Debug.WriteLine($"[TERMINAR] ✅ Proceso terminado: PID {pid}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al terminar el proceso (PID {pid}):\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Inyección

        /// <summary>
        /// Inyecta todas las DLLs habilitadas en la configuración.
        /// Usa la carpeta Documents\GTA GGS Launcher\Temp, limpia y copia con nombres únicos.
        /// </summary>
        public static void InjectAllDlls()
        {
            if (!GetGameProcessId(out int pid))
            {
                MessageBox.Show("El juego no está ejecutándose. Inicie GTA V primero.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string tempBasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                DOCUMENTS_FOLDER,
                "Temp"
            );

            // Preparar carpeta temporal
            try
            {
                if (!Directory.Exists(tempBasePath))
                {
                    Directory.CreateDirectory(tempBasePath);
                }
                else
                {
                    foreach (string file in Directory.EnumerateFiles(tempBasePath, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                        }
                        catch { /* ignorar */ }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo preparar la carpeta temporal:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var entries = SettingsManager.Settings?.DllEntries ?? new List<DllEntry>();
            int totalAttempted = 0;
            int totalInjected = 0;

            foreach (var entry in entries)
            {
                if (!entry.Enabled) continue;
                totalAttempted++;

                // Validar archivo
                if (string.IsNullOrWhiteSpace(entry.Path) || !File.Exists(entry.Path))
                {
                    MessageBox.Show($"La DLL no existe o la ruta está vacía:\n{entry.Path}",
                        "Advertencia", MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }

                try
                {
                    if (new FileInfo(entry.Path).Length == 0)
                    {
                        MessageBox.Show($"La DLL está vacía:\n{entry.Path}",
                            "Advertencia", MessageBoxButton.OK, MessageBoxImage.Warning);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"No se pudo verificar el tamaño de:\n{entry.Path}\nError: {ex.Message}",
                        "Advertencia", MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }

                // Generar nombre único
                string fileName = Path.GetFileName(entry.Path);
                string destPath = Path.Combine(tempBasePath, fileName);

                if (File.Exists(destPath))
                {
                    string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                    string ext = Path.GetExtension(fileName);
                    int i = 1;
                    do
                    {
                        destPath = Path.Combine(tempBasePath, $"{nameNoExt}_{i}{ext}");
                        i++;
                    } while (File.Exists(destPath));
                }

                // Copiar
                try
                {
                    File.Copy(entry.Path, destPath, true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al copiar la DLL a la carpeta temporal:\n{entry.Path} → {destPath}\n{ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    continue;
                }

                // Inyectar
                var result = InjectDllInternal(pid, destPath);
                if (result == InjectionResult.INJECT_OK)
                {
                    totalInjected++;
                }
                else
                {
                    string errorMsg = result switch
                    {
                        InjectionResult.ERROR_OPEN_PROCESS => "No se pudo abrir el proceso (¿ya terminó?)",
                        InjectionResult.ERROR_ALLOC => "Fallo al reservar memoria remota",
                        InjectionResult.ERROR_WRITE => "Fallo al escribir la ruta en la memoria",
                        InjectionResult.ERROR_CREATE_THREAD => "Fallo al crear el hilo remoto",
                        _ => $"Error desconocido: {result}"
                    };
                    MessageBox.Show($"Falló la inyección de:\n{entry.Path}\n\n{errorMsg}",
                        "Inyección fallida", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            MessageBox.Show($"Inyección completada.\nDLLs inyectadas: {totalInjected} de {totalAttempted}.",
                "Resultado", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Inyecta una única DLL (API pública).
        /// </summary>
        public static InjectionResult InjectDll(string dllPath)
        {
            if (!File.Exists(dllPath))
                return InjectionResult.ERROR_DLL_NOTFOUND;

            return GetGameProcessId(out int pid)
                ? InjectDllInternal(pid, dllPath)
                : InjectionResult.ERROR_OPEN_PROCESS;
        }

        /// <summary>
        /// Lógica interna de inyección (usa LoadLibraryA, ASCII, null-terminado).
        /// </summary>
        private static InjectionResult InjectDllInternal(int processId, string dllPath)
        {
            IntPtr hProcess = IntPtr.Zero;
            IntPtr allocMemAddress = IntPtr.Zero;

            try
            {
                hProcess = OpenProcess(
                    PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
                    PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                    false, processId);

                if (hProcess == IntPtr.Zero)
                    return InjectionResult.ERROR_OPEN_PROCESS;

                IntPtr loadLibraryAddr = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
                if (loadLibraryAddr == IntPtr.Zero)
                    return InjectionResult.ERROR_UNKNOWN;

                // ASCII + null-terminado (como en el original C++)
                byte[] asciiBytes = Encoding.ASCII.GetBytes(dllPath + "\0");
                uint size = (uint)asciiBytes.Length;

                allocMemAddress = VirtualAllocEx(hProcess, IntPtr.Zero, size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                if (allocMemAddress == IntPtr.Zero)
                    return InjectionResult.ERROR_ALLOC;

                if (!WriteProcessMemory(hProcess, allocMemAddress, asciiBytes, size, out _))
                    return InjectionResult.ERROR_WRITE;

                IntPtr hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0, IntPtr.Zero);
                if (hThread == IntPtr.Zero)
                    return InjectionResult.ERROR_CREATE_THREAD;

                CloseHandle(hThread);
                return InjectionResult.INJECT_OK;
            }
            catch
            {
                return InjectionResult.ERROR_UNKNOWN;
            }
            finally
            {
                if (hProcess != IntPtr.Zero)
                    CloseHandle(hProcess);
            }
        }

        #endregion
    }
}