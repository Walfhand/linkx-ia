using System.Security.Cryptography;
using System.Text;
using LinkxAi.Api.Modules.Turns.Infrastructure.Security;
using Xunit;

namespace LinkxAi.Tests.Unit;

public sealed class LinkxSignatureTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1756900000);
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"record\":\"b 15 --\"}");

    [Fact]
    public void IsValid_ShouldAcceptSignature_WhenTimestampAndExactBodyMatch()
    {
        var signature = Sign("secret", "1756900000", Body);

        Assert.True(LinkxSignature.IsValid("secret", "1756900000", signature, Body, Now));
    }

    [Fact]
    public void IsValid_ShouldRejectSignature_WhenBodyChanges()
    {
        var signature = Sign("secret", "1756900000", Body);

        Assert.False(LinkxSignature.IsValid("secret", "1756900000", signature, Encoding.UTF8.GetBytes("{}"), Now));
    }

    [Theory]
    [InlineData("1756899699")]
    [InlineData("1756900301")]
    public void IsValid_ShouldRejectSignature_WhenTimestampIsOutsideFiveMinutes(string timestamp)
    {
        var signature = Sign("secret", timestamp, Body);

        Assert.False(LinkxSignature.IsValid("secret", timestamp, signature, Body, Now));
    }

    [Fact]
    public void IsValid_ShouldRejectSignature_WhenDigestIsMalformed()
    {
        Assert.False(LinkxSignature.IsValid("secret", "1756900000", "sha256=broken", Body, Now));
    }

    private static string Sign(string secret, string timestamp, byte[] body)
    {
        var message = Encoding.UTF8.GetBytes($"{timestamp}.").Concat(body).ToArray();
        return "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), message));
    }
}
