using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace SRAFrontend.Desktop.ViewModels;

/// <summary>
/// 鑴氭湰 config.json 鐨勯敭鍊肩紪杈戝櫒 ViewModel銆?
/// 渚?CustomTaskView 涓€岃剼鏈厤缃€嶅尯鍩熺粦瀹氫娇鐢ㄣ€?
/// </summary>
public partial class ScriptConfigEditorViewModel : ObservableObject
{
    private readonly ILogger<ScriptConfigEditorViewModel> _logger;
    private string _scriptId = "";
    private string _configPath = "";

    [ObservableProperty] private string _rawJson = "";
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _isLoaded;

    public ScriptConfigEditorViewModel(ILogger<ScriptConfigEditorViewModel> logger)
    {
        _logger = logger;
    }

    /// <summary>鍔犺浇鎸囧畾鑴氭湰鐨?config.json锛堜笉瀛樺湪鍒欐樉绀虹┖锛?/summary>
    public void Load(string scriptId)
    {
        _scriptId = scriptId;
        if (string.IsNullOrEmpty(scriptId))
        {
            RawJson = "";
            IsLoaded = false;
            return;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var scriptDir = Path.Combine(appData, "SRA", "scripts", scriptId);
        _configPath = Path.Combine(scriptDir, "config.json");

        try
        {
            if (File.Exists(_configPath))
            {
                var text = File.ReadAllText(_configPath);
                // 鏍煎紡鍖?JSON 浠ヤ究闃呰
                var node = JsonNode.Parse(text);
                RawJson = node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? text;
            }
            else
            {
                RawJson = "{}";
            }
            HasError = false;
            ErrorMessage = "";
            IsLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "鍔犺浇鑴氭湰閰嶇疆澶辫触: {Path}", _configPath);
            RawJson = "{}";
            HasError = true;
            ErrorMessage = $"鍔犺浇澶辫触: {ex.Message}";
            IsLoaded = true;
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrEmpty(_configPath)) return;
        try
        {
            // 楠岃瘉 JSON 鍚堟硶鎬?
            JsonNode.Parse(RawJson);
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            File.WriteAllText(_configPath, RawJson);
            HasError = false;
            ErrorMessage = "";
        }
        catch (JsonException ex)
        {
            HasError = true;
            ErrorMessage = $"JSON 鏍煎紡閿欒: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "淇濆瓨鑴氭湰閰嶇疆澶辫触: {Path}", _configPath);
            HasError = true;
            ErrorMessage = $"淇濆瓨澶辫触: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", dir);
    }
}
