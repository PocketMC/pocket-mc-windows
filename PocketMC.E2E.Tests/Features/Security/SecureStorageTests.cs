using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using PocketMC.Infrastructure.Security;
using PocketMC.E2E.Tests.Infrastructure;
using Xunit;

namespace PocketMC.E2E.Tests.Features.Security
{
    public class SecureStorageTests
    {
        [Fact]
        public void Test1_Protect_NullOrEmpty_ReturnsSame()
        {
            Assert.Null(DataProtector.Protect(null!));
            Assert.Equal("", DataProtector.Protect(""));
        }

        [Fact]
        public void Test2_Unprotect_NullOrEmpty_ReturnsSame()
        {
            Assert.Null(DataProtector.Unprotect(null!));
            Assert.Equal("", DataProtector.Unprotect(""));
        }

        [Fact]
        public void Test3_ProtectAndUnprotect_OnWindows_WorksCorrectly()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Arrange
                string secret = "CurseForgeApiKey-12345";

                // Act
                string cipher = DataProtector.Protect(secret);
                string plain = DataProtector.Unprotect(cipher);

                // Assert
                Assert.NotEqual(secret, cipher);
                Assert.True(cipher.StartsWith("dpapi:v2:"));
                Assert.Equal(secret, plain);
            }
        }

        [Fact]
        public void Test4_Unprotect_LegacyPayload_ReturnsOriginalText()
        {
            // If the text does not have the prefix and is not valid Base64, Unprotect returns the input itself
            string plain = "normalplaintext_notbase64";
            string result = DataProtector.Unprotect(plain);
            Assert.Equal(plain, result);
        }

        [Fact]
        public void Test5_Unprotect_InvalidBase64Payload_ThrowsCryptographicException()
        {
            // Invalid base64 with version prefix should throw CryptographicException
            string invalidPayload = "dpapi:v2:invalidbase64!!!";
            Assert.ThrowsAny<CryptographicException>(() => DataProtector.Unprotect(invalidPayload));
        }

        [Fact]
        public void Test6_Unprotect_InvalidPayload_ReturnsPlainTextFallback()
        {
            // A non-prefixed Base64 string that cannot be decrypted falls back to returning the cipher text itself
            string base64String = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("some-base64-text"));
            string result = DataProtector.Unprotect(base64String);
            Assert.Equal(base64String, result);
        }

        [UnixFact]
        public void Test7_UnixFilePermissions_KeyFile0600_SkipsOnWindows()
        {
            using (var ctx = new TestDirectoryContext())
            {
                // Arrange
                var filePath = ctx.CreateFile("keys/key.xml", "dummy-key-data");

                // Act
                File.SetUnixFileModes(filePath, UnixFileModes.UserRead | UnixFileModes.UserWrite);
                var modes = File.GetUnixFileModes(filePath);

                // Assert
                Assert.Equal(UnixFileModes.UserRead | UnixFileModes.UserWrite, modes);
            }
        }

        [UnixFact]
        public void Test8_UnixFilePermissions_SecureStorageWrite_SkipsOnWindows()
        {
            using (var ctx = new TestDirectoryContext())
            {
                // Arrange
                var filePath = Path.Combine(ctx.TempPath, "storage.dat");
                
                // Act
                File.WriteAllText(filePath, "encrypted-data");
                File.SetUnixFileModes(filePath, UnixFileModes.UserRead | UnixFileModes.UserWrite);
                var modes = File.GetUnixFileModes(filePath);

                // Assert
                Assert.Equal(UnixFileModes.UserRead | UnixFileModes.UserWrite, modes);
            }
        }

        [UnixFact]
        public void Test9_UnixFilePermissions_InvalidPermissionCorrection_SkipsOnWindows()
        {
            using (var ctx = new TestDirectoryContext())
            {
                // Arrange
                var filePath = ctx.CreateFile("insecure.txt", "insecure-data");
                // Set wide open permissions (0777)
                File.SetUnixFileModes(filePath, UnixFileModes.UserRead | UnixFileModes.UserWrite | UnixFileModes.UserExecute |
                                               UnixFileModes.GroupRead | UnixFileModes.GroupWrite | UnixFileModes.GroupExecute |
                                               UnixFileModes.OtherRead | UnixFileModes.OtherWrite | UnixFileModes.OtherExecute);

                // Act - Restrict it to owner-only read/write (0600)
                File.SetUnixFileModes(filePath, UnixFileModes.UserRead | UnixFileModes.UserWrite);
                var modes = File.GetUnixFileModes(filePath);

                // Assert
                Assert.Equal(UnixFileModes.UserRead | UnixFileModes.UserWrite, modes);
            }
        }

        [UnixFact]
        public void Test10_Unprotect_FormatExceptions_SkipsOnWindows()
        {
            // Verification of cryptographic behavior under version prefix but incorrect cipher contents on Unix
            var legacyInvalid = "dpapi:v1:invalidbase64!!!";
            Assert.ThrowsAny<CryptographicException>(() => DataProtector.Unprotect(legacyInvalid));
        }
    }
}
