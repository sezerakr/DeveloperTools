using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DeveloperTools.Tools;

public enum CodecMode
{
    Base64,
    Url,
    Html,
    Hex,
    Jwt,
    Hash,
    Unicode,
    Image
}

/// <summary>One labelled output value.</summary>
public sealed record CodecResult(string Label, string Value, string? Note = null);

/// <summary>Text encoders/decoders, a JWT decoder and hashes. Everything runs locally.</summary>
public static class Codecs
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static IReadOnlyList<CodecResult> Run(CodecMode mode, string input, bool encode, bool urlSafe,
        string? hmacKey, bool hashBase64)
    {
        if (input.Length == 0)
            return [];

        return mode switch
        {
            CodecMode.Base64 when encode => [new("Base64" + (urlSafe ? " (URL-safe)" : ""), Base64Encode(input, urlSafe))],
            CodecMode.Base64 => [BytesToText(Base64Decode(input))],
            CodecMode.Url when encode => [new("URL-encoded", Uri.EscapeDataString(input))],
            CodecMode.Url => [new("Decoded", WebUtility.UrlDecode(input))],
            CodecMode.Html when encode => [new("HTML-encoded", WebUtility.HtmlEncode(input))],
            CodecMode.Html => [new("Decoded", WebUtility.HtmlDecode(input))],
            CodecMode.Hex when encode => [new("Hex", Convert.ToHexStringLower(Encoding.UTF8.GetBytes(input)))],
            CodecMode.Hex => [BytesToText(HexDecode(input))],
            CodecMode.Unicode when encode => [new("Escaped", UnicodeEscape(input))],
            CodecMode.Unicode => [new("Unescaped", UnicodeUnescape(input))],
            CodecMode.Jwt => DecodeJwt(input),
            CodecMode.Hash => Hashes(input, hmacKey, hashBase64),
            _ => []
        };
    }

    // ------------------------------------------------------------------ base64 / hex

    public static string Base64Encode(string text, bool urlSafe)
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        return urlSafe ? base64.TrimEnd('=').Replace('+', '-').Replace('/', '_') : base64;
    }

    /// <summary>Accepts standard and URL-safe alphabets, missing padding and line breaks.</summary>
    public static byte[] Base64Decode(string text)
    {
        var clean = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray())
            .Replace('-', '+').Replace('_', '/');
        clean = clean.TrimEnd('=');
        clean += (clean.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        if (clean.Length % 4 == 1)
            throw new FormatException("Not valid Base64: the length is wrong.");
        try
        {
            return Convert.FromBase64String(clean);
        }
        catch (FormatException)
        {
            throw new FormatException("Not valid Base64: it contains characters outside the Base64 alphabet.");
        }
    }

    public static byte[] HexDecode(string text)
    {
        var clean = new string(text.Where(c => !char.IsWhiteSpace(c) && c is not (':' or '-')).ToArray());
        if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            clean = clean[2..];
        clean = clean.Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (clean.Length % 2 != 0)
            throw new FormatException("Not valid hex: it needs an even number of digits.");
        try
        {
            return Convert.FromHexString(clean);
        }
        catch (FormatException)
        {
            throw new FormatException("Not valid hex: use only 0-9 and a-f.");
        }
    }

    private static CodecResult BytesToText(byte[] bytes)
    {
        try
        {
            return new("Decoded text", StrictUtf8.GetString(bytes), $"{bytes.Length:N0} bytes, UTF-8");
        }
        catch (DecoderFallbackException)
        {
            return new("Decoded bytes (hex)", Convert.ToHexStringLower(bytes),
                $"{bytes.Length:N0} bytes. Not UTF-8 text, so shown as hex.");
        }
    }

    // ------------------------------------------------------------------ unicode

    /// <summary>Escapes everything outside printable ASCII as \uXXXX (surrogate pairs for emoji).</summary>
    public static string UnicodeEscape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c >= 0x20 && c < 0x7F && c != '\\')
                sb.Append(c);
            else if (c == '\\')
                sb.Append("\\\\");
            else if (c == '\n')
                sb.Append("\\n");
            else if (c == '\t')
                sb.Append("\\t");
            else if (c == '\r')
                sb.Append("\\r");
            else
                sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>Reads \uXXXX, \u{1F600}, \UXXXXXXXX, \xNN and the usual \n \t \r \\ escapes.</summary>
    public static string UnicodeUnescape(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\\' || i + 1 >= text.Length)
            {
                sb.Append(c);
                continue;
            }

            var n = text[i + 1];
            string? hex = null;
            var skip = 0;
            if (n == 'u' && i + 2 < text.Length && text[i + 2] == '{')
            {
                var close = text.IndexOf('}', i + 3);
                if (close > 0) { hex = text[(i + 3)..close]; skip = close - i; }
            }
            else if (n == 'u' && i + 6 <= text.Length) { hex = text.Substring(i + 2, 4); skip = 5; }
            else if (n == 'U' && i + 10 <= text.Length) { hex = text.Substring(i + 2, 8); skip = 9; }
            else if (n == 'x' && i + 4 <= text.Length) { hex = text.Substring(i + 2, 2); skip = 3; }

            if (hex is not null && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code) && code <= 0x10FFFF)
            {
                // Lone surrogates (😀 written as two escapes) are appended as-is and pair up.
                sb.Append(code is >= 0xD800 and <= 0xDFFF ? ((char)code).ToString() : char.ConvertFromUtf32(code));
                i += skip;
                continue;
            }

            switch (n)
            {
                case 'n': sb.Append('\n'); i++; break;
                case 't': sb.Append('\t'); i++; break;
                case 'r': sb.Append('\r'); i++; break;
                case '\\': sb.Append('\\'); i++; break;
                case '"': sb.Append('"'); i++; break;
                case '\'': sb.Append('\''); i++; break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ images

    /// <summary>Image type from the first bytes, or null when it is not a common image format.</summary>
    public static (string Mime, string Extension)? DetectImage(byte[] b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ("image/png", "png");
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ("image/jpeg", "jpg");
        if (b.Length >= 6 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F') return ("image/gif", "gif");
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return ("image/webp", "webp");
        if (b.Length >= 2 && b[0] == 'B' && b[1] == 'M') return ("image/bmp", "bmp");
        if (b.Length >= 4 && b[0] == 0 && b[1] == 0 && b[2] == 1 && b[3] == 0) return ("image/x-icon", "ico");
        var head = Encoding.UTF8.GetString(b, 0, Math.Min(b.Length, 256)).TrimStart();
        if (head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) || (head.StartsWith("<?xml") && head.Contains("<svg"))) return ("image/svg+xml", "svg");
        return null;
    }

    /// <summary>Accepts raw Base64 or a data: URI.</summary>
    public static byte[] DecodeDataUri(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = t.IndexOf(',');
            if (comma < 0) throw new FormatException("The data URI has no comma before the data.");
            t = t[(comma + 1)..];
        }
        return Base64Decode(t);
    }

    // ------------------------------------------------------------------ JWT

    public static IReadOnlyList<CodecResult> DecodeJwt(string input)
    {
        var token = input.Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = token[7..].Trim();

        var parts = token.Split('.');
        if (parts.Length == 5)
            throw new FormatException("This is an encrypted token (JWE). Only signed tokens (JWS) can be read without the key.");
        if (parts.Length != 3)
            throw new FormatException("A JWT has three parts separated by dots: header.payload.signature.");

        var header = DecodeJwtPart(parts[0], "header");
        var payload = DecodeJwtPart(parts[1], "payload");

        var results = new List<CodecResult>
        {
            new("Header", PrettyJson(header)),
            new("Payload", PrettyJson(payload))
        };

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                AddTime(results, doc.RootElement, "exp", "Expires");
                AddTime(results, doc.RootElement, "iat", "Issued at");
                AddTime(results, doc.RootElement, "nbf", "Not before");
            }
        }
        catch (JsonException)
        {
            // payload isn't JSON; already shown as text
        }

        var alg = "";
        try
        {
            using var doc = JsonDocument.Parse(header);
            if (doc.RootElement.TryGetProperty("alg", out var a) && a.ValueKind == JsonValueKind.String)
                alg = a.GetString() ?? "";
        }
        catch (JsonException)
        {
        }

        results.Add(new("Signature" + (alg.Length > 0 ? $" ({alg})" : ""), parts[2],
            "Not verified. The token is decoded locally and never sent anywhere."));
        return results;
    }

    private static string DecodeJwtPart(string part, string name)
    {
        try
        {
            return StrictUtf8.GetString(Base64Decode(part));
        }
        catch (Exception e) when (e is FormatException or DecoderFallbackException)
        {
            throw new FormatException($"The {name} is not valid Base64URL-encoded JSON.");
        }
    }

    private static string PrettyJson(string json)
    {
        try
        {
            return JsonTools.Format(json, "  ");
        }
        catch (FormatException)
        {
            return json;
        }
    }

    private static void AddTime(List<CodecResult> results, JsonElement root, string claim, string label)
    {
        if (!root.TryGetProperty(claim, out var value) || !value.TryGetInt64(out var seconds))
            return;

        DateTimeOffset time;
        try
        {
            time = DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        var local = time.ToLocalTime();
        var delta = time - DateTimeOffset.UtcNow;
        var relative = delta.TotalSeconds >= 0 ? "in " + Humanize(delta) : Humanize(delta.Negate()) + " ago";
        var note = claim == "exp"
            ? (delta.TotalSeconds >= 0 ? $"Valid, expires {relative}" : $"Expired {relative}")
            : relative;
        results.Add(new($"{label} ({claim})",
            local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + local.ToString("zzz", CultureInfo.InvariantCulture),
            note));
    }

    private static string Humanize(TimeSpan span) => span.TotalSeconds switch
    {
        < 90 => $"{(int)span.TotalSeconds} seconds",
        < 5400 => $"{(int)span.TotalMinutes} minutes",
        < 172800 => $"{(int)span.TotalHours} hours",
        < 5184000 => $"{(int)span.TotalDays} days",
        < 63072000 => $"{(int)(span.TotalDays / 30)} months",
        _ => $"{(int)(span.TotalDays / 365)} years"
    };

    // ------------------------------------------------------------------ hashes

    public static IReadOnlyList<CodecResult> Hashes(string input, string? hmacKey, bool base64)
    {
        var data = Encoding.UTF8.GetBytes(input);
        string Out(byte[] hash) => base64 ? Convert.ToBase64String(hash) : Convert.ToHexStringLower(hash);

        if (!string.IsNullOrEmpty(hmacKey))
        {
            var key = Encoding.UTF8.GetBytes(hmacKey);
            return
            [
                new("HMAC-SHA256", Out(HMACSHA256.HashData(key, data))),
                new("HMAC-SHA384", Out(HMACSHA384.HashData(key, data))),
                new("HMAC-SHA512", Out(HMACSHA512.HashData(key, data))),
                new("HMAC-SHA1", Out(HMACSHA1.HashData(key, data)), "Legacy. Prefer HMAC-SHA256."),
                OperatingSystem.IsBrowser()
                    ? new CodecResult("HMAC-MD5", "Not available in the browser version.")
                    : new CodecResult("HMAC-MD5", Out(HMACMD5.HashData(key, data)), "Legacy. Prefer HMAC-SHA256.")
            ];
        }

        return
        [
            new("SHA-256", Out(SHA256.HashData(data))),
            new("SHA-384", Out(SHA384.HashData(data))),
            new("SHA-512", Out(SHA512.HashData(data))),
            new("SHA-1", Out(SHA1.HashData(data)), "Not collision-resistant. Use for checksums only."),
            // MD5 is not implemented in .NET's WebAssembly runtime.
            OperatingSystem.IsBrowser()
                ? new CodecResult("MD5", "Not available in the browser version.")
                : new CodecResult("MD5", Out(MD5.HashData(data)), "Not secure. Use for checksums only.")
        ];
    }
}
