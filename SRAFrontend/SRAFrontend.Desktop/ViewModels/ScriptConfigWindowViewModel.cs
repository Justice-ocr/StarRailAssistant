using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SRAFrontend.Models;

namespace SRAFrontend.Desktop.ViewModels;

public partial class ScriptParamValueViewModel : ObservableObject
{
    public ScriptParamDef Def { get; }

    public string Key => Def.Key;
    public string Label => !string.IsNullOrEmpty(Def.Label) ? Def.Label : Def.Key;
    public string Description => Def.Description ?? "";
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    public string DefaultValue => Def.Default ?? "";
    public List<string> Options => Def.Options ?? [];

    public bool IsText { get; }
    public bool IsFolder { get; }
    public bool IsBool { get; }
    public bool IsSelect { get; }

    [ObservableProperty] private string _value = "";
    [ObservableProperty] private bool _boolValue;

    public ScriptParamValueViewModel(ScriptParamDef def, string currentValue)
    {
        Def = def;
        IsText = def.Type is "string" or "int" or "number" or "folder" or null or "";
        IsFolder = def.Type == "folder";
        IsBool = def.Type == "bool";
        IsSelect = def.Type == "select";

        BrowseFolderCommand = new RelayCommand(async () =>
        {
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;
            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "选择文件夹",
                    AllowMultiple = false
                });
            if (folders.Count > 0)
                Value = folders[0].Path.LocalPath;
        });

        if (IsBool)
        {
            _boolValue = currentValue is "true" or "True" or "1";
            _value = _boolValue.ToString().ToLowerInvariant();
        }
        else
        {
            _value = string.IsNullOrEmpty(currentValue) ? (def.Default ?? "") : currentValue;
        }
    }

    partial void OnBoolValueChanged(bool value) => Value = value.ToString().ToLowerInvariant();

    public string GetSaveValue() => IsBool ? BoolValue.ToString().ToLowerInvariant() : Value;

    public ICommand BrowseFolderCommand { get; }
}

public partial class ScriptParamGroupViewModel : ObservableObject
{
    public string GroupName { get; }
    public bool HasGroupName => !string.IsNullOrEmpty(GroupName);
    public AvaloniaList<ScriptParamValueViewModel> Params { get; } = [];

    public ScriptParamGroupViewModel(string groupName = "")
    {
        GroupName = groupName;
    }
}

public partial class ScriptConfigWindowViewModel : ObservableObject
{
    private readonly string _configDir;

    public string Title { get; }
    public bool HasParams { get; }
    public string NoParamsMessage { get; private set; } =
        "未找到 settings.json，此脚本没有可配置参数。";

    public AvaloniaList<ScriptParamGroupViewModel> ParamGroups { get; } = [];

    public ScriptConfigWindowViewModel(
        string scriptId,
        string configDir,
        List<ScriptParamDef> paramDefs)
    {
        _configDir = configDir;
        Title = $"编辑脚本配置 - {scriptId}";

        if (paramDefs.Count == 0)
        {
            HasParams = false;
            NoParamsMessage =
                "未找到 settings.json。你可以在脚本目录中手动创建 config.json。";
            return;
        }

        HasParams = true;
        var existing = LoadExistingConfig();

        ScriptParamGroupViewModel? currentGroup = null;
        foreach (var def in paramDefs)
        {
            if (def.Type == "group")
            {
                currentGroup = new ScriptParamGroupViewModel(def.Label ?? def.Key);
                ParamGroups.Add(currentGroup);
                continue;
            }

            currentGroup ??= new ScriptParamGroupViewModel("");
            if (!ParamGroups.Contains(currentGroup))
                ParamGroups.Add(currentGroup);

            existing.TryGetValue(def.Key, out var savedValue);
            currentGroup.Params.Add(new ScriptParamValueViewModel(def, savedValue ?? ""));
        }
    }

    private Dictionary<string, string> LoadExistingConfig()
    {
        var path = Path.Combine(_configDir, "config.json");
        if (!File.Exists(path)) return [];

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path, System.Text.Encoding.UTF8)) as JsonObject;
            if (root == null) return [];

            var result = new Dictionary<string, string>();
            foreach (var item in root)
                result[item.Key] = item.Value?.ToString() ?? "";
            return result;
        }
        catch
        {
            return [];
        }
    }

    [RelayCommand]
    private void Save()
    {
        var path = Path.Combine(_configDir, "config.json");
        JsonObject root;

        if (File.Exists(path))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllText(path, System.Text.Encoding.UTF8)) as JsonObject
                       ?? new JsonObject();
            }
            catch
            {
                root = new JsonObject();
            }
        }
        else
        {
            Directory.CreateDirectory(_configDir);
            root = new JsonObject();
        }

        foreach (var group in ParamGroups)
        foreach (var param in group.Params)
            root[param.Key] = JsonValue.Create(param.GetSaveValue());

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (Directory.Exists(_configDir))
            System.Diagnostics.Process.Start("explorer.exe", _configDir);
    }
}
