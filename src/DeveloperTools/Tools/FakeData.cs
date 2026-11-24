using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using QRCoder;

namespace DeveloperTools.Tools;

/// <summary>Lorem ipsum text.</summary>
public static class Lorem
{
    private const string Opening = "Lorem ipsum dolor sit amet, consectetur adipiscing elit";

    private static readonly string[] Words = ("lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore et dolore " +
        "magna aliqua enim ad minim veniam quis nostrud exercitation ullamco laboris nisi aliquip ex ea commodo consequat duis aute irure " +
        "in reprehenderit voluptate velit esse cillum fugiat nulla pariatur excepteur sint occaecat cupidatat non proident sunt culpa qui " +
        "officia deserunt mollit anim id est laborum curabitur pretium tincidunt lacus nulla gravida orci a odio nullam varius turpis " +
        "egestas mauris vitae ultricies leo integer malesuada nunc vel risus commodo viverra maecenas accumsan lacus vel facilisis").Split(' ');

    private static string Word() => Words[RandomNumberGenerator.GetInt32(Words.Length)];

    public static string Sentence(int minWords = 6, int maxWords = 14)
    {
        var count = RandomNumberGenerator.GetInt32(minWords, maxWords + 1);
        var words = Enumerable.Range(0, count).Select(_ => Word()).ToList();
        if (count > 8) words[RandomNumberGenerator.GetInt32(3, count - 3)] += ",";
        var s = string.Join(" ", words);
        return char.ToUpperInvariant(s[0]) + s[1..] + ".";
    }

    public static string Paragraph() =>
        string.Join(" ", Enumerable.Range(0, RandomNumberGenerator.GetInt32(4, 8)).Select(_ => Sentence()));

    /// <param name="unit">"paragraphs", "sentences" or "words".</param>
    public static string Generate(string unit, int count, bool startWithLorem)
    {
        count = Math.Clamp(count, 1, 1000);
        string text = unit switch
        {
            "words" => string.Join(" ", Enumerable.Range(0, count).Select(_ => Word())),
            "sentences" => string.Join(" ", Enumerable.Range(0, count).Select(_ => Sentence())),
            _ => string.Join("\n\n", Enumerable.Range(0, count).Select(_ => Paragraph()))
        };

        if (!startWithLorem)
            return unit == "words" ? text : text;

        if (unit == "words")
        {
            var opening = Opening.Replace(",", "").Split(' ');
            var words = text.Split(' ');
            for (var i = 0; i < Math.Min(opening.Length, words.Length); i++) words[i] = opening[i];
            var joined = string.Join(" ", words);
            return char.ToUpperInvariant(joined[0]) + joined[1..];
        }

        // Replace the first sentence's start.
        var dot = text.IndexOf('.');
        return Opening + text[dot..];
    }
}

public enum FakeField
{
    FullName,
    FirstName,
    LastName,
    Email,
    Phone,
    Username,
    Company,
    Street,
    City,
    Country,
    BirthDate,
    Uuid,
    IPv4,
    Url,
    Sentence
}

/// <summary>
/// Obviously fake test data: reserved domains (example.com), fictional phone numbers (555-01xx),
/// documentation IP ranges (RFC 5737). Never real people.
/// </summary>
public static class FakeData
{
    private static readonly string[] First = ["Ada", "Alan", "Grace", "Linus", "Ayla", "Deniz", "Emre", "Elif", "Mert", "Zeynep", "Noah", "Mia", "Leo", "Nora", "Omar", "Sara", "Ivan", "Yuki", "Lena", "Kai", "Maya", "Arda", "Ece", "Can", "Selin", "Theo", "Iris", "Hugo", "Lina", "Felix"];
    private static readonly string[] Last = ["Example", "Sample", "Tester", "Placeholder", "Demo", "Mock", "Fixture", "Stub", "Dummy", "Prototype", "Draft", "Sandbox", "Specimen", "Template", "Default"];
    private static readonly string[] Companies = ["Acme Corp", "Globex Test Ltd", "Initech Demo", "Umbrella Sample Inc", "Example Labs", "Contoso Test", "Fabrikam Demo", "Northwind Sample", "Tailspin Test", "Wingtip Example"];
    private static readonly string[] Streets = ["Example Street", "Sample Avenue", "Test Road", "Demo Lane", "Mock Boulevard", "Fixture Way", "Placeholder Drive"];
    private static readonly string[] Cities = ["Springfield", "Riverton", "Lakeside", "Hillview", "Fairview", "Maplewood", "Brookside", "Oakridge"];
    private static readonly string[] Countries = ["Testland", "Sampleland", "Examplia", "Demoland", "Mockovia"];

    private static T Pick<T>(T[] items) => items[RandomNumberGenerator.GetInt32(items.Length)];
    private static int N(int min, int max) => RandomNumberGenerator.GetInt32(min, max + 1);

    public static string Label(FakeField f) => f switch
    {
        FakeField.FullName => "Full name",
        FakeField.FirstName => "First name",
        FakeField.LastName => "Last name",
        FakeField.Email => "Email",
        FakeField.Phone => "Phone",
        FakeField.Username => "Username",
        FakeField.Company => "Company",
        FakeField.Street => "Street address",
        FakeField.City => "City",
        FakeField.Country => "Country",
        FakeField.BirthDate => "Birth date",
        FakeField.Uuid => "UUID",
        FakeField.IPv4 => "IPv4",
        FakeField.Url => "URL",
        _ => "Sentence"
    };

    public static string Key(FakeField f) => f switch
    {
        FakeField.FullName => "name",
        FakeField.FirstName => "firstName",
        FakeField.LastName => "lastName",
        FakeField.Street => "street",
        FakeField.BirthDate => "birthDate",
        FakeField.IPv4 => "ip",
        _ => char.ToLowerInvariant(f.ToString()[0]) + f.ToString()[1..]
    };

    /// <summary>One record; related fields (name, email, username) stay consistent.</summary>
    public static List<(FakeField Field, string Value)> Record(IReadOnlyList<FakeField> fields)
    {
        var first = Pick(First);
        var last = Pick(Last);
        var user = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}{N(1, 99)}";
        var birth = new DateTime(N(1950, 2005), N(1, 12), N(1, 28));
        return fields.Select(f => (f, f switch
        {
            FakeField.FullName => $"{first} {last}",
            FakeField.FirstName => first,
            FakeField.LastName => last,
            FakeField.Email => $"{user}@example.com",
            FakeField.Phone => $"+1 555-01{N(0, 99):00}",
            FakeField.Username => user,
            FakeField.Company => Pick(Companies),
            FakeField.Street => $"{N(1, 999)} {Pick(Streets)}",
            FakeField.City => Pick(Cities),
            FakeField.Country => Pick(Countries),
            FakeField.BirthDate => birth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            FakeField.Uuid => Guid.NewGuid().ToString(),
            FakeField.IPv4 => Pick(["192.0.2", "198.51.100", "203.0.113"]) + "." + N(1, 254),
            FakeField.Url => $"https://example.com/{Pick(["users", "orders", "items", "docs"])}/{N(1, 9999)}",
            _ => Lorem.Sentence(5, 10)
        })).ToList();
    }

    public static string Generate(IReadOnlyList<FakeField> fields, int count, string format)
    {
        count = Math.Clamp(count, 1, 10_000);
        var records = Enumerable.Range(0, count).Select(_ => Record(fields)).ToList();
        var sb = new StringBuilder();
        switch (format)
        {
            case "CSV":
                sb.Append(string.Join(",", fields.Select(Key))).Append('\n');
                foreach (var r in records)
                    sb.Append(string.Join(",", r.Select(x => Csv(x.Value)))).Append('\n');
                break;
            case "Lines":
                foreach (var r in records)
                    sb.Append(string.Join("  ", r.Select(x => x.Value))).Append('\n');
                break;
            default:
                sb.Append("[\n");
                for (var i = 0; i < records.Count; i++)
                {
                    sb.Append("  {");
                    sb.Append(string.Join(", ", records[i].Select(x => $"{JsonTools.Escape(Key(x.Field))}: {JsonTools.Escape(x.Value)}")));
                    sb.Append(i < records.Count - 1 ? "},\n" : "}\n");
                }
                sb.Append(']');
                break;
        }
        return sb.ToString();
    }

    private static string Csv(string v) => v.IndexOfAny([',', '"', '\n']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
}

/// <summary>QR codes as PNG bytes.</summary>
public static class QrCode
{
    public static byte[] Png(string text, char eccLevel, int pixelsPerModule, bool dark)
    {
        var level = eccLevel switch
        {
            'L' => QRCodeGenerator.ECCLevel.L,
            'Q' => QRCodeGenerator.ECCLevel.Q,
            'H' => QRCodeGenerator.ECCLevel.H,
            _ => QRCodeGenerator.ECCLevel.M
        };
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, level);
        var png = new PngByteQRCode(data);
        byte[] black = [0x18, 0x18, 0x1B, 0xFF];
        byte[] white = [0xFF, 0xFF, 0xFF, 0xFF];
        return dark ? png.GetGraphic(pixelsPerModule, white, [0x13, 0x13, 0x15, 0xFF]) : png.GetGraphic(pixelsPerModule, black, white);
    }

    /// <summary>Wi-Fi network payload understood by phone cameras.</summary>
    public static string WifiPayload(string ssid, string password, string security, bool hidden)
    {
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace(":", "\\:").Replace("\"", "\\\"");
        var type = security == "None" ? "nopass" : security;
        return $"WIFI:T:{type};S:{Esc(ssid)};{(type == "nopass" ? "" : $"P:{Esc(password)};")}{(hidden ? "H:true;" : "")};";
    }
}
