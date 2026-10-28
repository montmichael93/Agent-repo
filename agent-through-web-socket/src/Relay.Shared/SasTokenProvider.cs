using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Relay.Shared;

/// <summary>
/// Minimal Shared Access Signature (SAS) generator for Azure Relay.
///
/// Networking / security note: the token is *not* sent as a body. It is attached
/// either as the <c>ServiceBusAuthorization</c> HTTP header (HTTP(S) requests to the
/// relay gateway) or as the <c>sb-hc-token</c> query parameter during the WebSocket
/// upgrade handshake. The relay gateway validates it at the TLS edge *before* any
/// frame is forwarded into the tunnel, so an unauthenticated caller never reaches
/// machine M.
/// </summary>
public static class SasTokenProvider
{
    public static string CreateToken(
        string resourceUri,
        string keyName,
        string key,
        TimeSpan lifetime)
    {
        if (string.IsNullOrWhiteSpace(resourceUri)) throw new ArgumentNullException(nameof(resourceUri));
        if (string.IsNullOrWhiteSpace(keyName)) throw new ArgumentNullException(nameof(keyName));
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));

        var expiry = GetExpiry(lifetime);
        var encodedResource = WebUtility.UrlEncode(resourceUri.ToLowerInvariant());
        var stringToSign = encodedResource + "\n" + expiry;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));

        return string.Format(
            CultureInfo.InvariantCulture,
            "SharedAccessSignature sr={0}&sig={1}&se={2}&skn={3}",
            encodedResource,
            WebUtility.UrlEncode(signature),
            expiry,
            WebUtility.UrlEncode(keyName));
    }

    private static string GetExpiry(TimeSpan lifetime)
    {
        var epoch = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds();
        return epoch.ToString(CultureInfo.InvariantCulture);
    }
}
