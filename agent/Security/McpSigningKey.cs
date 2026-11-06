using System.Security.Cryptography;

namespace ItmoBot.Agent.Security;

public sealed class McpSigningKey
{
    private readonly byte[] bytes;

    public McpSigningKey()
    {
        bytes = RandomNumberGenerator.GetBytes(32);
    }

    public McpSigningKey(string encoded)
    {
        try
        {
            bytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            throw new ArgumentException("Ключ подписи должен быть в формате Base64.");
        }

        if (bytes.Length != 32)
        {
            throw new ArgumentException("Ключ подписи должен содержать 32 байта.");
        }
    }

    public string ExportForChildProcess()
    {
        return Convert.ToBase64String(bytes);
    }

    public byte[] ComputeSignature(byte[] message)
    {
        return HMACSHA256.HashData(bytes, message);
    }

    public override string ToString()
    {
        return "[СКРЫТО]";
    }
}
