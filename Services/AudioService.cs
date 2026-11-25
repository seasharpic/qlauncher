using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MinecraftLauncher.Services
{
    public interface IAudioService
    {
        void PlayClickSound(bool enabled);
        void PlayLaunchSound(bool enabled);
    }

    public class AudioService : IAudioService
    {
        private static byte[]? _cachedClickWav;
        private static byte[]? _cachedLaunchWav;

        public static AudioService Instance { get; } = new AudioService();

        public void PlayClickSound(bool enabled)
        {
            if (!enabled) return;
            _cachedClickWav ??= GenerateClickSoundWav();
            PlayWavAsync(_cachedClickWav, "click.wav");
        }

        public void PlayLaunchSound(bool enabled)
        {
            if (!enabled) return;
            _cachedLaunchWav ??= GenerateLaunchSoundWav();
            PlayWavAsync(_cachedLaunchWav, "launch.wav");
        }

        private static void PlayWavAsync(byte[] wavBytes, string fileName)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        PlaySound(wavBytes, IntPtr.Zero, 0x0004 | 0x0001); // SND_MEMORY | SND_ASYNC
                        return;
                    }

                    string tempPath = Path.Combine(Path.GetTempPath(), $"qlauncher_{fileName}");
                    if (!File.Exists(tempPath))
                    {
                        File.WriteAllBytes(tempPath, wavBytes);
                    }

                    if (OperatingSystem.IsMacOS())
                    {
                        TrySpawn("afplay", tempPath);
                    }
                    else
                    {
                        if (!TrySpawn("pw-play", tempPath) && !TrySpawn("paplay", tempPath))
                        {
                            TrySpawn("aplay", $"-q \"{tempPath}\"");
                        }
                    }
                }
                catch { }
            });
        }

        private static bool TrySpawn(string cmd, string args)
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = cmd,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                return proc != null;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern bool PlaySound(byte[] pszSound, IntPtr hmod, uint fdwSound);

        private static byte[] GenerateClickSoundWav()
        {
            const int sampleRate = 44100;
            const double duration = 0.035;
            int numSamples = (int)(sampleRate * duration);
            short[] samples = new short[numSamples];

            for (int i = 0; i < numSamples; i++)
            {
                double t = (double)i / sampleRate;
                double envelope = Math.Exp(-t * 150);
                double freq = 1400 - (t / duration) * 700;
                double wave = Math.Sin(2 * Math.PI * freq * t);
                samples[i] = (short)(wave * envelope * 14000);
            }

            return CreateWavHeaderAndData(samples, sampleRate);
        }

        private static byte[] GenerateLaunchSoundWav()
        {
            const int sampleRate = 44100;
            const double duration = 0.28;
            int numSamples = (int)(sampleRate * duration);
            short[] samples = new short[numSamples];

            double[] freqs = { 523.25, 659.25, 783.99 };

            for (int i = 0; i < numSamples; i++)
            {
                double t = (double)i / sampleRate;
                double envelope = Math.Sin(Math.PI * (t / duration)) * Math.Exp(-t * 6);

                double wave = 0;
                for (int f = 0; f < freqs.Length; f++)
                {
                    double delay = f * 0.04;
                    if (t >= delay)
                    {
                        wave += Math.Sin(2 * Math.PI * freqs[f] * (t - delay));
                    }
                }

                samples[i] = (short)(wave * envelope * 6000);
            }

            return CreateWavHeaderAndData(samples, sampleRate);
        }

        private static byte[] CreateWavHeaderAndData(short[] samples, int sampleRate)
        {
            int dataSize = samples.Length * 2;
            int totalSize = 36 + dataSize;

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(new char[] { 'R', 'I', 'F', 'F' });
            writer.Write(totalSize);
            writer.Write(new char[] { 'W', 'A', 'V', 'E' });

            writer.Write(new char[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);

            writer.Write(new char[] { 'd', 'a', 't', 'a' });
            writer.Write(dataSize);

            for (int i = 0; i < samples.Length; i++)
            {
                writer.Write(samples[i]);
            }

            return ms.ToArray();
        }
    }
}
