using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using DeveloperTools.Services;
using DeveloperTools.Tools;
using DeveloperTools.ViewModels;

namespace DeveloperTools;

/// <summary>
/// "DeveloperTools.exe --selftest log.txt": opens every page and exercises each tool in the real build
/// (used to verify the native AOT build). Saves nothing; the theme change is not persisted.
/// </summary>
public static class SelfTest
{
    public static string? LogPath;

    public static async Task RunAsync(MainWindowViewModel vm)
    {
        var log = new List<string>();
        var failures = 0;
        var clock = Stopwatch.StartNew();

        async Task Step(string name, Func<Task> action)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                await action();
                log.Add($"OK   {name} ({sw.ElapsedMilliseconds} ms)");
            }
            catch (Exception ex)
            {
                failures++;
                log.Add($"FAIL {name}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        static Task Wait(int ms = 250) => Task.Delay(ms);
        static void Expect(bool condition, string what)
        {
            if (!condition) throw new InvalidOperationException("Expected " + what);
        }

        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Application.Current!.RequestedThemeVariant = theme;
            var t = theme == ThemeVariant.Dark ? "dark" : "light";

            await Step($"{t}: JSON", async () =>
            {
                vm.CurrentPage = vm.Json;
                vm.Json.LoadText("{name: 'Ada', tags: ['x',], // c\n n: .5}");
                await vm.Json.RepairCommand.ExecuteAsync(null);
                await Wait();
                Expect(vm.Json.IsValid, "repaired JSON to be valid");
                vm.Json.Panel.IsOpen = true;
                foreach (var tab in Enum.GetValues<JsonPanelTab>())
                {
                    vm.Json.Panel.Tab = tab;
                    await Wait(300);
                }
                Expect(vm.Json.Panel.YamlOutput.Contains("name: Ada"), "YAML output");
                Expect(vm.Json.Panel.CodeOutput.Contains("class Root"), "generated code");
                vm.Json.Panel.IsOpen = false;
            });

            await Step($"{t}: Diff", async () =>
            {
                vm.CurrentPage = vm.Diff;
                vm.Diff.LeftDocument.Text = "a\nb\nc\nvar x = 1;";
                vm.Diff.RightDocument.Text = "a\nB\nc\nvar x = 2;\nd";
                vm.Diff.View = DiffView.SideBySide;
                await Wait(500);
                Expect(vm.Diff.Result is { Identical: false }, "a diff result");
                vm.Diff.NextChangeCommand.Execute(null);
                vm.Diff.View = DiffView.Inline;
                await Wait(400);
                vm.Diff.View = DiffView.Edit;
            });

            await Step($"{t}: Regex", async () =>
            {
                vm.CurrentPage = vm.Regex;
                vm.Regex.RunNow();
                await Wait(400);
                Expect(vm.Regex.HasMatches, "regex matches");
                foreach (var p in Enum.GetValues<RegexPanel>()) { vm.Regex.Panel = p; await Wait(150); }
            });

            await Step($"{t}: Timestamp", async () =>
            {
                vm.CurrentPage = vm.Timestamp;
                vm.Timestamp.Input = "1700000000";
                await Wait();
                Expect(vm.Timestamp.Results.Count > 5, "timestamp rows");
            });

            await Step($"{t}: Text", async () =>
            {
                vm.CurrentPage = vm.Text;
                vm.Text.Document.Text = "hello world\nfoo bar";
                vm.Text.CaseCommand.Execute("Pascal");
                await Wait();
                Expect(vm.Text.Document.Text.StartsWith("HelloWorld"), "case conversion");
            });

            await Step($"{t}: Generators", async () =>
            {
                vm.CurrentPage = vm.Generators;
                foreach (var tab in Enum.GetValues<GeneratorTab>()) { vm.Generators.Tab = tab; await Wait(200); }
                Expect(vm.Generators.QrImage is not null, "QR image");
                Expect(vm.Generators.Passwords.Count > 0, "passwords");
            });

            await Step($"{t}: Encode", async () =>
            {
                vm.CurrentPage = vm.Encode;
                foreach (var mode in Enum.GetValues<CodecMode>())
                {
                    vm.Encode.Mode = mode;
                    vm.Encode.IsEncode = true;
                    vm.Encode.Input = mode == CodecMode.Jwt
                        ? "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.x"
                        : "Merhaba dünya 😀";
                    await Wait(120);
                }
                vm.Encode.Mode = CodecMode.Image;
                vm.Encode.IsEncode = false;
                vm.Encode.Input = Convert.ToBase64String(QrCode.Png("x", 'M', 4, false));
                await Wait(200);
                Expect(vm.Encode.HasImage, "decoded image preview");
            });

            await Step($"{t}: Markdown + Settings", async () =>
            {
                vm.CurrentPage = vm.Markdown;
                await Wait();
                vm.CurrentPage = vm.Settings;
                await Wait();
                vm.CurrentPage = vm.Markdown;
            });

            await Step($"{t}: Palette", async () =>
            {
                vm.OpenPaletteCommand.Execute(null);
                vm.PaletteQuery = "json";
                await Wait();
                Expect(vm.PaletteResults.Count > 3, "palette results");
                vm.ClosePaletteCommand.Execute(null);
            });
        }

        Application.Current!.RequestedThemeVariant = App.ThemeFromSetting(vm.Settings.Theme);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        log.Add($"Total {clock.ElapsedMilliseconds} ms, failures: {failures}");
        if (OperatingSystem.IsBrowser())
            log.Add($"Managed heap after visiting every page: {GC.GetTotalMemory(false) / 1048576} MB");
        else
        {
            var process = Process.GetCurrentProcess();
            log.Add($"Memory after visiting every page: working set {process.WorkingSet64 / 1048576} MB, private {process.PrivateMemorySize64 / 1048576} MB, managed heap {GC.GetTotalMemory(false) / 1048576} MB");
        }

        if (LogPath is not null)
            await File.WriteAllLinesAsync(LogPath, log);
        AppServices.SelfTestReport?.Invoke((failures == 0 ? "SELFTEST PASSED" : $"SELFTEST FAILED ({failures})") + "\n" + string.Join("\n", log));
        (Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(failures == 0 ? 0 : 1);
    }
}
