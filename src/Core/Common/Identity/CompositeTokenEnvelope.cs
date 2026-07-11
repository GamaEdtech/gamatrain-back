namespace GamaEdtech.Common.Identity
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Temporary bridge format wrapping a gamatrain-back token together with a gama-api (legacy) token, so the
    /// frontend can send one Authorization value to either backend during the migration window. Each backend
    /// only ever trusts the half it validates itself; the HMAC only guards against the two halves being spliced
    /// from different sessions, not against forging either token on its own.
    /// Remove alongside LegacyAuthBridgeController once the frontend migrates off the old backend.
    /// </summary>
    public static class CompositeTokenEnvelope
    {
        public static string Encode([NotNull] string newBackToken, [NotNull] string oldBackToken, [NotNull] string secret)
        {
            ArgumentNullException.ThrowIfNull(newBackToken);
            ArgumentNullException.ThrowIfNull(oldBackToken);
            ArgumentNullException.ThrowIfNull(secret);

            EnvelopeDto envelope = new()
            {
                NewBackToken = newBackToken,
                OldBackToken = oldBackToken,
                Signature = ComputeSignature(newBackToken, oldBackToken, secret),
            };
            var json = JsonSerializer.Serialize(envelope);
            return Base64UrlEncode(json);
        }

        public static CompositeTokenParts? TryDecode([NotNull] string token, [NotNull] string secret)
        {
            ArgumentNullException.ThrowIfNull(token);
            ArgumentNullException.ThrowIfNull(secret);

            EnvelopeDto? envelope;
            try
            {
                var json = Base64UrlDecode(token);
                envelope = JsonSerializer.Deserialize<EnvelopeDto>(json);
            }
            catch (Exception exc) when (exc is FormatException or JsonException)
            {
                return null;
            }

            if (string.IsNullOrEmpty(envelope?.NewBackToken) || string.IsNullOrEmpty(envelope.OldBackToken) || string.IsNullOrEmpty(envelope.Signature))
            {
                return null;
            }

            var expectedSignature = ComputeSignature(envelope.NewBackToken, envelope.OldBackToken, secret);
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expectedSignature), Encoding.UTF8.GetBytes(envelope.Signature))
                ? new CompositeTokenParts(envelope.NewBackToken, envelope.OldBackToken)
                : null;
        }

        private static string ComputeSignature(string newBackToken, string oldBackToken, string secret)
        {
            var data = Encoding.UTF8.GetBytes($"{newBackToken}.{oldBackToken}");
            var key = Encoding.UTF8.GetBytes(secret);
            return Convert.ToBase64String(HMACSHA256.HashData(key, data));
        }

        private static string Base64UrlEncode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static string Base64UrlDecode(string value)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - (padded.Length % 4)) % 4);
            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }

        private sealed class EnvelopeDto
        {
            [JsonPropertyName("n")]
            public string? NewBackToken { get; set; }

            [JsonPropertyName("o")]
            public string? OldBackToken { get; set; }

            [JsonPropertyName("sig")]
            public string? Signature { get; set; }
        }
    }

    public sealed record CompositeTokenParts(string NewBackToken, string OldBackToken);
}
