namespace MinecraftLauncher.Services
{
    /// <summary>
    /// Идентификация лаунчера для ядра: версия приходит в отчёты
    /// диагностики и сервис проверки обновлений. Единый источник правды,
    /// чтобы строка не разъезжалась между сборками UI и ядра.
    /// </summary>
    public static class LauncherIdentity
    {
        public const string CurrentVersion = "v2.0.0";
    }
}
