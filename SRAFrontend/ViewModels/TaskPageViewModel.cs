using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SRAFrontend.Controls;
using SRAFrontend.Data;
using SRAFrontend.Models;
using SRAFrontend.Services;
using SukiUI.Controls;
using SukiUI.MessageBox;
using System;

namespace SRAFrontend.ViewModels;

public partial class TaskOrderItem : ObservableObject
{
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isSelected;
    public string ClassName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsFixed { get; set; } = false;
    public bool IsMovable => !IsFixed;
    public bool IsCustom { get; set; } = false;
    public bool IsAddButton { get; set; } = false;
    public bool IsFixedTab => IsFixed && !IsAddButton;
    public int OriginalIndex { get; set; } = -1;
}

public partial class ScriptParamViewModel : ObservableObject
{
    private readonly Action<string, string> _onChanged;
    public ScriptParamDef Def { get; }
    [ObservableProperty] private string _value = "";
    public ScriptParamViewModel(ScriptParamDef def, string currentValue, Action<string, string> onChanged)
    {
        Def = def;
        _value = currentValue;
        _onChanged = onChanged;
    }
    partial void OnValueChanged(string value) => _onChanged(Def.Key, value);
}

public partial class TaskPageViewModel : PageViewModel
{
    private readonly CacheService _cacheService;
    private readonly CommonModel _commonModel;
    private readonly ConfigService _configService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CosmicStrifeConfig), nameof(MissionAccomplishedConfig),
        nameof(ReceiveRewardsConfig), nameof(StartGameConfig), nameof(TrailblazePowerConfig))]
    private TasksConfig _currentConfig;

    [ObservableProperty] private bool _isTpTaskAutoDetect;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(EnableContextMenu))]
    private object? _selectedTaskItem;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CurrentTpTaskLevels), nameof(CurrentTpTaskMaxSingleTimes))]
    private int _selectedTpTaskIndex;

    [ObservableProperty] private int _selectedTpTaskLevelIndex;
    [ObservableProperty] private int _tpTaskRunTimes = 1;
    [ObservableProperty] private int _tpTaskSingleTimes = 1;

    [ObservableProperty] private AvaloniaList<TaskOrderItem> _taskOrderList = [];

    private static readonly string FixedFirstTask = "StartGameTask";
    private static readonly string FixedLastTask  = "MissionAccomplishTask";

    private static readonly List<(string ClassName, string DisplayName)> AllTaskDefs =
    [
        ("StartGameTask",         "启动游戏"),
        ("TrailblazePowerTask",   "清开拓力"),
        ("ReceiveRewardsTask",    "领取奖励"),
        ("CosmicStrifeTask",      "旷宇纷争"),
        ("MissionAccomplishTask", "任务完成"),
    ];

    private SRAFrontend.Services.ScriptService _scriptService = null!;
    private string _selectedClassName = "StartGameTask";

    public System.Collections.ObjectModel.ObservableCollection<SRAFrontend.Models.ScriptManifest> InstalledScripts { get; } = new();
    public AvaloniaList<ScriptParamViewModel> ScriptParams { get; } = new();
    public bool HasScriptParams => ScriptParams.Count > 0;
    public ScriptConfigEditorViewModel ScriptConfigEditor { get; }

    public TaskPageViewModel(
        CommonModel commonModel,
        ControlPanelViewModel controlPanelViewModel,
        ConfigService configService,
        CacheService cacheService,
        SRAFrontend.Services.ScriptService scriptService,
        ILogger<ScriptConfigEditorViewModel> configEditorLogger) : base(PageName.Task, "\uE1BC")
    {
        ControlPanelViewModel = controlPanelViewModel;
        _commonModel = commonModel;
        _configService = configService;
        _cacheService = cacheService;
        _scriptService = scriptService;
        ScriptConfigEditor = new ScriptConfigEditorViewModel(configEditorLogger);
        CurrentConfig = _configService.TaskConfig!;

        void OnCachePropertyChanged(object? _, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(Cache.CurrentConfigIndex)) return;
            _configService.SwitchConfig(_cacheService.Cache.ConfigNames[_cacheService.Cache.CurrentConfigIndex]);
            CurrentConfig = _configService.TaskConfig!;
            InitTaskOrderList();
        }

        _cacheService.Cache.PropertyChanged += OnCachePropertyChanged;
        if (Cache.Strategies.Count == 0) RefreshStrategies();
        InitTaskOrderList();
    }

    // ===== TrailblazePower 新 UI =====
    public string[] TpTaskNames => TpTaskItems.TpTaskNames;
    public string[] CurrentTpTaskLevels => TpTaskItems.GetLevelsByIndex(SelectedTpTaskIndex);
    public string[] GardenOfPlentyLevels1 => TpTaskItems.GetLevelsByIndex(1);
    public string[] GardenOfPlentyLevels2 => TpTaskItems.GetLevelsByIndex(2);
    public string[] PlanarFissureLevels => TpTaskItems.GetLevelsByIndex(0);
    public string[] RealmOfTheStrangeLevels => TpTaskItems.GetLevelsByIndex(4);
    public int CurrentTpTaskMaxSingleTimes => TpTaskItems.GetMaxSingleTimesByIndex(SelectedTpTaskIndex);

    public string TaskListText =>
        TrailblazePowerConfig.TaskList.Count == 0
            ? "暂无任务"
            : $"{string.Join("、", TrailblazePowerConfig.TaskList.Select(x => x.Name).Take(3))} 等 {TrailblazePowerConfig.TaskList.Count} 个任务";

    // ===== 子配置属性 =====
    public CosmicStrifeConfig CosmicStrifeConfig => CurrentConfig.CosmicStrife;
    public MissionAccomplishedConfig MissionAccomplishedConfig => CurrentConfig.MissionAccomplished;
    public ReceiveRewardsConfig ReceiveRewardsConfig => CurrentConfig.ReceiveRewards;
    public StartGameConfig StartGameConfig => CurrentConfig.StartGame;
    public TrailblazePowerConfig TrailblazePowerConfig => CurrentConfig.TrailblazePower;

    public int CurrencyWarsStrategyIndex
    {
        get => CosmicStrifeConfig.CurrencyWarsStrategyIndex;
        set
        {
            CosmicStrifeConfig.CurrencyWarsStrategyIndex = value;
            OnPropertyChanged();
            CosmicStrifeConfig.CurrencyWarsStrategy = Cache.Strategies.ElementAtOrDefault(value)?.FileName ?? "";
        }
    }

    public ControlPanelViewModel ControlPanelViewModel { get; }
    public TopLevel? TopLevelObject { get; set; }
    public bool EnableContextMenu => SelectedTaskItem is not null;

    public int CurrencyWarsModeIndex
    {
        get => CosmicStrifeConfig.CurrencyWarsMode;
        set
        {
            CosmicStrifeConfig.CurrencyWarsMode = value;
            OnPropertyChanged(nameof(IsCwNormalMode));
        }
    }

    public bool IsCwNormalMode => CosmicStrifeConfig.CurrencyWarsMode != 2;
    public Cache Cache => _cacheService.Cache;

    // ===== 任务排序列表 =====
    private void InitTaskOrderList()
    {
        TaskOrderList.Clear();
        var middleDefs = AllTaskDefs.Where(d => d.ClassName != FixedFirstTask && d.ClassName != FixedLastTask).ToList();
        var firstDef = AllTaskDefs.First(d => d.ClassName == FixedFirstTask);
        var lastDef  = AllTaskDefs.First(d => d.ClassName == FixedLastTask);

        List<(string ClassName, string DisplayName, bool Enabled)> middleItems;
        if (CurrentConfig.TaskOrder.Count > 0)
        {
            var enabledMiddle = CurrentConfig.TaskOrder
                .Where(c => c != FixedFirstTask && c != FixedLastTask && c != "__add__")
                .Select(c =>
                {
                    if (c.StartsWith("CustomTask_"))
                    {
                        var id = c.Replace("CustomTask_", "");
                        var entry = CurrentConfig.CustomTasks.FirstOrDefault(e => e.Id == id);
                        return (c, entry?.Name ?? "自定义任务", true);
                    }
                    return (c, AllTaskDefs.FirstOrDefault(d => d.ClassName == c).DisplayName, true);
                })
                .Where(t => !string.IsNullOrEmpty(t.Item2))
                .ToList();
            var enabledSet = new HashSet<string>(enabledMiddle.Select(t => t.c));
            var disabledMiddle = middleDefs.Where(d => !enabledSet.Contains(d.ClassName))
                .Select(d => (d.ClassName, d.DisplayName, false)).ToList();
            middleItems = enabledMiddle.Concat(disabledMiddle).ToList();
        }
        else
        {
            middleItems = middleDefs.Select((d, i) =>
            {
                int origIdx = AllTaskDefs.FindIndex(x => x.ClassName == d.ClassName);
                bool enabled = origIdx >= 0 && origIdx < CurrentConfig.EnabledTasks.Count && CurrentConfig.EnabledTasks[origIdx];
                return (d.ClassName, d.DisplayName, enabled);
            }).ToList();
        }

        bool firstEnabled = CurrentConfig.TaskOrder.Count > 0
            ? CurrentConfig.TaskOrder.Contains(FixedFirstTask)
            : (0 < CurrentConfig.EnabledTasks.Count && CurrentConfig.EnabledTasks[0]);
        TaskOrderList.Add(new TaskOrderItem { ClassName = firstDef.ClassName, DisplayName = firstDef.DisplayName, IsEnabled = firstEnabled, IsFixed = true, OriginalIndex = 0 });

        foreach (var (className, displayName, enabled) in middleItems)
            TaskOrderList.Add(new TaskOrderItem { ClassName = className, DisplayName = displayName, IsEnabled = enabled, IsFixed = false, IsCustom = className.StartsWith("CustomTask_"), OriginalIndex = AllTaskDefs.FindIndex(d => d.ClassName == className) });

        bool lastEnabled = CurrentConfig.TaskOrder.Count > 0
            ? CurrentConfig.TaskOrder.Contains(FixedLastTask)
            : (4 < CurrentConfig.EnabledTasks.Count && CurrentConfig.EnabledTasks[4]);
        TaskOrderList.Add(new TaskOrderItem { ClassName = lastDef.ClassName, DisplayName = lastDef.DisplayName, IsEnabled = lastEnabled, IsFixed = true, OriginalIndex = 4 });
        TaskOrderList.Add(new TaskOrderItem { ClassName = "__add__", DisplayName = "+", IsFixed = true, IsAddButton = true });

        foreach (var item in TaskOrderList)
            item.PropertyChanged += (_, _) => SyncTaskOrderToConfig();
        SyncTaskOrderToConfig();
        if (TaskOrderList.Count > 0) SelectTask(TaskOrderList[0].ClassName);
    }

    public TaskOrderItem? GetTaskItem(string className) => TaskOrderList.FirstOrDefault(t => t.ClassName == className);

    private void SyncTaskOrderToConfig()
    {
        CurrentConfig.TaskOrder.Clear();
        foreach (var item in TaskOrderList) CurrentConfig.TaskOrder.Add(item.ClassName);
    }

    public void SelectTask(string className)
    {
        _selectedClassName = className;
        foreach (var item in TaskOrderList) item.IsSelected = item.ClassName == className;
        OnPropertyChanged(nameof(StartGameTaskSelected));
        OnPropertyChanged(nameof(TrailblazePowerTaskSelected));
        OnPropertyChanged(nameof(ReceiveRewardsTaskSelected));
        OnPropertyChanged(nameof(CosmicStrifeTaskSelected));
        OnPropertyChanged(nameof(MissionAccomplishTaskSelected));
        OnPropertyChanged(nameof(CustomTaskSelected));
        OnPropertyChanged(nameof(SelectedCustomTask));
        if (CustomTaskSelected) RefreshInstalledScripts();
    }

    public bool CustomTaskSelected => _selectedClassName.StartsWith("CustomTask_");
    public bool StartGameTaskSelected         => _selectedClassName == "StartGameTask";
    public bool TrailblazePowerTaskSelected   => _selectedClassName == "TrailblazePowerTask";
    public bool ReceiveRewardsTaskSelected    => _selectedClassName == "ReceiveRewardsTask";
    public bool CosmicStrifeTaskSelected      => _selectedClassName == "CosmicStrifeTask";
    public bool MissionAccomplishTaskSelected => _selectedClassName == "MissionAccomplishTask";

    public bool StartGameTaskEnabled         { get => GetTaskItem("StartGameTask")?.IsEnabled ?? false; set { var t = GetTaskItem("StartGameTask"); if (t != null) t.IsEnabled = value; } }
    public bool TrailblazePowerTaskEnabled   { get => GetTaskItem("TrailblazePowerTask")?.IsEnabled ?? false; set { var t = GetTaskItem("TrailblazePowerTask"); if (t != null) t.IsEnabled = value; } }
    public bool ReceiveRewardsTaskEnabled    { get => GetTaskItem("ReceiveRewardsTask")?.IsEnabled ?? false; set { var t = GetTaskItem("ReceiveRewardsTask"); if (t != null) t.IsEnabled = value; } }
    public bool CosmicStrifeTaskEnabled      { get => GetTaskItem("CosmicStrifeTask")?.IsEnabled ?? false; set { var t = GetTaskItem("CosmicStrifeTask"); if (t != null) t.IsEnabled = value; } }
    public bool MissionAccomplishTaskEnabled { get => GetTaskItem("MissionAccomplishTask")?.IsEnabled ?? false; set { var t = GetTaskItem("MissionAccomplishTask"); if (t != null) t.IsEnabled = value; } }

    public SRAFrontend.Models.CustomTaskEntry? SelectedCustomTask
        => _selectedClassName.StartsWith("CustomTask_")
            ? CurrentConfig.CustomTasks.FirstOrDefault(t => "CustomTask_" + t.Id == _selectedClassName)
            : null;

    private SRAFrontend.Models.ScriptManifest? _selectedInstalledScript;
    public SRAFrontend.Models.ScriptManifest? SelectedInstalledScript
    {
        get => _selectedInstalledScript;
        set
        {
            _selectedInstalledScript = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedScriptTasks));
            OnPropertyChanged(nameof(SelectedScriptHasMultipleTasks));
            SelectedScriptTask = value?.Tasks.FirstOrDefault();
            if (value != null && value.Tasks.Count == 1) ApplyScriptSelection(value, value.Tasks[0]);
        }
    }

    public List<SRAFrontend.Models.ScriptTaskDef> SelectedScriptTasks => _selectedInstalledScript?.Tasks ?? [];
    public bool SelectedScriptHasMultipleTasks => (_selectedInstalledScript?.Tasks.Count ?? 0) > 1;

    private SRAFrontend.Models.ScriptTaskDef? _selectedScriptTask;
    public SRAFrontend.Models.ScriptTaskDef? SelectedScriptTask
    {
        get => _selectedScriptTask;
        set { _selectedScriptTask = value; OnPropertyChanged(); if (_selectedInstalledScript != null && value != null) ApplyScriptSelection(_selectedInstalledScript, value); }
    }

    private void ApplyScriptSelection(SRAFrontend.Models.ScriptManifest script, SRAFrontend.Models.ScriptTaskDef task)
    {
        if (SelectedCustomTask == null) return;
        SelectedCustomTask.ScriptId = script.Id;
        SelectedCustomTask.TaskEntry = task.Entry;
        SelectedCustomTask.TaskClassName = task.Class;
        if (SelectedCustomTask.Name.StartsWith("自定义任务")) SelectedCustomTask.Name = task.Name;
        var item = TaskOrderList.FirstOrDefault(t => t.ClassName == "CustomTask_" + SelectedCustomTask.Id);
        if (item != null) item.DisplayName = SelectedCustomTask.Name;
        SelectedCustomTask.ScriptPath = "";
        OnPropertyChanged(nameof(SelectedCustomTask));
        SyncTaskOrderToConfig();
        ScriptParams.Clear();
        var paramDefs = script.LoadedParams.Count > 0 ? script.LoadedParams : task.Params;
        foreach (var p in paramDefs)
        {
            if (SelectedCustomTask.Params == null) SelectedCustomTask.Params = new Dictionary<string, string>();
            if (!SelectedCustomTask.Params.ContainsKey(p.Key)) SelectedCustomTask.Params[p.Key] = p.Default ?? "";
            var vm = new ScriptParamViewModel(p, SelectedCustomTask.Params.GetValueOrDefault(p.Key, ""),
                (key, val) => { if (SelectedCustomTask.Params == null) SelectedCustomTask.Params = new Dictionary<string, string>(); SelectedCustomTask.Params[key] = val; _configService.Save(); });
            ScriptParams.Add(vm);
        }
        OnPropertyChanged(nameof(HasScriptParams));
        ScriptConfigEditor.Load(script.Id);
    }

    [RelayCommand]
    public void RefreshInstalledScripts()
    {
        InstalledScripts.Clear();
        foreach (var s in _scriptService.GetInstalledScripts()) InstalledScripts.Add(s);
        if (SelectedCustomTask != null && !string.IsNullOrEmpty(SelectedCustomTask.ScriptId))
        {
            _selectedInstalledScript = InstalledScripts.FirstOrDefault(s => s.Id == SelectedCustomTask.ScriptId);
            OnPropertyChanged(nameof(SelectedInstalledScript));
            OnPropertyChanged(nameof(SelectedScriptTasks));
            OnPropertyChanged(nameof(SelectedScriptHasMultipleTasks));
            if (_selectedInstalledScript != null)
                _selectedScriptTask = _selectedInstalledScript.Tasks.FirstOrDefault(t => t.Entry == SelectedCustomTask.TaskEntry);
            OnPropertyChanged(nameof(SelectedScriptTask));
        }
    }

    [RelayCommand]
    private void AddCustomTask()
    {
        RefreshInstalledScripts();
        var entry = new SRAFrontend.Models.CustomTaskEntry
        {
            Name = $"自定义任务 {CurrentConfig.CustomTasks.Count + 1}",
            ScriptId = "", TaskEntry = "", TaskClassName = "", ScriptPath = "", IsEnabled = true
        };
        CurrentConfig.CustomTasks.Add(entry);
        var className = "CustomTask_" + entry.Id;
        var newItem = new TaskOrderItem { ClassName = className, DisplayName = entry.Name, IsEnabled = true, IsFixed = false, IsCustom = true, OriginalIndex = -1 };
        newItem.PropertyChanged += (_, _) => SyncTaskOrderToConfig();
        var lastFixed = TaskOrderList.FirstOrDefault(t => t.ClassName == FixedLastTask);
        var insertPos = lastFixed != null ? TaskOrderList.IndexOf(lastFixed) : TaskOrderList.Count;
        TaskOrderList.Insert(insertPos, newItem);
        SyncTaskOrderToConfig();
        SelectTask(className);
    }

    [RelayCommand]
    private void RemoveCustomTask()
    {
        if (SelectedCustomTask == null) return;
        var className = "CustomTask_" + SelectedCustomTask.Id;
        var item = TaskOrderList.FirstOrDefault(t => t.ClassName == className);
        if (item != null) TaskOrderList.Remove(item);
        CurrentConfig.CustomTasks.Remove(SelectedCustomTask);
        SyncTaskOrderToConfig();
        if (TaskOrderList.Count > 0) SelectTask(TaskOrderList[0].ClassName);
    }

    public void MoveTaskToIndex(TaskOrderItem item, int targetIndex)
    {
        if (item.IsFixed) return;
        var idx = TaskOrderList.IndexOf(item);
        if (idx < 0 || idx == targetIndex) return;
        if (targetIndex < 0 || targetIndex >= TaskOrderList.Count) return;
        var target = TaskOrderList[targetIndex];
        if (target.IsFixed) return;
        TaskOrderList.RemoveAt(idx);
        TaskOrderList.Insert(targetIndex, item);
        item.PropertyChanged += (_, _) => SyncTaskOrderToConfig();
        SyncTaskOrderToConfig();
    }

    [RelayCommand]
    private void OpenScriptConfig()
    {
        if (SelectedCustomTask == null || string.IsNullOrEmpty(SelectedCustomTask.ScriptId)) return;
        var scriptId = SelectedCustomTask.ScriptId;
        var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
        var configDir = System.IO.Path.Combine(appData, "SRA", "scripts", scriptId);
        var paramDefs = new List<SRAFrontend.Models.ScriptParamDef>();
        var settingsPath = System.IO.Path.Combine(configDir, "settings.json");
        bool hasSettingsFile = System.IO.File.Exists(settingsPath);
        var manifest = InstalledScripts.FirstOrDefault(s => s.Id == scriptId);
        if (manifest != null)
        {
            if (manifest.LoadedParams.Count > 0) paramDefs.AddRange(manifest.LoadedParams);
            else if (!hasSettingsFile) foreach (var task in manifest.Tasks) paramDefs.AddRange(task.Params);
        }
        var vm = new ScriptConfigWindowViewModel(scriptId, configDir, paramDefs);
        var win = new SRAFrontend.Views.ScriptConfigWindow { DataContext = vm };
        win.Show();
    }

    [RelayCommand]
    private async Task SelectCustomTaskScript()
    {
        if (SelectedCustomTask == null || TopLevelObject == null) return;
        var files = await TopLevelObject.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Python 脚本文件",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Python 脚本") { Patterns = ["*.py"] }]
        });
        if (files.Count == 0) return;
        SelectedCustomTask.ScriptPath = files[0].Path.LocalPath;
        OnPropertyChanged(nameof(SelectedCustomTask));
    }

    [RelayCommand]
    private void SingleCustomTask()
    {
        if (SelectedCustomTask == null) return;
        SingleTask("CustomTask_" + SelectedCustomTask.Id);
    }

    // ===== TrailblazePower 命令 =====
    [RelayCommand]
    private void SingleTask(string taskName) => ControlPanelViewModel.StartSingleTask(taskName);

    [RelayCommand]
    private void RefreshStrategies()
    {
        if (!Directory.Exists(PathString.StrategiesDir)) { _commonModel.ShowErrorToast("Error", "未找到攻略文件夹，无法刷新"); return; }
        var strategies = new List<Strategy>();
        foreach (var file in Directory.GetFiles(PathString.StrategiesDir))
        {
            if (!file.EndsWith(".json")) continue;
            var json = File.ReadAllText(file);
            var strategy = JsonSerializer.Deserialize<Strategy>(json);
            if (strategy is null) continue;
            strategy.FileName = Path.GetFileNameWithoutExtension(file);
            strategies.Add(strategy);
        }
        Cache.Strategies.Clear();
        Cache.Strategies.AddRange(strategies);
        CurrencyWarsStrategyIndex = 0;
    }

    [RelayCommand]
    private async Task SelectedPath()
    {
        if (TopLevelObject is null) return;
        var files = await TopLevelObject.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions());
        if (files.Count == 0) return;
        StartGameConfig.GamePath = files[0].Path.LocalPath;
    }

    [RelayCommand]
    private void DeleteSelectedTaskItem()
    {
        if (SelectedTaskItem is TrailblazePowerTaskItem item) TrailblazePowerConfig.TaskList.Remove(item);
    }

    [RelayCommand]
    private void AddTaskItem()
    {
        if (SelectedTpTaskLevelIndex == 0) { _commonModel.ShowInfoToast("Info", "请选择副本关卡后再添加任务"); return; }
        TrailblazePowerConfig.TaskList.Add(new TrailblazePowerTaskItem
        {
            Name = TpTaskItems.TpTaskNames[SelectedTpTaskIndex],
            Id = TpTaskItems.TaskItems[SelectedTpTaskIndex].Id,
            Level = SelectedTpTaskLevelIndex,
            LevelName = CurrentTpTaskLevels.ElementAtOrDefault(SelectedTpTaskLevelIndex) ?? "",
            Count = TpTaskSingleTimes,
            RunTimes = TpTaskRunTimes,
            AutoDetect = IsTpTaskAutoDetect
        });
    }

    [RelayCommand]
    private async Task ShowTaskListControl()
    {
        var taskListControl = new TpTaskListControl { DataContext = this };
        await SukiMessageBox.ShowDialog(new SukiMessageBoxHost { Content = taskListControl });
        OnPropertyChanged(nameof(TaskListText));
    }

    [RelayCommand]
    private void ShowAddTaskControl()
    {
        var addTaskControl = new TpAddTaskControl { DataContext = this };
        SukiMessageBox.ShowDialog(new SukiMessageBoxHost { Content = addTaskControl });
    }
}
