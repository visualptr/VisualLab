using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using VisualLab.Serialization;

namespace VisualLab.Security;

/// <summary>Encrypts and authenticates dashboard frames.</summary>
public sealed class Cipher
{
    private const int KeySize = 32;
    private const int IvSize = 16;
    private const int MacSize = 32;

    private readonly byte[] _encryptionKey;
    private readonly byte[] _macKey;

    /// <summary>Initializes a new instance of the <see cref="Cipher"/> class.</summary>
    /// <param name="key">The 32-byte session key.</param>
    public Cipher(byte[] key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException("Expected a 32-byte key.");

        _encryptionKey = Hmac(key, Encoding.UTF8.GetBytes("visuallab/enc"));
        _macKey = Hmac(key, Encoding.UTF8.GetBytes("visuallab/mac"));
    }

    /// <summary>Generates a session key.</summary>
    /// <returns>A random 32-byte key.</returns>
    public static byte[] RandomKey()
    {
        byte[] key = new byte[KeySize];
        using RandomNumberGenerator random = RandomNumberGenerator.Create();
        random.GetBytes(key);
        return key;
    }

    /// <summary>Encodes bytes as URL-safe base64.</summary>
    /// <param name="data">The bytes to encode.</param>
    /// <returns>The encoded text without padding.</returns>
    public static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Reads the authentication tag of a frame.</summary>
    /// <param name="frame">The encrypted frame.</param>
    /// <returns>The tag as URL-safe base64.</returns>
    public static string Fingerprint(string frame)
    {
        byte[] bytes = Decode(frame);
        return Base64Url(bytes.Skip(bytes.Length - MacSize).ToArray());
    }

    /// <summary>Encrypts and authenticates a JSON object.</summary>
    /// <param name="value">The value to use.</param>
    /// <returns>The encoded frame.</returns>
    public string Seal(JsonObject value)
    {
        using Aes aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.GenerateIV();

        byte[] plain = Encoding.UTF8.GetBytes(Json.Write(value));
        using ICryptoTransform transform = aes.CreateEncryptor();
        byte[] body = aes.IV.Concat(transform.TransformFinalBlock(plain, 0, plain.Length)).ToArray();
        return Base64Url(body.Concat(Hmac(_macKey, body)).ToArray());
    }

    /// <summary>Authenticates and decrypts a frame.</summary>
    /// <param name="frame">The encrypted frame.</param>
    /// <returns>The decrypted JSON object.</returns>
    public JsonObject Open(string frame)
    {
        byte[] bytes = Decode(frame);
        if (bytes.Length < IvSize + IvSize + MacSize || (bytes.Length - IvSize - MacSize) % IvSize != 0)
            throw new CryptographicException("Invalid frame.");

        byte[] body = bytes.Take(bytes.Length - MacSize).ToArray();
        byte[] mac = Hmac(_macKey, body);
        int difference = 0;
        for (int i = 0; i < MacSize; i++)
            difference |= mac[i] ^ bytes[body.Length + i];

        if (difference != 0)
            throw new CryptographicException("Bad MAC.");

        using Aes aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.IV = body.Take(IvSize).ToArray();
        using ICryptoTransform transform = aes.CreateDecryptor();
        return Json.ParseObject(Encoding.UTF8.GetString(transform.TransformFinalBlock(body, IvSize, body.Length - IvSize)));
    }

    private static byte[] Decode(string data)
    {
        string base64 = data.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
    }

    private static byte[] Hmac(byte[] key, byte[] data)
    {
        using HMACSHA256 hmac = new (key);
        return hmac.ComputeHash(data);
    }
}
