using System;
using System.Security.Cryptography;
using System.Text;

namespace EmuDOS.Metadata;

/// <summary>
/// Optional AES-256-GCM encryption for cloud-synced save data. A passphrase is stretched with PBKDF2
/// into a key; each blob is encrypted with a random nonce and a self-identifying header, so encrypted
/// and plain blobs can coexist in a repo and a wrong passphrase fails cleanly (returns null) rather
/// than producing garbage. Compression happens before encryption (encrypted bytes don't compress).
/// </summary>
public static class CloudCrypto
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("EDENC1\n"); // 7-byte marker
    private static readonly byte[] Salt = "EmuDOS-cloud-save-v1"u8.ToArray();
    private const int NonceLen = 12, TagLen = 16;

    public static byte[] DeriveKey(string passphrase) =>
        Rfc2898DeriveBytes.Pbkdf2(passphrase, Salt, 200_000, HashAlgorithmName.SHA256, 32);

    public static bool IsEncrypted(byte[] data)
    {
        if (data.Length < Magic.Length)
            return false;
        for (int i = 0; i < Magic.Length; i++)
            if (data[i] != Magic[i])
                return false;
        return true;
    }

    public static byte[] Encrypt(byte[] plain, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceLen);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagLen];
        using var aes = new AesGcm(key, TagLen);
        aes.Encrypt(nonce, plain, cipher, tag);

        var output = new byte[Magic.Length + NonceLen + TagLen + cipher.Length];
        int o = 0;
        Buffer.BlockCopy(Magic, 0, output, o, Magic.Length); o += Magic.Length;
        Buffer.BlockCopy(nonce, 0, output, o, NonceLen); o += NonceLen;
        Buffer.BlockCopy(tag, 0, output, o, TagLen); o += TagLen;
        Buffer.BlockCopy(cipher, 0, output, o, cipher.Length);
        return output;
    }

    /// <summary>Decrypt if the blob is encrypted; pass plain blobs through unchanged. Returns null if
    /// it's encrypted but the key is wrong / data is corrupt.</summary>
    public static byte[]? TryDecrypt(byte[] data, byte[] key)
    {
        if (!IsEncrypted(data))
            return data;
        try
        {
            int o = Magic.Length;
            var nonce = data.AsSpan(o, NonceLen); o += NonceLen;
            var tag = data.AsSpan(o, TagLen); o += TagLen;
            var cipher = data.AsSpan(o);
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, TagLen);
            aes.Decrypt(nonce, cipher, tag, plain);
            return plain;
        }
        catch
        {
            return null;
        }
    }
}
