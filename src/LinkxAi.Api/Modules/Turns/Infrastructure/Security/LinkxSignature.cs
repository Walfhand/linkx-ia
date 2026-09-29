using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LinkxAi.Api.Modules.Turns.Infrastructure.Security;

public static class LinkxSignature
{
    public static bool IsValid(string secret, string timestamp, string signature, ReadOnlySpan<byte> body, DateTimeOffset now)
    {
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var issuedAt) ||
            issuedAt < now.ToUnixTimeSeconds() - 300 || issuedAt > now.ToUnixTimeSeconds() + 300 ||
            !signature.StartsWith("sha256=", StringComparison.Ordinal))
            return false;

        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(signature[7..]);
        }
        catch (FormatException)
        {
            return false;
        }

        var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        var message = new byte[prefix.Length + body.Length];
        prefix.CopyTo(message, 0);
        body.CopyTo(message.AsSpan(prefix.Length));
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), message);
        return CryptographicOperations.FixedTimeEquals(supplied, expected);
    }
}
