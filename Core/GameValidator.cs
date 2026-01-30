using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GTAVInjector.Core
{
    /// <summary>
    /// Validador de configuración del juego y anti-cheat
    /// </summary>
    public static class GameValidator
    {
        // Nombres de procesos de anti-cheat de BattlEye
        private static readonly string[] BATTLEYE_PROCESSES = new[]
        {
            "GTA5_Enhanced_BE.exe",
            "GTA5_BE.exe",
            "BEService.exe",
            "BEService_x64.exe"
        };

        /// <summary>
        /// Verifica si BattlEye (anti-cheat) está activo
        /// </summary>
        public static bool IsBattlEyeActive()
        {
            try
            {
                var processes = Process.GetProcesses();
                
                foreach (var process in processes)
                {
                    try
                    {
                        string processName = process.ProcessName + ".exe";
                        
                        if (BATTLEYE_PROCESSES.Any(be => 
                            processName.Equals(be, StringComparison.OrdinalIgnoreCase)))
                        {
                            System.Diagnostics.Debug.WriteLine($"[VALIDATOR] ⚠️ BattlEye detectado: {processName}");
                            return true;
                        }
                    }
                    catch { }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VALIDATOR] Error verificando BattlEye: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Verifica si FiveM Server Link (FSL) está instalado
        /// Busca WINMM.dll en la carpeta del juego
        /// </summary>
        public static bool IsFSLInstalled()
        {
            try
            {
                string gamePath = GetGTAVPath();
                
                if (string.IsNullOrEmpty(gamePath))
                    return false;

                string winmmPath = Path.Combine(gamePath, "WINMM.dll");
                bool exists = File.Exists(winmmPath);
                
                if (exists)
                {
                    System.Diagnostics.Debug.WriteLine($"[VALIDATOR] ℹ️ FSL (WINMM.dll) DETECTADO");
                    System.Diagnostics.Debug.WriteLine($"[VALIDATOR] Ruta: {winmmPath}");
                }
                
                return exists;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VALIDATOR] Error verificando FSL: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Obtiene la ruta de instalación de GTA V
        /// </summary>
        public static string GetGTAVPath()
        {
            try
            {
                // Intentar obtener desde el proceso en ejecución
                var gtaProcesses = Process.GetProcessesByName("GTA5");
                if (gtaProcesses.Length > 0)
                {
                    try
                    {
                        string processPath = gtaProcesses[0].MainModule?.FileName;
                        if (!string.IsNullOrEmpty(processPath))
                        {
                            string path = Path.GetDirectoryName(processPath);
                            System.Diagnostics.Debug.WriteLine($"[VALIDATOR] GTA V path (proceso): {path}");
                            return path;
                        }
                    }
                    catch { }
                }

                // Buscar en ubicaciones comunes
                string[] commonPaths = new[]
                {
                    @"C:\Program Files\Rockstar Games\Grand Theft Auto V",
                    @"C:\Program Files (x86)\Rockstar Games\Grand Theft Auto V",
                    @"D:\Program Files\Rockstar Games\Grand Theft Auto V",
                    @"E:\Program Files\Rockstar Games\Grand Theft Auto V",
                    @"C:\Program Files\Epic Games\GTAV",
                    @"D:\Program Files\Epic Games\GTAV",
                    @"D:\SteamLibrary\steamapps\common\Grand Theft Auto V",
                    @"C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V",
                    @"C:\Program Files\Steam\steamapps\common\Grand Theft Auto V"
                };

                foreach (var path in commonPaths)
                {
                    if (Directory.Exists(path))
                    {
                        // Verificar que sea realmente la carpeta de GTA V
                        if (File.Exists(Path.Combine(path, "GTA5.exe")))
                        {
                            System.Diagnostics.Debug.WriteLine($"[VALIDATOR] GTA V encontrado: {path}");
                            return path;
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine("[VALIDATOR] ❌ No se encontró la ruta de GTA V");
                return string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VALIDATOR] Error obteniendo ruta: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Obtiene el delay recomendado basado en la configuración del juego
        /// </summary>
        public static int GetRecommendedDelay()
        {
            // Si FSL está instalado, requiere más tiempo de espera
            if (IsFSLInstalled())
            {
                System.Diagnostics.Debug.WriteLine("[VALIDATOR] FSL detectado - Delay recomendado: 15 segundos");
                return 15; // FSL necesita más tiempo para cargar
            }

            // Delay estándar
            return 5;
        }

        /// <summary>
        /// Verifica todas las condiciones y retorna un resumen
        /// </summary>
        public static ValidationResult ValidateGameState()
        {
            return new ValidationResult
            {
                IsBattlEyeActive = IsBattlEyeActive(),
                IsFSLInstalled = IsFSLInstalled(),
                RecommendedDelay = GetRecommendedDelay(),
                GamePath = GetGTAVPath()
            };
        }
    }

    /// <summary>
    /// Resultado de validación del juego
    /// </summary>
    public class ValidationResult
    {
        public bool IsBattlEyeActive { get; set; }
        public bool IsFSLInstalled { get; set; }
        public int RecommendedDelay { get; set; }
        public string GamePath { get; set; }

        /// <summary>
        /// Indica si es seguro inyectar DLLs
        /// </summary>
        public bool CanInject => !IsBattlEyeActive;

        /// <summary>
        /// Obtiene mensaje de advertencia si no se puede inyectar
        /// </summary>
        public string GetWarningMessage(string language = "es")
        {
            if (IsBattlEyeActive)
            {
                return language == "es"
                    ? "⚠️ BattlEye detectado. La inyección está deshabilitada por seguridad."
                    : "⚠️ BattlEye detected. Injection is disabled for safety.";
            }

            if (IsFSLInstalled)
            {
                return language == "es"
                    ? "ℹ️ FSL detectado. Usando delay extendido (15s)."
                    : "ℹ️ FSL detected. Using extended delay (15s).";
            }

            return string.Empty;
        }
    }
}
