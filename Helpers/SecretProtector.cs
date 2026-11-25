using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using MinecraftLauncher.Services;

namespace MinecraftLauncher.Helpers
{
    /// <summary>
    /// Кроссплатформенное шифрование секретов при сохранении на диск:
    /// на Windows используется DPAPI (CryptProtectData/CryptUnprotectData),
    /// на Linux/macOS — AES-256-GCM с локальным ключом пользователя.
    /// </summary>
    public sealed class SecretProtector : ISecretProtector
    {
        public static SecretProtector Instance { get; } = new SecretProtector();

        private SecretProtector() { }

        private readonly byte[] _entropy = Encoding.UTF8.GetBytes("QLauncher.v2.settings");
        private const string DpapiPrefix = "dpapi:";
        private const string AesPrefix = "aesgcm:";

        public string? Protect(string? plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) return plaintext;

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    byte[] data = Encoding.UTF8.GetBytes(plaintext);
                    byte[] encrypted = DpapiProtect(data, _entropy);
                    return DpapiPrefix + Convert.ToBase64String(encrypted);
                }
                catch
                {
                    return null;
                }
            }

            try
            {
                byte[] key = GetOrCreateLocalKey();
                byte[] nonce = RandomNumberGenerator.GetBytes(12);
                byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
                byte[] cipherBytes = new byte[plainBytes.Length];
                byte[] tag = new byte[16];

                using var aes = new AesGcm(key, 16);
                aes.Encrypt(nonce, plainBytes, cipherBytes, tag, _entropy);

                byte[] combined = new byte[nonce.Length + tag.Length + cipherBytes.Length];
                Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
                Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
                Buffer.BlockCopy(cipherBytes, 0, combined, nonce.Length + tag.Length, cipherBytes.Length);

                return AesPrefix + Convert.ToBase64String(combined);
            }
            catch
            {
                return null;
            }
        }

        public string? Unprotect(string? stored)
        {
            if (string.IsNullOrEmpty(stored)) return stored;

            if (stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            {
                if (!OperatingSystem.IsWindows()) return "";
                try
                {
                    byte[] encrypted = Convert.FromBase64String(stored[DpapiPrefix.Length..]);
                    byte[] decrypted = DpapiUnprotect(encrypted, _entropy);
                    return Encoding.UTF8.GetString(decrypted);
                }
                catch
                {
                    return "";
                }
            }

            if (stored.StartsWith(AesPrefix, StringComparison.Ordinal))
            {
                try
                {
                    byte[] combined = Convert.FromBase64String(stored[AesPrefix.Length..]);
                    if (combined.Length < 28) return "";

                    byte[] key = GetOrCreateLocalKey();
                    byte[] nonce = combined[..12];
                    byte[] tag = combined[12..28];
                    byte[] cipherBytes = combined[28..];
                    byte[] plainBytes = new byte[cipherBytes.Length];

                    using var aes = new AesGcm(key, 16);
                    aes.Decrypt(nonce, cipherBytes, tag, plainBytes, _entropy);
                    return Encoding.UTF8.GetString(plainBytes);
                }
                catch
                {
                    return "";
                }
            }

            return stored;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(
            ref DATA_BLOB pDataIn, string? szDataDescr, ref DATA_BLOB pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(
            ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, ref DATA_BLOB pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        private static byte[] DpapiProtect(byte[] data, byte[] entropy)
        {
            var inBlob = AllocBlob(data);
            var entBlob = AllocBlob(entropy);
            DATA_BLOB outBlob = default;
            try
            {
                if (!CryptProtectData(ref inBlob, null, ref entBlob, IntPtr.Zero, IntPtr.Zero, 0, out outBlob))
                    throw new CryptographicException(Marshal.GetLastWin32Error());
                byte[] res = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, res, 0, res.Length);
                return res;
            }
            finally
            {
                FreeBlob(inBlob);
                FreeBlob(entBlob);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }

        private static byte[] DpapiUnprotect(byte[] data, byte[] entropy)
        {
            var inBlob = AllocBlob(data);
            var entBlob = AllocBlob(entropy);
            DATA_BLOB outBlob = default;
            try
            {
                if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entBlob, IntPtr.Zero, IntPtr.Zero, 0, out outBlob))
                    throw new CryptographicException(Marshal.GetLastWin32Error());
                byte[] res = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, res, 0, res.Length);
                return res;
            }
            finally
            {
                FreeBlob(inBlob);
                FreeBlob(entBlob);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }

        private static DATA_BLOB AllocBlob(byte[] data)
        {
            IntPtr ptr = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, ptr, data.Length);
            return new DATA_BLOB { cbData = data.Length, pbData = ptr };
        }

        private static void FreeBlob(DATA_BLOB blob)
        {
            if (blob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(blob.pbData);
        }

        private static byte[] GetOrCreateLocalKey()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "QLauncher");
            Directory.CreateDirectory(dir);
            string keyPath = Path.Combine(dir, ".secret.key");

            if (File.Exists(keyPath))
            {
                byte[] existing = File.ReadAllBytes(keyPath);
                if (existing.Length == 32) return existing;
            }

            byte[] key = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(keyPath, key);
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch { }
            }
            return key;
        }
    }
}
