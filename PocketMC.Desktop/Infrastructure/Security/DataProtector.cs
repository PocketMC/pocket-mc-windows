using System;
using System.Security.Cryptography;
using System.Text;

namespace PocketMC.Desktop.Infrastructure.Security
{
    public static class DataProtector
    {
        private const string ProtectedPrefix = "dpapi:v1:";
        private const DataProtectionScope Scope = DataProtectionScope.CurrentUser;
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PocketMC-LocalSettings");

        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;

            byte[]? plainBytes = null;
            byte[]? cipherBytes = null;
            try
            {
                plainBytes = Encoding.UTF8.GetBytes(plainText);
                cipherBytes = ProtectedData.Protect(plainBytes, Entropy, Scope);
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
            string payload = hasPrefix ? cipherText[ProtectedPrefix.Length..] : cipherText;
            byte[]? cipherBytes = null;
            byte[]? plainBytes = null;

            try
            {
                cipherBytes = Convert.FromBase64String(payload);
            }
            catch (FormatException) when (!hasPrefix)
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
                plainBytes = ProtectedData.Unprotect(cipherBytes, Entropy, Scope);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (CryptographicException) when (!hasPrefix)
            {
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
    }
}
