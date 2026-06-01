using System.Security.Cryptography;
using System.Text;

namespace CopilotDeepSeek;

public static class SecurityHelper
{
    private static byte[] DeriveKey()
    {
        byte[] ikm = Encoding.UTF8.GetBytes($"{Environment.MachineName}:{Environment.UserName}");
        byte[] salt = "CopilotDeepSeek"u8.ToArray();
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, salt);
    }

    public static string Encrypt(string plainText)
    {
        byte[] key = DeriveKey();
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using var ms = new MemoryStream();
        using var encStream = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write);
        encStream.Write(plainBytes);
        encStream.FlushFinalBlock();

        // Prepend IV so we can decrypt later: [IV (16)] + [ciphertext]
        byte[] result = new byte[aes.IV.Length + ms.Length];
        aes.IV.CopyTo(result, 0);
        ms.ToArray().CopyTo(result, aes.IV.Length);
        return Convert.ToBase64String(result);
    }

    public static string Decrypt(string encryptedBase64)
    {
        byte[] key = DeriveKey();
        byte[] data = Convert.FromBase64String(encryptedBase64);

        byte[] iv = data[..16];
        byte[] cipher = data[16..];

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;

        using var ms = new MemoryStream(cipher);
        using var decStream = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var reader = new StreamReader(decStream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static string ReadInput()
    {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = Console.BackgroundColor;
        string? input = Console.ReadLine();
        Console.ForegroundColor = originalColor;
        return input ?? string.Empty;
    }
}