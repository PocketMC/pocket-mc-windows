using System;
using System.Security.Cryptography;
using System.Text;

namespace PocketMC.Infrastructure.Backups.OAuth;

public static class PkceHelper
{
    public static (string codeVerifier, string codeChallenge) Generate()
    {
        var buffer = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(buffer);
        }

        string codeVerifier = Base64UrlEncode(buffer);

        using (var sha256 = SHA256.Create())
        {
            byte[] challengeBytes = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
            string codeChallenge = Base64UrlEncode(challengeBytes);
            return (codeVerifier, codeChallenge);
        }
    }

    private static string Base64UrlEncode(byte[] input)
    {
        string output = Convert.ToBase64String(input)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
        return output;
    }
}
