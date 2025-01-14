using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Conductor.Shared.Config;
using Conductor.Shared.Types;

namespace Conductor.Shared;

public static class Encryption
{
    public static string Sha256(string data)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return GetStringFromByteArray(bytes);
    }

    public static string SymmetricEncryptAES256(string input, string authSecret)
    {
        byte[] keyBytes = GetByteArrayFromHash(authSecret);
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);

        using Aes aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.GenerateIV();
        aes.Key = keyBytes;

        using MemoryStream mem = new();
        using CryptoStream crypto = new(mem, aes.CreateEncryptor(), CryptoStreamMode.Write);
        crypto.Write(inputBytes, 0, inputBytes.Length);
        crypto.FlushFinalBlock();

        byte[] result = mem.ToArray();
        string stringResult = Convert.ToBase64String(aes.IV.Concat(result).ToArray());

        return stringResult;
    }

    public static string SymmetricDecryptAES256(string input, string authSecret)
    {
        byte[] fullCipher = Convert.FromBase64String(input);
        byte[] iv = fullCipher.Take(16).ToArray();
        byte[] cipherBytes = fullCipher.Skip(16).ToArray();
        byte[] keyBytes = GetByteArrayFromHash(authSecret);

        using Aes aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Key = keyBytes;
        aes.IV = iv;

        using MemoryStream mem = new(cipherBytes);
        using CryptoStream crypto = new(mem, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using StreamReader stream = new(crypto);

        string result = stream.ReadToEnd();

        return result;
    }

    private static byte[] GetByteArrayFromHash(string input)
    {
        string hashString = Sha256(input);

        byte[] hashBytes = new byte[32];
        for (Int32 i = 0; i < hashBytes.Length; i++)
        {
            hashBytes[i] = Convert.ToByte(hashString.Substring(i * 2, 2), 16);
        }
        return hashBytes;
    }

    private static string GetStringFromByteArray(byte[] bytes)
    {
        StringBuilder builder = new();

        for (Int32 i = 0; i < bytes.Length; i++)
        {
            builder.Append(bytes[i].ToString("x2"));
        }
        return builder.ToString();
    }

    public static string GenerateJwt(string ip, string userName, string authSecret)
    {
        string iss = Environment.MachineName;
        string aud = ip;
        string sub = userName;

        DateTime exp = DateTime.Now.AddSeconds(Settings.SessionTime);

        string header = JsonSerializer.Serialize(new { alg = "AES256", typ = "JWT" });
        string headerBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(header));

        string payload = JsonSerializer.Serialize(new { iss, sub, aud, exp });
        string encrypted = SymmetricEncryptAES256(payload, authSecret);

        return $"{headerBase64}.{encrypted}";
    }

    public static Result ValidateJwt(string ip, string token, string authSecret)
    {
        string[] parts = token.Split('.');

        if (parts.Length != 2)
        {
            return new Error("Invalid token format.");
        }

        string headerJson = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));

        try
        {
            JsonObject header = JsonSerializer.Deserialize<JsonObject>(headerJson)!;

            if (
                !header.TryGetPropertyValue("alg", out var alg) &&
                alg?.GetValue<string>() == "AES256"
            ) return new Error("Invalid encryption method.");

            string decrypted = SymmetricDecryptAES256(parts[1], authSecret);
            JsonObject payload = JsonSerializer.Deserialize<JsonObject>(decrypted)!;

            string[] availableIssuers = Settings.Nodes.Split("|");

            if (!payload.TryGetPropertyValue("audience", out var audience)) return new Error("Invalid token.");
            if (!payload.TryGetPropertyValue("issuer", out var issuer)) return new Error("Invalid token.");
            if (!payload.TryGetPropertyValue("expiration", out var expiration)) return new Error("Invalid token.");

            if (
                ip == audience!.GetValue<string>() &&
                availableIssuers.Contains(issuer!.GetValue<string>()) &&
                DateTime.UtcNow.ToLocalTime() <= Convert.ToDateTime(expiration!.GetValue<string>())
            ) return Result.Ok();

            return new Error("Validation failed.");
        }
        catch
        {
            return new Error("Invalid token.");
        }
    }
}