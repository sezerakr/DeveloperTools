using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace DeveloperTools.Tools;

public sealed record PasswordOptions(
    int Length,
    bool Upper = true,
    bool Lower = true,
    bool Digits = true,
    bool Symbols = true,
    bool ExcludeAmbiguous = false);

public enum KeyFormat
{
    Hex,
    Base64,
    Base64Url,
    Alphanumeric,
    Custom
}

/// <summary>Passwords, keys and UUIDs from the OS cryptographic RNG (unbiased choice, nothing stored).</summary>
public static class SecretGenerator
{
    public const string UpperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    public const string LowerChars = "abcdefghijklmnopqrstuvwxyz";
    public const string DigitChars = "0123456789";
    public const string SymbolChars = "!@#$%^&*()-_=+[]{};:,.<>?/~";
    private const string Ambiguous = "Il1O0o";
    private const string AlphanumericChars = UpperChars + LowerChars + DigitChars;

    // ------------------------------------------------------------------ passwords

    private static List<string> PasswordSets(PasswordOptions o)
    {
        var sets = new List<string>();
        if (o.Upper) sets.Add(UpperChars);
        if (o.Lower) sets.Add(LowerChars);
        if (o.Digits) sets.Add(DigitChars);
        if (o.Symbols) sets.Add(SymbolChars);
        if (o.ExcludeAmbiguous)
            sets = sets.Select(s => new string(s.Where(c => !Ambiguous.Contains(c)).ToArray())).ToList();
        return sets;
    }

    public static int PasswordPoolSize(PasswordOptions o) => PasswordSets(o).Sum(s => s.Length);

    public static double PasswordEntropy(PasswordOptions o)
    {
        var pool = PasswordPoolSize(o);
        return pool <= 1 ? 0 : o.Length * Math.Log2(pool);
    }

    /// <exception cref="ArgumentException">No character set selected.</exception>
    public static string Password(PasswordOptions o)
    {
        var sets = PasswordSets(o);
        if (sets.Count == 0)
            throw new ArgumentException("Choose at least one character set.");

        var pool = string.Concat(sets);
        var password = RandomNumberGenerator.GetString(pool, o.Length);
        // Re-draw until every chosen set is present (keeps the distribution uniform over valid passwords).
        for (var attempt = 0; attempt < 1000 && o.Length >= sets.Count; attempt++)
        {
            if (sets.All(s => password.AsSpan().IndexOfAny(s) >= 0))
                break;
            password = RandomNumberGenerator.GetString(pool, o.Length);
        }

        return password;
    }

    /// <summary>0 = very weak … 4 = very strong.</summary>
    public static int StrengthLevel(double bits) => bits switch
    {
        < 36 => 0,
        < 60 => 1,
        < 80 => 2,
        < 112 => 3,
        _ => 4
    };

    public static string StrengthName(int level) => level switch
    {
        0 => "Very weak",
        1 => "Weak",
        2 => "Fair",
        3 => "Strong",
        _ => "Very strong"
    };

    // ------------------------------------------------------------------ keys

    /// <summary>Byte-based formats take <paramref name="size"/> in bytes; text formats in characters.</summary>
    public static bool IsByteFormat(KeyFormat format) => format is KeyFormat.Hex or KeyFormat.Base64 or KeyFormat.Base64Url;

    public static string Key(KeyFormat format, int size, string? customChars = null)
    {
        switch (format)
        {
            case KeyFormat.Hex:
                return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(size));
            case KeyFormat.Base64:
                return Convert.ToBase64String(RandomNumberGenerator.GetBytes(size));
            case KeyFormat.Base64Url:
                return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(size));
            case KeyFormat.Alphanumeric:
                return RandomNumberGenerator.GetString(AlphanumericChars, size);
            default:
                var chars = DistinctChars(customChars);
                if (chars.Length < 2)
                    throw new ArgumentException("Enter at least two different characters.");
                return RandomNumberGenerator.GetString(chars, size);
        }
    }

    public static double KeyEntropy(KeyFormat format, int size, string? customChars = null) => format switch
    {
        KeyFormat.Hex or KeyFormat.Base64 or KeyFormat.Base64Url => size * 8,
        KeyFormat.Alphanumeric => size * Math.Log2(AlphanumericChars.Length),
        _ => DistinctChars(customChars) is { Length: > 1 } c ? size * Math.Log2(c.Length) : 0
    };

    private static string DistinctChars(string? chars) =>
        new((chars ?? "").Where(c => !char.IsControl(c)).Distinct().ToArray());

    // ------------------------------------------------------------------ UUIDs

    public static string Uuid(bool version7, bool uppercase, bool hyphens, bool braces)
    {
        var guid = version7 ? Guid.CreateVersion7() : Guid.NewGuid();
        var text = guid.ToString(hyphens ? "D" : "N");
        if (uppercase)
            text = text.ToUpperInvariant();
        return braces ? "{" + text + "}" : text;
    }
}
