using System;
using System.Diagnostics;

namespace MinecraftLauncher.Helpers
{
    /// <summary>
    /// Считает скорость передачи по накопленным счётчикам байт.
    ///
    /// Раньше строка прогресса при загрузке компонентов показывала
    /// «12.4 / 40.0 МБ (31%) • » — хвост всегда был пустым, потому что
    /// событие прогресса отдавало скорость пустой строкой. UpdateService
    /// скорость считал, оверлей запуска — нет.
    ///
    /// Считает по разнице счётчиков между отчётами, а не по размеру
    /// чанка: чанки приходят разного размера, и мгновенная скорость
    /// скачет от нуля до десятков мегабайт. Значение сглаживается, иначе
    /// надпись мигает.
    /// </summary>
    public sealed class TransferRateTracker
    {
        /// <summary>Минимальный интервал между замерами, секунды.</summary>
        private const double MinSampleSeconds = 0.35;

        private readonly object _gate = new();
        private long _lastBytes;
        private long _lastTimestamp;
        private double _bytesPerSecond;

        public TransferRateTracker()
        {
            _lastTimestamp = Stopwatch.GetTimestamp();
        }

        /// <summary>Текущая скорость в байтах в секунду.</summary>
        public double BytesPerSecond
        {
            get { lock (_gate) { return _bytesPerSecond; } }
        }

        /// <summary>
        /// Подаёт очередное значение накопленного счётчика и возвращает
        /// скорость в байтах в секунду. Первые вызовы возвращают 0, пока не
        /// наберётся интервал для замера.
        /// </summary>
        public double Feed(long currentBytes)
        {
            lock (_gate)
            {
                long now = Stopwatch.GetTimestamp();
                double seconds = (now - _lastTimestamp) / (double)Stopwatch.Frequency;

                if (seconds < MinSampleSeconds) return _bytesPerSecond;

                long delta = currentBytes - _lastBytes;

                // Счётчик мог уменьшиться (новый этап загрузки) — тогда
                // замер бессмысленен, но время надо сбросить, иначе
                // следующий sample посчитает разницу за две операции.
                if (delta >= 0)
                {
                    double instant = delta / seconds;
                    _bytesPerSecond = _bytesPerSecond <= 0
                        ? instant
                        : _bytesPerSecond * 0.7 + instant * 0.3;
                }

                _lastBytes = currentBytes;
                _lastTimestamp = now;

                return _bytesPerSecond;
            }
        }

        /// <summary>Сбрасывает счётчик перед новой операцией загрузки.</summary>
        public void Reset(long currentBytes = 0)
        {
            lock (_gate)
            {
                _lastBytes = currentBytes;
                _lastTimestamp = Stopwatch.GetTimestamp();
                _bytesPerSecond = 0;
            }
        }
    }
}