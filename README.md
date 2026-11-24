# DeveloperTools

Everyday developer tools in one fast app, for **Windows**, **Linux** and the **browser**.
Everything runs locally: nothing you paste or open is uploaded.

| Tool | What it does |
| --- | --- |
| **Markdown** | Browse folders of `.md` files, preview, edit, split view, task checkboxes, code highlighting, search |
| **JSON** | Live validation, format / minify / sort keys, **repair** broken JSON, tree view, JSONPath queries, C# / Kotlin / TypeScript types, YAML ⇄ JSON |
| **Diff** | Side-by-side and inline compare, word-level changes, ignore whitespace/case, JSON mode, overview map, copy as patch |
| **Regex** | .NET regex tester with live highlights, groups, replace preview, C# / JavaScript code |
| **Timestamp** | Unix seconds/ms/µs/ns ⇄ ISO 8601, time zones, time between dates |
| **Text** | Case conversion, sort/dedupe/trim lines, word and character counts |
| **Generators** | Passwords, secret keys, UUID v4/v7, Lorem ipsum, fake test data, QR codes |
| **Encode / Decode** | Base64, URL, HTML, hex, Unicode, image ⇄ Base64, JWT decoding, hashes and HMAC |

Press **Ctrl+K** to search tools and actions.

## Download

Get the latest build from [Releases](../../releases):

- **Windows (x64):** unzip and run `DeveloperTools.exe`.
- **Linux (x64):** extract, then `./install.sh` to add it to your app menu, or run `./DeveloperTools`.
- **Browser:** unzip `…-web.zip` onto any static web host. Links like `/#json` or `/#diff` open a tool directly.

The desktop builds are compiled to native code (no .NET runtime needed, starts in about 0.2 s).

### Browser version differences

- Markdown opens and saves individual files (no folder browsing). Chrome and Edge save back to the same file; other browsers download a copy.
- Settings and snippets are kept in the browser's local storage for the site.
- MD5 is not available (not supported by .NET in WebAssembly).

## Build

Requires the .NET 11 SDK.

```sh
dotnet run --project src/DeveloperTools.Desktop                       # run the desktop app
dotnet publish src/DeveloperTools.Desktop -c Release -r win-x64        # native AOT (build on the target OS)

dotnet workload install wasm-tools                                    # once, for the browser version
dotnet publish src/DeveloperTools.Browser -c Release                  # static site in publish/wwwroot
```

`DeveloperTools.exe --selftest log.txt` opens every tool and checks it; `#selftest` does the same in the browser.

## Projects

| Project | |
| --- | --- |
| `src/DeveloperTools` (`DeveloperTools.Core`) | All tools, views and view models (Avalonia) |
| `src/DeveloperTools.Desktop` | Windows / Linux app |
| `src/DeveloperTools.Browser` | WebAssembly app |

## Releases

Push a version tag to build and publish all three downloads:

```sh
git tag v1.2.0
git push origin v1.2.0
```

## Licenses

The bundled Cascadia Mono font is © Microsoft, under the SIL Open Font License 1.1 (`src/DeveloperTools/Assets/Fonts/CascadiaMono-LICENSE.txt`).
