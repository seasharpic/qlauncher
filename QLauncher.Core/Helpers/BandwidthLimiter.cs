using System;
using System.Threading;
using System.Threading.Tasks;

namespace MinecraftLauncher.Helpers
{
    /// <summary>
    /// Ограничитель скорости загрузки.
    ///
    /// Настройка «Ограничение скорости загрузки» работала только для
    /// самообновления лаунчера: её значение передавалось в UpdateService и
    /// больше никуда. Загрузка ресурсов, библиотек и Java шла на полной
    /// скорости и могла забить канал, из-за чего страдали другие устройства
    /// в сети.
    ///
    /// Учёт общий на весь процесс: параллельных загрузок много (до 24
    /// ассетов), поэтому лимит считается суммарно, а не на каждый поток.
    /// </summary>
    public static class BandwidthLimiter
    {
        private const int BitsPerByte = 8;
        private const int BitsPerMegabyte = 1024 * 1024 * BitsPerByte;

        private static long _bytesPerSecond;
        private static readonly SemaphoreSlim _gate = new(1, 1);

        private static long _windowStart;
        private static long _windowBytes;

        /// <summary>Текущий лимит в байтах в секунду. Ноль — без ограничения.</summary>
        public static long BytesPerSecond => Interlocked.Read(ref _bytesPerSecond);

        /// <summary>
        /// Задаёт лимит. При значении 0 или меньше ограничение снимается.
        /// </summary>
        public static void Configure(int limitMbps)
        {
            long bps = limitMbps > 0
                ? (long)limitMbps * BitsPerMegabyte / BitsPerByte
                : 0;

            Interlocked.Exchange(ref _bytesPerSecond, bps);

            // Сбрасываем окно, иначе после смены лимит применится только
            // по истечении текущей секунды.
            Interlocked.Exchange(ref _windowStart, Environment.TickCount64);
            Interlocked.Exchange(ref _windowBytes, 0);
        }

        /// <summary>
        /// Просит пропустить указанное количество байт, при необходимости
        /// задерживая вызывающий поток.
        /// </summary>
        public static async Task ThrottleAsync(int bytes, CancellationToken cancellationToken = default)
        {
            long limit = Interlocked.Read(ref _bytesPerSecond);
            if (limit <= 0 || bytes <= 0) return;

            int delayMs = 0;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                long now = Environment.TickCount64;

                // Окно усреднения — 250 мс: так лимит отзывчив к изменению
                // настройки и не дёргает задержками на каждом чанке.
                const long WindowMs = 250;
                if (now - Interlocked.Read(ref _windowStart) >= WindowMs)
                {
                    Interlocked.Exchange(ref _windowStart, now);
                    Interlocked.Exchange(ref _windowBytes, 0);
                }

                long spent = Interlocked.Add(ref _windowBytes, bytes) - bytes;
                long allowedPerWindow = limit * WindowMs / 1000;

                if (spent + bytes > allowedPerWindow)
                {
                    long excess = spent + bytes - allowedPerWindow;
                    Interlocked.Exchange(ref _windowBytes, allowedPerWindow);
                    delayMs = (int)Math.Min(excess * 1000.0 / limit, 2000);
                }
            }
            finally
            {
                _gate.Release();
            }

            // Задержка выполняется вне блокировки: держать семафор во время
            // сна означало бы полную сериализацию параллельных загрузок.
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}