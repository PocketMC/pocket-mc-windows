using System;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace PocketMC.Infrastructure.Security
{
    public static class DataProtector
    {
        private const string LegacyProtectedPrefix = "dpapi:v1:";
        private const string ProtectedPrefix = "dpapi:v2:";
        private const string EntropySalt = "PocketMC-LocalSettings";
        private const DataProtectionScope Scope = DataProtectionScope.CurrentUser;
        private static readonly byte[] LegacyEntropy = Encoding.UTF8.GetBytes(EntropySalt);
        private static readonly Lazy<byte[]> CurrentEntropy = new(CreateCurrentUserEntropy);

        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;

            byte[]? plainBytes = null;
            byte[]? cipherBytes = null;
            try
            {
                plainBytes = Encoding.UTF8.GetBytes(plainText);
                cipherBytes = ProtectedData.Protect(plainBytes, CurrentEntropy.Value, Scope);
                return ProtectedPrefix + Convert.ToBase64String(cipherBytes);
            }
            finally
            {
                if (plainBytes != null) CryptographicOperations.ZeroMemory(plainBytes);
                if (cipherBytes != null) CryptographicOperations.ZeroMemory(cipherBytes);
            }
        }

        public static string Unprotect(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;

            bool hasPrefix = cipherText.StartsWith(ProtectedPrefix, StringComparison.Ordinal);
            bool hasLegacyPrefix = cipherText.StartsWith(LegacyProtectedPrefix, StringComparison.Ordinal);
            string payload = hasPrefix
                ? cipherText[ProtectedPrefix.Length..]
                : hasLegacyPrefix
                    ? cipherText[LegacyProtectedPrefix.Length..]
                    : cipherText;
            byte[]? cipherBytes = null;
            byte[]? plainBytes = null;

            try
            {
                cipherBytes = Convert.FromBase64String(payload);
            }
            catch (FormatException) when (!hasPrefix && !hasLegacyPrefix)
            {
                // Legacy plaintext fallback: non-Base64 settings predate DPAPI protection.
                return cipherText;
            }
            catch (FormatException ex)
            {
                throw new CryptographicException("Protected setting payload is not valid Base64.", ex);
            }

            try
            {
                byte[] entropy = hasLegacyPrefix ? LegacyEntropy : CurrentEntropy.Value;
                plainBytes = ProtectedData.Unprotect(cipherBytes, entropy, Scope);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (CryptographicException) when (!hasPrefix && !hasLegacyPrefix)
            {
                if (TryUnprotectLegacy(cipherBytes, out byte[]? legacyPlainBytes) && legacyPlainBytes != null)
                {
                    plainBytes = legacyPlainBytes;
                    return Encoding.UTF8.GetString(legacyPlainBytes);
                }

                // Legacy plaintext settings can be Base64-shaped. Only versioned payloads fail closed.
                return cipherText;
            }
            catch (CryptographicException)
            {
                throw;
            }
            finally
            {
                if (cipherBytes != null) CryptographicOperations.ZeroMemory(cipherBytes);
                if (plainBytes != null) CryptographicOperations.ZeroMemory(plainBytes);
            }
        }

        private static bool TryUnprotectLegacy(byte[] cipherBytes, out byte[]? plainBytes)
        {
            plainBytes = null;
            try
            {
                plainBytes = ProtectedData.Unprotect(cipherBytes, LegacyEntropy, Scope);
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        private static byte[] CreateCurrentUserEntropy()
        {
            string userScope;
            try
            {
                userScope = WindowsIdentity.GetCurrent().User?.Value ?? "UnknownSid";
            }
            catch (SecurityException)
            {
                userScope = $"{Environment.UserDomainName}\\{Environment.UserName}";
            }
            catch (UnauthorizedAccessException)
            {
                userScope = $"{Environment.UserDomainName}\\{Environment.UserName}";
            }

            return SHA256.HashData(Encoding.UTF8.GetBytes($"{userScope}:{EntropySalt}"));
        }
    }
}
