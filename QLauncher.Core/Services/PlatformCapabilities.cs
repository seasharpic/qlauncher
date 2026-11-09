using System;

namespace MinecraftLauncher.Services
{
    /// <summary>
    /// Единая точка регистрации платформенных возможностей (аналог
    /// SecretProtection для секретов). Хост-приложение (на Windows — WPF-сборка)
    /// заполняет делегаты при старте, ядро вызоваает их без знания платформы.
    /// Всё опционально: без регистрации фича просто не выполняется — так
    /// ядро остаётся пригодным для Linux/macOS, где Registry/user32 нет.
    /// </summary>
    public static class PlatformCapabilities
    {
        /// <summary>
        /// Подготовка окружения перед запуском Java: выбор дискретного GPU
        /// (на Windows — запись UserGpuPreferences в Registry) и переменные
        /// окружения процесса.
        /// </summary>
        public static Action<string, string, System.Diagnostics.ProcessStartInfo>? PrepareLaunchEnvironment { get; set; }

        /// <summary>Список видеокарт системы (аппаратный перебор — платформенный).</summary>
        public static Func<System.Collections.Generic.List<Models.GpuInfo>?>? EnumerateGpus { get; set; }
    }
}
