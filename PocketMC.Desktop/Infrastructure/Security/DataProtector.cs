using System;
using System.Security.Cryptography;
using System.Text;

namespace PocketMC.Desktop.Infrastructure.Security
{
    public static class DataProtector
    {
        private const DataProtectionScope Scope = DataProtectionScope.CurrentUser;
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PocketMC-LocalSettings");

        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipherBytes = ProtectedData.Protect(plainBytes, Entropy, Scope);
            return Convert.ToBase64String(cipherBytes);
        }

        public static string Unprotect(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;
            try
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, Entropy, Scope);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                // Legacy plaintext fallback: if it cannot be decoded as Base64 or decrypted via DPAPI,
                // assume it was saved in plaintext prior to this security feature.
                // It will be re-encrypted automatically on next save.
                return cipherText;
            }
        }
    }
}
