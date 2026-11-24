using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Text;
using Avalonia.Media.Imaging;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeveloperTools.Services;
using DeveloperTools.Tools;

namespace DeveloperTools.ViewModels;

public partial class EncodePageViewModel : PageViewModel
{
    private readonly IDialogService _dialogs;

    public EncodePageViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        PropertyChanged += OnOptionChanged;
    }

    public override string Title => "Encode / Decode";
    public override string Subtitle => "Base64, URL, HTML, hex, Unicode, images, JWT and hashes";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDirection), nameof(IsBase64), nameof(IsHash), nameof(InputHint), nameof(IsImageMode), nameof(IsImageEncode), nameof(ShowTextInput))]
    private CodecMode _mode = CodecMode.Base64;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDecode), nameof(InputHint), nameof(IsImageEncode), nameof(ShowTextInput))]
    private bool _isEncode = true;

    public bool IsDecode
    {
        get => !IsEncode;
        set => IsEncode = !value;
    }

    [ObservableProperty] private string _input = "";
    [ObservableProperty] private bool _urlSafe;
    [ObservableProperty] private string _hmacKey = "";
    [ObservableProperty] private bool _hashBase64;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    [ObservableProperty] private string _inputStats = "";

    public bool HasError => Error is not null;
    public bool HasDirection => Mode is not (CodecMode.Jwt or CodecMode.Hash);
    public bool IsImageMode => Mode == CodecMode.Image;
    public bool IsImageEncode => Mode == CodecMode.Image && IsEncode;
    public bool ShowTextInput => !IsImageEncode;
    public bool IsBase64 => Mode == CodecMode.Base64;
    public bool IsHash => Mode == CodecMode.Hash;
    public bool HasNoOutput => Results.Count == 0 && Error is null && PreviewImage is null;

    public string InputHint => Mode switch
    {
        CodecMode.Jwt => "Paste a JWT (eyJhbGciOi…). \"Bearer \" is ignored.",
        CodecMode.Hash => "Text to hash (UTF-8)",
        CodecMode.Unicode when IsDecode => @"Text with escapes like \u00e7 \u{1F600} \x41",
        CodecMode.Unicode => "Text to escape (non-ASCII becomes \\uXXXX)",
        CodecMode.Image => "Base64 or a data: URI (data:image/png;base64,…)",
        CodecMode.Base64 when IsDecode => "Base64 or Base64URL text",
        CodecMode.Hex when IsDecode => "Hex bytes, e.g. 48 65 6c 6c 6f",
        _ when IsDecode => "Text to decode",
        _ => "Text to encode"
    };

    public ObservableCollection<CopyableValue> Results { get; } = new();

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Mode) or nameof(IsEncode) or nameof(Input) or nameof(UrlSafe)
            or nameof(HmacKey) or nameof(HashBase64))
            Run();
    }

    // ------------------------------------------------------------------ images

    private const int MaxShown = 20_000;
    private byte[]? _imageBytes;
    private string? _imageName;
    private string _imageExtension = "png";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    private Bitmap? _previewImage;

    [ObservableProperty] private string _imageInfo = "";

    public bool HasImage => PreviewImage is not null;

    [RelayCommand]
    private async Task OpenImage()
    {
        var path = await _dialogs.PickOpenFileAsync("Open image", "Images", "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp", "*.ico", "*.svg");
        if (path is null)
            return;
        var info = new FileInfo(path);
        if (info.Length > 20 * 1024 * 1024)
        {
            Error = "Images up to 20 MB can be encoded here.";
            return;
        }
        _imageBytes = await File.ReadAllBytesAsync(path);
        _imageName = info.Name;
        Run();
    }

    [RelayCommand]
    private async Task SaveImage()
    {
        if (_imageBytes is null)
            return;
        var path = await _dialogs.PickSaveFileAsync("Save image", "image." + _imageExtension, _imageExtension, "Image");
        if (path is not null)
            await File.WriteAllBytesAsync(path, _imageBytes);
    }

    private void ShowImage(byte[] bytes)
    {
        var type = Codecs.DetectImage(bytes);
        _imageExtension = type?.Extension ?? "bin";
        PreviewImage = null;
        if (type is { Extension: not "svg" })
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                PreviewImage = new Bitmap(stream);
            }
            catch (Exception)
            {
                // unsupported variant: info only
            }
        }
        var size = PreviewImage is null ? "" : $"{PreviewImage.PixelSize.Width} × {PreviewImage.PixelSize.Height} px  ·  ";
        ImageInfo = $"{size}{type?.Mime ?? "unknown type"}  ·  {bytes.Length:N0} bytes";
    }

    private CopyableValue Long(string value, string label, string? note = null) =>
        new(value, t => _dialogs.CopyToClipboardAsync(t), label, note,
            value.Length > MaxShown ? value[..MaxShown] + $"…  ({value.Length:N0} characters; Copy takes all of it)" : null);

    private void RunImage()
    {
        if (IsEncode)
        {
            if (_imageBytes is null)
            {
                PreviewImage = null;
                ImageInfo = "";
                return;
            }
            ShowImage(_imageBytes);
            var mime = Codecs.DetectImage(_imageBytes)?.Mime ?? "application/octet-stream";
            var base64 = Convert.ToBase64String(_imageBytes);
            Results.Add(Long($"data:{mime};base64,{base64}", "Data URI", $"{_imageName}. Use in HTML/CSS: src=\"data:…\""));
            Results.Add(Long(base64, "Base64"));
            return;
        }

        PreviewImage = null;
        ImageInfo = "";
        if (Input.Trim().Length == 0)
            return;
        var bytes = Codecs.DecodeDataUri(Input);
        if (Codecs.DetectImage(bytes) is null)
            throw new FormatException("The data decodes fine but is not a PNG, JPEG, GIF, WebP, BMP, ICO or SVG image.");
        _imageBytes = bytes;
        ShowImage(bytes);
    }

    private void Run()
    {
        Results.Clear();
        Error = null;
        if (Mode == CodecMode.Image)
        {
            try
            {
                RunImage();
            }
            catch (FormatException ex)
            {
                Error = ex.Message;
            }
            OnPropertyChanged(nameof(HasNoOutput));
            return;
        }
        PreviewImage = null;
        InputStats = Input.Length == 0 ? "" : $"{Input.Length:N0} chars  ·  {Encoding.UTF8.GetByteCount(Input):N0} bytes";
        try
        {
            foreach (var r in Codecs.Run(Mode, Input, IsEncode, UrlSafe, HmacKey, HashBase64))
                Results.Add(Long(r.Value, r.Label, r.Note));
        }
        catch (FormatException ex)
        {
            Error = ex.Message;
        }

        OnPropertyChanged(nameof(HasNoOutput));
    }

    /// <summary>Feeds the output back as input and flips the direction.</summary>
    [RelayCommand]
    private void Swap()
    {
        var output = Results.FirstOrDefault()?.Value;
        if (output is null || !HasDirection)
            return;
        IsEncode = !IsEncode;
        Input = output;
    }

    [RelayCommand]
    private void Clear() => Input = "";

    [RelayCommand]
    private Task CopyResult() =>
        Results.FirstOrDefault() is { } first ? _dialogs.CopyToClipboardAsync(first.Value) : Task.CompletedTask;
}
