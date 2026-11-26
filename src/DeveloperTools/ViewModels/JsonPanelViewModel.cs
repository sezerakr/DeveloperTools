using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeveloperTools.Services;
using DeveloperTools.Tools;

using DeveloperTools.Localization;

namespace DeveloperTools.ViewModels;

public enum JsonPanelTab
{
    Tree,
    Query,
    Code,
    Yaml
}

/// <summary>One node in the JSON tree; children are built when first needed.</summary>
public partial class JsonTreeNode : ObservableObject
{
    private const int MaxChildren = 500;
    private readonly JsonNode? _node;
    private IReadOnlyList<JsonTreeNode>? _children;

    private readonly int _depth;

    public JsonTreeNode(string key, JsonNode? node, string path, bool expanded = false, int depth = 0)
    {
        _depth = depth;
        Key = key;
        _node = node;
        Path = path;
        _isExpanded = expanded;
        (Kind, Preview) = node switch
        {
            JsonObject o => ("object", $"{{{o.Count}}}"),
            JsonArray a => ("array", $"[{a.Count}]"),
            null => ("null", "null"),
            JsonValue v => v.GetValueKind() switch
            {
                System.Text.Json.JsonValueKind.String => ("string", Trim(v.ToJsonString())),
                System.Text.Json.JsonValueKind.Number => ("number", v.ToJsonString()),
                System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => ("bool", v.ToJsonString()),
                _ => ("null", "null")
            },
            _ => ("null", "null")
        };
    }

    private JsonTreeNode(string text) : this(text, null, "")
    {
        Kind = "more";
        Preview = "";
    }

    private static string Trim(string s) => s.Length > 120 ? s[..118] + "…\"" : s;

    public string Key { get; }
    public string Path { get; }
    public string Kind { get; }
    public string Preview { get; }
    public bool IsContainer => Kind is "object" or "array";

    [ObservableProperty] private bool _isExpanded;

    public IReadOnlyList<JsonTreeNode> Children => _children ??= BuildChildren();

    private IReadOnlyList<JsonTreeNode> BuildChildren()
    {
        var list = new List<JsonTreeNode>();
        switch (_node)
        {
            case JsonObject o:
                foreach (var (k, v) in o.Take(MaxChildren))
                    list.Add(new JsonTreeNode(k, v, Path + JsonPathKey(k), expanded: _depth < 1, depth: _depth + 1));
                if (o.Count > MaxChildren) list.Add(new JsonTreeNode(Loc.F("… {0:N0} more keys", o.Count - MaxChildren)));
                break;
            case JsonArray a:
                for (var i = 0; i < Math.Min(a.Count, MaxChildren); i++)
                    list.Add(new JsonTreeNode($"[{i}]", a[i], $"{Path}[{i}]", expanded: _depth < 1 && a.Count <= 20, depth: _depth + 1));
                if (a.Count > MaxChildren) list.Add(new JsonTreeNode(Loc.F("… {0:N0} more items", a.Count - MaxChildren)));
                break;
        }
        return list;
    }

    private static string JsonPathKey(string k) =>
        k.Length > 0 && !char.IsDigit(k[0]) && k.All(c => char.IsLetterOrDigit(c) || c == '_') ? "." + k : "['" + k.Replace("'", "\\'") + "']";
}

/// <summary>Tree, JSONPath query, code generation and YAML for the JSON page.</summary>
public partial class JsonPanelViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly Func<string> _text;
    private int _version;

    public JsonPanelViewModel(IDialogService dialogs, Func<string> text)
    {
        _dialogs = dialogs;
        _text = text;
    }

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private JsonPanelTab _tab = JsonPanelTab.Tree;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _message;

    public bool HasMessage => Message is not null;

    partial void OnIsOpenChanged(bool value) => Refresh();
    partial void OnTabChanged(JsonPanelTab value) => Refresh();

    // tree
    public ObservableCollection<JsonTreeNode> TreeRoots { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPath), nameof(HasSelection))]
    private JsonTreeNode? _selectedNode;

    public string SelectedPath => SelectedNode?.Path ?? "";
    public bool HasSelection => SelectedNode is { Kind: not "more" };

    [RelayCommand]
    private Task CopyPath() => _dialogs.CopyToClipboardAsync(SelectedPath);

    [RelayCommand]
    private void UsePathInQuery()
    {
        if (!HasSelection) return;
        Query = SelectedPath;
        Tab = JsonPanelTab.Query;
    }

    // query
    [ObservableProperty] private string _query = "$";
    [ObservableProperty] private bool _showPaths;
    [ObservableProperty] private string _queryOutput = "";
    [ObservableProperty] private string _queryInfo = "";

    partial void OnQueryChanged(string value) => Refresh();
    partial void OnShowPathsChanged(bool value) => Refresh();

    // code
    [ObservableProperty] private CodeTarget _codeTarget = CodeTarget.CSharp;
    [ObservableProperty] private string _rootName = "Root";
    [ObservableProperty] private string _codeOutput = "";

    public string CodeLanguage => CodeTarget switch { CodeTarget.Kotlin => "kotlin", CodeTarget.TypeScript => "ts", _ => "cs" };

    partial void OnCodeTargetChanged(CodeTarget value)
    {
        OnPropertyChanged(nameof(CodeLanguage));
        Refresh();
    }

    partial void OnRootNameChanged(string value) => Refresh();

    // yaml
    [ObservableProperty] private string _yamlOutput = "";

    [RelayCommand]
    private Task CopyOutput() => _dialogs.CopyToClipboardAsync(Tab switch
    {
        JsonPanelTab.Query => QueryOutput,
        JsonPanelTab.Code => CodeOutput,
        JsonPanelTab.Yaml => YamlOutput,
        _ => SelectedPath
    });

    /// <summary>Called when the JSON changes (after validation) or panel inputs change.</summary>
    public async void Refresh()
    {
        if (!IsOpen)
            return;
        var version = ++_version;
        var text = _text();
        var tab = Tab;
        var query = Query;
        var showPaths = ShowPaths;
        var target = CodeTarget;
        var root = string.IsNullOrWhiteSpace(RootName) ? "Root" : RootName.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            Message = Loc.T("Paste or type JSON in the editor.");
            TreeRoots.Clear();
            return;
        }

        try
        {
            var result = await Task.Run<object>(() =>
            {
                switch (tab)
                {
                    case JsonPanelTab.Tree:
                        return new JsonTreeNode("$", JsonPath.Parse(text), "$", expanded: true);
                    case JsonPanelTab.Query:
                    {
                        var matches = JsonPath.Evaluate(JsonPath.Parse(text), query);
                        var array = new JsonArray(matches.Select(m => showPaths ? JsonValue.Create(m.Path) : m.Value?.DeepClone()).ToArray());
                        return (JsonTools.Format(array.ToJsonString(), "  "), matches.Count);
                    }
                    case JsonPanelTab.Code:
                        return JsonCodeGen.Generate(text, target, root);
                    default:
                        return YamlTools.JsonToYaml(text);
                }
            });

            if (version != _version)
                return;
            Message = null;
            switch (result)
            {
                case JsonTreeNode node:
                    TreeRoots.Clear();
                    TreeRoots.Add(node);
                    break;
                case ValueTuple<string, int> q:
                    QueryOutput = q.Item1;
                    QueryInfo = q.Item2 switch { 0 => Loc.T("No matches"), 1 => Loc.T("1 match"), _ => Loc.F("{0:N0} matches", q.Item2) };
                    break;
                case string s when tab == JsonPanelTab.Code:
                    CodeOutput = s;
                    break;
                case string s:
                    YamlOutput = s;
                    break;
            }
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {
            if (version != _version)
                return;
            if (tab == JsonPanelTab.Query && ex is FormatException)
            {
                QueryInfo = ex.Message;
                QueryOutput = "";
                Message = null;
            }
            else
            {
                Message = ex is System.Text.Json.JsonException
                    ? "The JSON has an error. Fix it (or use Repair) to see this panel."
                    : ex.Message;
            }
        }
    }
}
