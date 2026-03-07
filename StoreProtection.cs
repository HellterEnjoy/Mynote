using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;

namespace Mynote.Services;

internal interface IStoreProtector
{
    string ProtectToBase64(string plaintext);
    string UnprotectFromBase64(string base64);
    bool IsSupported { get; }
}

internal static class StoreProtector
{
    public static IStoreProtector CreateDefault() =>
        OperatingSystem.IsWindows() ? new WindowsDpapiProtector() : new NoopProtector();

    private sealed class NoopProtector : IStoreProtector
    {
        public bool IsSupported => false;
        public string ProtectToBase64(string plaintext) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
        public string UnprotectFromBase64(string base64) => Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    [SupportedOSPlatform("windows")]
    private sealed class WindowsDpapiProtector : IStoreProtector
    {
        // Fixed entropy so users can move the project folder between paths (still same Windows user).
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Mynote.NoteStore.v1");

        public bool IsSupported => true;

        public string ProtectToBase64(string plaintext)
        {
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public string UnprotectFromBase64(string base64)
        {
            var protectedBytes = Convert.FromBase64String(base64);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
