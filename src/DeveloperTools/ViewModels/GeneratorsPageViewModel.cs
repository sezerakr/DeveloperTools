using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeveloperTools.Services;
using DeveloperTools.Tools;

namespace DeveloperTools.ViewModels;

public enum GeneratorTab
{
    Password,
    Secret,
    Uuid,
    Lorem,
    Fake,
    Qr
}

public enum LoremUnit
{
    Paragraphs,
    Sentences,
    Words
}

public enum QrMode
{
    Text,
    Wifi
}

public partial class FakeFieldOption(FakeField field, Action changed) : ObservableObject
{
    public FakeField Field { get; } = field;
    public string Label => FakeData.Label(Field);

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => changed();
}

public sealed record KeyFormatOption(KeyFormat Format, string Name)
{
    public override string ToString() => Name;
}

public partial class GeneratorsPageViewModel : PageViewModel
{
    private readonly IDialogService _dialogs;
    private bool _suspend;

    public GeneratorsPageViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        _keyFormat = KeyFormats[0];
        PropertyChanged += OnOptionChanged;
        GeneratePasswords();
        GenerateSecrets();
        GenerateUuids();

        foreach (var f in Enum.GetValues<FakeField>())
            FakeFields.Add(new FakeFieldOption(f, GenerateFake)
            {
                IsSelected = f is FakeField.FullName or FakeField.Email or FakeField.Phone or FakeField.City
            });
        GenerateLorem();
        GenerateFake();
        GenerateQr();
    }

    public override string Title => "Generators";
    public override string Subtitle => "Passwords, keys, UUIDs, test data and QR codes";

    [ObservableProperty]
    private GeneratorTab _tab = GeneratorTab.Password;

    private Task Copy(string text) => _dialogs.CopyToClipboardAsync(text);

    /// <summary>Regenerates whenever an option changes, so the output always matches the settings.</summary>
    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suspend)
            return;
        switch (e.PropertyName)
        {
            case nameof(PasswordLength) or nameof(UseUpper) or nameof(UseLower) or nameof(UseDigits)
                or nameof(UseSymbols) or nameof(ExcludeAmbiguous) or nameof(PasswordCount):
                GeneratePasswords();
                break;
            case nameof(KeyFormat) or nameof(KeySize) or nameof(CustomChars) or nameof(Prefix) or nameof(KeyCount):
                GenerateSecrets();
                break;
            case nameof(UuidVersion7) or nameof(UuidUppercase) or nameof(UuidHyphens) or nameof(UuidBraces) or nameof(UuidCount):
                GenerateUuids();
                break;
            case nameof(LoremUnit) or nameof(LoremCount) or nameof(LoremStart):
                GenerateLorem();
                break;
            case nameof(FakeCount) or nameof(FakeFormat):
                GenerateFake();
                break;
            case nameof(QrMode) or nameof(QrText) or nameof(WifiSsid) or nameof(WifiPassword) or nameof(WifiSecurity)
                or nameof(WifiHidden) or nameof(QrEcc) or nameof(QrScale):
                GenerateQr();
                break;
        }
    }

    private static int Count(decimal? value, int max) => (int)Math.Clamp(value ?? 1, 1, max);

    // ------------------------------------------------------------------ passwords

    [ObservableProperty] private double _passwordLength = 20;
    [ObservableProperty] private bool _useUpper = true;
    [ObservableProperty] private bool _useLower = true;
    [ObservableProperty] private bool _useDigits = true;
    [ObservableProperty] private bool _useSymbols = true;
    [ObservableProperty] private bool _excludeAmbiguous;
    [ObservableProperty] private decimal? _passwordCount = 5;

    [ObservableProperty] private string? _passwordError;
    [ObservableProperty] private int _strengthLevel;
    [ObservableProperty] private string _strengthText = "";

    public ObservableCollection<CopyableValue> Passwords { get; } = new();

    private PasswordOptions PasswordOptions =>
        new((int)PasswordLength, UseUpper, UseLower, UseDigits, UseSymbols, ExcludeAmbiguous);

    [RelayCommand]
    private void GeneratePasswords()
    {
        Passwords.Clear();
        var options = PasswordOptions;
        var bits = SecretGenerator.PasswordEntropy(options);
        StrengthLevel = SecretGenerator.StrengthLevel(bits);
        StrengthText = $"{SecretGenerator.StrengthName(StrengthLevel)}  ·  {bits:0} bits";
        try
        {
            for (var i = 0; i < Count(PasswordCount, 100); i++)
                Passwords.Add(new CopyableValue(SecretGenerator.Password(options), Copy));
            PasswordError = null;
        }
        catch (ArgumentException ex)
        {
            PasswordError = ex.Message;
            StrengthLevel = 0;
            StrengthText = "";
        }
    }

    [RelayCommand]
    private Task CopyAllPasswords() => Copy(string.Join(Environment.NewLine, Passwords.Select(p => p.Value)));

    // ------------------------------------------------------------------ secrets / keys

    public IReadOnlyList<KeyFormatOption> KeyFormats { get; } =
    [
        new(Tools.KeyFormat.Hex, "Hex"),
        new(Tools.KeyFormat.Base64, "Base64"),
        new(Tools.KeyFormat.Base64Url, "Base64 URL-safe"),
        new(Tools.KeyFormat.Alphanumeric, "Letters and digits"),
        new(Tools.KeyFormat.Custom, "Custom characters")
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeySizeLabel), nameof(IsCustomFormat))]
    private KeyFormatOption _keyFormat;

    [ObservableProperty] private decimal? _keySize = 32;
    [ObservableProperty] private string _customChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    [ObservableProperty] private string _prefix = "";
    [ObservableProperty] private decimal? _keyCount = 5;
    [ObservableProperty] private string? _keyError;
    [ObservableProperty] private string _keyInfo = "";

    public string KeySizeLabel => SecretGenerator.IsByteFormat(KeyFormat.Format) ? "Length in bytes" : "Length in characters";
    public bool IsCustomFormat => KeyFormat.Format == Tools.KeyFormat.Custom;

    public ObservableCollection<CopyableValue> Secrets { get; } = new();

    [RelayCommand]
    private void GenerateSecrets()
    {
        Secrets.Clear();
        var size = (int)Math.Clamp(KeySize ?? 32, 1, 4096);
        try
        {
            for (var i = 0; i < Count(KeyCount, 100); i++)
                Secrets.Add(new CopyableValue(Prefix + SecretGenerator.Key(KeyFormat.Format, size, CustomChars), Copy));
            var bits = SecretGenerator.KeyEntropy(KeyFormat.Format, size, CustomChars);
            KeyInfo = $"{bits:0} bits of randomness";
            KeyError = null;
        }
        catch (ArgumentException ex)
        {
            KeyError = ex.Message;
            KeyInfo = "";
        }
    }

    /// <summary>Common key shapes: format + size.</summary>
    [RelayCommand]
    private void ApplyPreset(string preset)
    {
        _suspend = true;
        (KeyFormat, KeySize) = preset switch
        {
            "api" => (KeyFormats[3], 32m),
            "jwt" => (KeyFormats[1], 64m),
            "aes256" => (KeyFormats[0], 32m),
            "aes128" => (KeyFormats[0], 16m),
            "token" => (KeyFormats[2], 32m),
            _ => (KeyFormat, KeySize)
        };
        _suspend = false;
        GenerateSecrets();
    }

    [RelayCommand]
    private Task CopyAllSecrets() => Copy(string.Join(Environment.NewLine, Secrets.Select(p => p.Value)));

    // ------------------------------------------------------------------ UUIDs

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UuidVersion4))]
    private bool _uuidVersion7;

    public bool UuidVersion4
    {
        get => !UuidVersion7;
        set => UuidVersion7 = !value;
    }

    [ObservableProperty] private bool _uuidUppercase;
    [ObservableProperty] private bool _uuidHyphens = true;
    [ObservableProperty] private bool _uuidBraces;
    [ObservableProperty] private decimal? _uuidCount = 10;

    public ObservableCollection<CopyableValue> Uuids { get; } = new();

    [RelayCommand]
    private void GenerateUuids()
    {
        Uuids.Clear();
        for (var i = 0; i < Count(UuidCount, 500); i++)
            Uuids.Add(new CopyableValue(SecretGenerator.Uuid(UuidVersion7, UuidUppercase, UuidHyphens, UuidBraces), Copy));
    }

    [RelayCommand]
    private Task CopyAllUuids() => Copy(string.Join(Environment.NewLine, Uuids.Select(p => p.Value)));
    // ------------------------------------------------------------------ lorem ipsum

    [ObservableProperty] private LoremUnit _loremUnit = LoremUnit.Paragraphs;
    [ObservableProperty] private decimal? _loremCount = 3;
    [ObservableProperty] private bool _loremStart = true;
    [ObservableProperty] private string _loremText = "";

    [RelayCommand]
    private void GenerateLorem() =>
        LoremText = Lorem.Generate(LoremUnit.ToString().ToLowerInvariant(), Count(LoremCount, 1000), LoremStart);

    [RelayCommand]
    private Task CopyLorem() => Copy(LoremText);

    // ------------------------------------------------------------------ fake data

    public ObservableCollection<FakeFieldOption> FakeFields { get; } = new();
    public IReadOnlyList<string> FakeFormats { get; } = ["JSON", "CSV", "Lines"];

    [ObservableProperty] private decimal? _fakeCount = 10;
    [ObservableProperty] private string _fakeFormat = "JSON";
    [ObservableProperty] private string _fakeOutput = "";

    [RelayCommand]
    private void GenerateFake()
    {
        var fields = FakeFields.Where(f => f.IsSelected).Select(f => f.Field).ToList();
        FakeOutput = fields.Count == 0 ? "" : FakeData.Generate(fields, Count(FakeCount, 10_000), FakeFormat);
    }

    [RelayCommand]
    private Task CopyFake() => Copy(FakeOutput);

    // ------------------------------------------------------------------ QR code

    public IReadOnlyList<string> WifiSecurities { get; } = ["WPA", "WEP", "None"];
    public IReadOnlyList<string> EccLevels { get; } = ["L", "M", "Q", "H"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQrText), nameof(IsQrWifi))]
    private QrMode _qrMode = QrMode.Text;

    public bool IsQrText => QrMode == QrMode.Text;
    public bool IsQrWifi => QrMode == QrMode.Wifi;

    [ObservableProperty] private string _qrText = "https://example.com";
    [ObservableProperty] private string _wifiSsid = "";
    [ObservableProperty] private string _wifiPassword = "";
    [ObservableProperty] private string _wifiSecurity = "WPA";
    [ObservableProperty] private bool _wifiHidden;
    [ObservableProperty] private string _qrEcc = "M";
    [ObservableProperty] private double _qrScale = 8;
    [ObservableProperty] private Bitmap? _qrImage;
    [ObservableProperty] private string _qrInfo = "";

    private byte[]? _qrPng;

    [RelayCommand]
    private void GenerateQr()
    {
        var payload = QrMode == QrMode.Wifi
            ? (WifiSsid.Length == 0 ? "" : QrCode.WifiPayload(WifiSsid, WifiPassword, WifiSecurity, WifiHidden))
            : QrText;
        if (payload.Length == 0)
        {
            QrImage = null;
            _qrPng = null;
            QrInfo = QrMode == QrMode.Wifi ? "Enter the network name." : "Enter text or a link.";
            return;
        }

        try
        {
            _qrPng = QrCode.Png(payload, QrEcc[0], (int)QrScale, dark: false);
            using var stream = new MemoryStream(_qrPng);
            QrImage = new Bitmap(stream);
            QrInfo = $"{QrImage.PixelSize.Width} × {QrImage.PixelSize.Height} px  ·  {payload.Length:N0} characters  ·  error correction {QrEcc}";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or QRCoder.Exceptions.DataTooLongException)
        {
            QrImage = null;
            _qrPng = null;
            QrInfo = "Too much text for a QR code. Shorten it or use a lower error correction level.";
        }
    }

    [RelayCommand]
    private async Task CopyQr()
    {
        if (QrImage is not null)
            await _dialogs.CopyImageAsync(QrImage);
    }

    [RelayCommand]
    private async Task SaveQr()
    {
        if (_qrPng is null)
            return;
        var file = await _dialogs.SaveFileAsync("Save QR code", "qr-code.png", "png", "PNG image");
        if (file is not null)
            await StorageFiles.WriteBytesAsync(file, _qrPng);
    }
}