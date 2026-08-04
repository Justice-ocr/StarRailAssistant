using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SRAFrontend.Data;
using SRAFrontend.Desktop.Controls;
using SRAFrontend.Desktop.Views;
using SRAFrontend.Models;
using SRAFrontend.Services;
using SukiUI.Controls;
using SukiUI.MessageBox;

namespace SRAFrontend.Desktop.ViewModels;

public partial class TaskOrderItem : ObservableObject
{
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isSelected;
    public string ClassName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsFixed { get; set; }
    public bool IsMovable => !IsFixed;
    public bool IsCustom { get; set; }
    public bool IsAddButton { get; set; }
    public bool IsFixedTab => IsFixed && !IsAddButton;
    public int OriginalIndex { get; set; } = -1;
}

public partial class TaskPageViewModel : PageViewModel
{
    private const string FixedFirstTask = "StartGameTask";
    private const string FixedLastTask = "MissionAccomplishTask";

    private static readonly List<(string ClassName, string DisplayName)> AllTaskDefs =
    [
        ("StartGameTask", "启动游戏"),
        ("TrailblazePowerTask", "清体力"),
        ("ReceiveRewardsTask", "领取奖励"),
        ("CosmicStrifeTask", "旷宇纷争"),
        ("MissionAccomplishTask", "任务完成"),
    ];

    private readonly CacheService _cacheService;
    private readonly CommonModel _commonModel;
    private readonly ConfigService _configService;
    private readonly IBackendService _backendService;
    private readonly ScriptService _scriptService;
    private TpTask[] _tpTasks = [];
    private string _selectedClassName = FixedFirstTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CosmicStrifeConfig), nameof(MissionAccomplishedConfig),
        nameof(ReceiveRewardsConfig), nameof(StartGameConfig), nameof(TrailblazePowerConfig))]
    private TasksConfig _currentConfig;

    [ObservableProperty] private bool _isTpTaskAutoDetect;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(EnableContextMenu))]
    private object? _selectedTaskItem;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CurrentTpTaskLevels), nameof(CurrentTpTaskMaxSingleTimes))]
    private int _selectedTpTaskIndex;
    public int SelectedGardenOfPlentyLevels1Index
    {
        get => TrailblazePowerConfig.GardenOfPlentyLevel1-1;
        set
        {
            TrailblazePowerConfig.GardenOfPlentyLevel1 = value+1;
            OnPropertyChanged();
        }
    }
    public int SelectedGardenOfPlentyLevels2Index
    {
        get => TrailblazePowerConfig.GardenOfPlentyLevel2-1;
        set
        {
            TrailblazePowerConfig.GardenOfPlentyLevel2 = value+1;
            OnPropertyChanged();
        }
    }

    public int SelectedPlanarFissureLevelsIndex
    {
        get => TrailblazePowerConfig.PlanarFissureLevel-1;
        set
        {
            TrailblazePowerConfig.PlanarFissureLevel = value+1;
            OnPropertyChanged();
        }
    }

    public int SelectedRealmOfTheStrangeLevelsIndex
    {
        get =>  TrailblazePowerConfig.RealmOfTheStrangeLevel-1;
        set
        {
            TrailblazePowerConfig.RealmOfTheStrangeLevel = value+1;
            OnPropertyChanged();
        }
    }

    [ObservableProperty] private TpTaskLevel? _selectedTpTaskLevel;
    [ObservableProperty] private int _tpTaskRunTimes = 1;
    [ObservableProperty] private int _tpTaskSingleTimes = 1;
    [ObservableProperty] private AvaloniaList<TaskOrderItem> _taskOrderList = [];

    public TaskPageViewModel(
        CommonModel commonModel,
        ControlPanelViewModel controlPanelViewModel,
        ConfigService configService,
        CacheService cacheService,
        IBackendService backendService,
        ScriptService scriptService) : base(PageName.Task, "\uE1BC")
    {
        ControlPanelViewModel = controlPanelViewModel;
        _commonModel = commonModel;
        _configService = configService;
        _cacheService = cacheService;
        _backendService = backendService;
        _scriptService = scriptService;
        CurrentConfig = _configService.TasksConfig!;

        _cacheService.Cache.PropertyChanged += OnCachePropertyChanged;

        if (Cache.Strategies.Count == 0) _ = RefreshStrategies();
        InitTaskOrderList();
        return;

        void OnCachePropertyChanged(object? _, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(Cache.CurrentConfigIndex)) return;
            _configService.SwitchConfig(_cacheService.Cache.ConfigNames[_cacheService.Cache.CurrentConfigIndex]);
            CurrentConfig = _configService.TasksConfig!;
            InitTaskOrderList();
        }
    }

    public string[] TpTaskNames => [.. _tpTasks.Select(t => t.Name)];
    public TpTaskLevel[] CurrentTpTaskLevels => _tpTasks.ElementAt(SelectedTpTaskIndex).Levels;
    public string[] GardenOfPlentyLevels1 => [.. _tpTasks.ElementAt(1).Levels.Select(x => $"{x.Name}（{x.Result}）")];
    public string[] GardenOfPlentyLevels2 => [.. _tpTasks.ElementAt(2).Levels.Select(x => $"{x.Name}（{x.Result}）")];
    public string[] PlanarFissureLevels => [.. _tpTasks.ElementAt(0).Levels.Select(x => $"{x.Name}（{x.Result}）")];
    public string[] RealmOfTheStrangeLevels => [.. _tpTasks.ElementAt(4).Levels.Select(x => $"{x.Name}（{x.Result}）")];
    public int CurrentTpTaskMaxSingleTimes => _tpTasks[SelectedTpTaskIndex].MaxSingleTimes;

    public string TaskListText =>
        TrailblazePowerConfig.TaskList.Count == 0
            ? "暂无任务"
            : $"{string.Join("、", TrailblazePowerConfig.TaskList.Select(x => x.Name).Take(3))} 等 {TrailblazePowerConfig.TaskList.Count} 个任务";

    public CosmicStrifeConfig CosmicStrifeConfig => CurrentConfig.CosmicStrife;
    public MissionAccomplishedConfig MissionAccomplishedConfig => CurrentConfig.MissionAccomplished;
    public ReceiveRewardsConfig ReceiveRewardsConfig => CurrentConfig.ReceiveRewards;
    public StartGameConfig StartGameConfig => CurrentConfig.StartGame;
    public TrailblazePowerConfig TrailblazePowerConfig => CurrentConfig.TrailblazePower;
    public ControlPanelViewModel ControlPanelViewModel { get; }
    public TopLevel? TopLevelObject { get; set; }
    public bool EnableContextMenu => SelectedTaskItem is not null;
    public Cache Cache => _cacheService.Cache;

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

    private void InitTaskOrderList()
    {
        TaskOrderList.Clear();
        var middleDefs = AllTaskDefs.Where(d => d.ClassName != FixedFirstTask && d.ClassName != FixedLastTask).ToList();
        var firstDef = AllTaskDefs.First(d => d.ClassName == FixedFirstTask);
        var lastDef = AllTaskDefs.First(d => d.ClassName == FixedLastTask);

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
                        return (c, entry?.Name ?? "自定义任务", entry?.IsEnabled ?? true);
                    }

                    return (c, AllTaskDefs.FirstOrDefault(d => d.ClassName == c).DisplayName, IsBuiltinTaskEnabled(c));
                })
                .Where(t => !string.IsNullOrEmpty(t.Item2))
                .ToList();
            var enabledSet = new HashSet<string>(enabledMiddle.Select(t => t.c));
            var disabledMiddle = middleDefs.Where(d => !enabledSet.Contains(d.ClassName))
                .Select(d => (d.ClassName, d.DisplayName, false));
            var missingCustomTasks = CurrentConfig.CustomTasks
                .Select(t => ("CustomTask_" + t.Id, t.Name, t.IsEnabled))
                .Where(t => !enabledSet.Contains(t.Item1));
            middleItems = enabledMiddle.Concat(disabledMiddle).Concat(missingCustomTasks).ToList();
        }
        else
        {
            middleItems = middleDefs.Select(d =>
            {
                var origIdx = AllTaskDefs.FindIndex(x => x.ClassName == d.ClassName);
                var enabled = origIdx >= 0 && origIdx < CurrentConfig.EnabledTasks.Count &&
                              CurrentConfig.EnabledTasks[origIdx];
                return (d.ClassName, d.DisplayName, enabled);
            }).Concat(CurrentConfig.CustomTasks.Select(t => ("CustomTask_" + t.Id, t.Name, t.IsEnabled))).ToList();
        }

        var firstEnabled = CurrentConfig.TaskOrder.Count > 0
            ? StartGameConfig.IsEnabled
            : CurrentConfig.EnabledTasks.ElementAtOrDefault(0);
        TaskOrderList.Add(new TaskOrderItem
        {
            ClassName = firstDef.ClassName,
            DisplayName = firstDef.DisplayName,
            IsEnabled = firstEnabled,
            IsFixed = true,
            OriginalIndex = 0
        });

        foreach (var (className, displayName, enabled) in middleItems)
        {
            TaskOrderList.Add(new TaskOrderItem
            {
                ClassName = className,
                DisplayName = displayName,
                IsEnabled = enabled,
                IsFixed = false,
                IsCustom = className.StartsWith("CustomTask_"),
                OriginalIndex = AllTaskDefs.FindIndex(d => d.ClassName == className)
            });
        }

        var lastEnabled = CurrentConfig.TaskOrder.Count > 0
            ? MissionAccomplishedConfig.IsEnabled
            : CurrentConfig.EnabledTasks.ElementAtOrDefault(4);
        TaskOrderList.Add(new TaskOrderItem
        {
            ClassName = lastDef.ClassName,
            DisplayName = lastDef.DisplayName,
            IsEnabled = lastEnabled,
            IsFixed = true,
            OriginalIndex = 4
        });
        TaskOrderList.Add(new TaskOrderItem { ClassName = "__add__", DisplayName = "+", IsFixed = true, IsAddButton = true });

        foreach (var item in TaskOrderList)
            WatchTaskOrderItem(item);

        SyncTaskOrderToConfig();
        if (TaskOrderList.Count > 0) SelectTask(TaskOrderList[0].ClassName);
    }

    private TaskOrderItem? GetTaskItem(string className) =>
        TaskOrderList.FirstOrDefault(t => t.ClassName == className);

    private void WatchTaskOrderItem(TaskOrderItem item) =>
        item.PropertyChanged += OnTaskOrderItemPropertyChanged;

    private void OnTaskOrderItemPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(TaskOrderItem.IsEnabled)) return;
        SyncTaskOrderToConfig(save: true);
    }

    private bool IsBuiltinTaskEnabled(string className) => className switch
    {
        "StartGameTask" => StartGameConfig.IsEnabled,
        "TrailblazePowerTask" => TrailblazePowerConfig.IsEnabled,
        "ReceiveRewardsTask" => ReceiveRewardsConfig.IsEnabled,
        "CosmicStrifeTask" => CosmicStrifeConfig.IsEnabled,
        "MissionAccomplishTask" => MissionAccomplishedConfig.IsEnabled,
        _ => false
    };

    private void SyncEnabledTasksToLegacyList()
    {
        var enabledTasks = new[]
        {
            StartGameConfig.IsEnabled,
            TrailblazePowerConfig.IsEnabled,
            ReceiveRewardsConfig.IsEnabled,
            CosmicStrifeConfig.IsEnabled,
            MissionAccomplishedConfig.IsEnabled
        };

        CurrentConfig.EnabledTasks.Clear();
        CurrentConfig.EnabledTasks.AddRange(enabledTasks);
    }

    private void SyncTaskOrderToConfig(bool save = false)
    {
        CurrentConfig.TaskOrder.Clear();
        foreach (var item in TaskOrderList.Where(i => !i.IsAddButton))
            CurrentConfig.TaskOrder.Add(item.ClassName);
        SyncEnabledTasksToLegacyList();
        if (save) _configService.Save();
    }

    public void SelectTask(string className)
    {
        _selectedClassName = className;
        foreach (var item in TaskOrderList)
            item.IsSelected = item.ClassName == className;

        OnPropertyChanged(nameof(StartGameTaskSelected));
        OnPropertyChanged(nameof(TrailblazePowerTaskSelected));
        OnPropertyChanged(nameof(ReceiveRewardsTaskSelected));
        OnPropertyChanged(nameof(CosmicStrifeTaskSelected));
        OnPropertyChanged(nameof(MissionAccomplishTaskSelected));
        OnPropertyChanged(nameof(CustomTaskSelected));
        OnPropertyChanged(nameof(SelectedCustomTask));
        if (CustomTaskSelected) RefreshInstalledScripts();
    }

    public bool StartGameTaskSelected => _selectedClassName == "StartGameTask";
    public bool TrailblazePowerTaskSelected => _selectedClassName == "TrailblazePowerTask";
    public bool ReceiveRewardsTaskSelected => _selectedClassName == "ReceiveRewardsTask";
    public bool CosmicStrifeTaskSelected => _selectedClassName == "CosmicStrifeTask";
    public bool MissionAccomplishTaskSelected => _selectedClassName == "MissionAccomplishTask";
    public bool CustomTaskSelected => _selectedClassName.StartsWith("CustomTask_");

    public CustomTaskEntry? SelectedCustomTask =>
        CustomTaskSelected
            ? CurrentConfig.CustomTasks.FirstOrDefault(t => "CustomTask_" + t.Id == _selectedClassName)
            : null;

    private ScriptManifest? _selectedInstalledScript;
    public ScriptManifest? SelectedInstalledScript
    {
        get => _selectedInstalledScript;
        set
        {
            _selectedInstalledScript = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedScriptTasks));
            OnPropertyChanged(nameof(SelectedScriptHasMultipleTasks));
            SelectedScriptTask = value?.Tasks.FirstOrDefault();
            if (value != null && value.Tasks.Count == 1)
                ApplyScriptSelection(value, value.Tasks[0]);
        }
    }

    public List<ScriptTaskDef> SelectedScriptTasks => _selectedInstalledScript?.Tasks ?? [];
    public bool SelectedScriptHasMultipleTasks => (_selectedInstalledScript?.Tasks.Count ?? 0) > 1;

    private ScriptTaskDef? _selectedScriptTask;
    public ScriptTaskDef? SelectedScriptTask
    {
        get => _selectedScriptTask;
        set
        {
            _selectedScriptTask = value;
            OnPropertyChanged();
            if (_selectedInstalledScript != null && value != null)
                ApplyScriptSelection(_selectedInstalledScript, value);
        }
    }

    public System.Collections.ObjectModel.ObservableCollection<ScriptManifest> InstalledScripts { get; } = [];

    private void ApplyScriptSelection(ScriptManifest script, ScriptTaskDef task)
    {
        if (SelectedCustomTask == null) return;
        SelectedCustomTask.ScriptId = script.Id;
        SelectedCustomTask.TaskEntry = task.Entry;
        SelectedCustomTask.TaskClassName = task.Class;
        if (SelectedCustomTask.Name.StartsWith("自定义任务"))
            SelectedCustomTask.Name = task.Name;

        var item = TaskOrderList.FirstOrDefault(t => t.ClassName == "CustomTask_" + SelectedCustomTask.Id);
        if (item != null) item.DisplayName = SelectedCustomTask.Name;
        SelectedCustomTask.ScriptPath = "";
        OnPropertyChanged(nameof(SelectedCustomTask));
        SyncTaskOrderToConfig();
        _configService.Save();
    }

    [RelayCommand]
    public void RefreshInstalledScripts()
    {
        InstalledScripts.Clear();
        foreach (var script in _scriptService.GetInstalledScripts())
            InstalledScripts.Add(script);

        if (SelectedCustomTask == null || string.IsNullOrEmpty(SelectedCustomTask.ScriptId)) return;
        _selectedInstalledScript = InstalledScripts.FirstOrDefault(s => s.Id == SelectedCustomTask.ScriptId);
        OnPropertyChanged(nameof(SelectedInstalledScript));
        OnPropertyChanged(nameof(SelectedScriptTasks));
        OnPropertyChanged(nameof(SelectedScriptHasMultipleTasks));
        if (_selectedInstalledScript != null)
            _selectedScriptTask = _selectedInstalledScript.Tasks.FirstOrDefault(t => t.Entry == SelectedCustomTask.TaskEntry);
        OnPropertyChanged(nameof(SelectedScriptTask));
    }

    [RelayCommand]
    private void AddCustomTask()
    {
        RefreshInstalledScripts();
        var entry = new CustomTaskEntry
        {
            Name = $"自定义任务 {CurrentConfig.CustomTasks.Count + 1}",
            IsEnabled = true
        };
        CurrentConfig.CustomTasks.Add(entry);
        var className = "CustomTask_" + entry.Id;
        var newItem = new TaskOrderItem
        {
            ClassName = className,
            DisplayName = entry.Name,
            IsEnabled = true,
            IsCustom = true,
            OriginalIndex = -1
        };
        WatchTaskOrderItem(newItem);
        var lastFixed = TaskOrderList.FirstOrDefault(t => t.ClassName == FixedLastTask);
        var insertPos = lastFixed != null ? TaskOrderList.IndexOf(lastFixed) : TaskOrderList.Count;
        TaskOrderList.Insert(insertPos, newItem);
        SyncTaskOrderToConfig();
        SelectTask(className);
        _configService.Save();
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
        _configService.Save();
    }

    public void MoveTaskToIndex(TaskOrderItem item, int targetIndex)
    {
        if (item.IsFixed) return;
        var currentIndex = TaskOrderList.IndexOf(item);
        if (currentIndex < 0 || currentIndex == targetIndex) return;
        if (targetIndex < 0 || targetIndex >= TaskOrderList.Count) return;
        if (TaskOrderList[targetIndex].IsFixed) return;

        TaskOrderList.RemoveAt(currentIndex);
        TaskOrderList.Insert(targetIndex, item);
        SyncTaskOrderToConfig(save: true);
    }

    [RelayCommand]
    private void OpenScriptConfig()
    {
        if (SelectedCustomTask == null || string.IsNullOrEmpty(SelectedCustomTask.ScriptId)) return;
        var scriptId = SelectedCustomTask.ScriptId;
        var configDir = Path.Combine(DataPath.AppDataDir, "scripts", scriptId);
        var paramDefs = _scriptService.LoadScriptParamDefs(scriptId);
        if (paramDefs.Count == 0)
        {
            var manifest = InstalledScripts.FirstOrDefault(s => s.Id == scriptId);
            if (manifest != null)
                paramDefs = manifest.LoadedParams.Count > 0
                    ? manifest.LoadedParams
                    : manifest.Tasks.SelectMany(t => t.Params).ToList();
        }

        var vm = new ScriptConfigWindowViewModel(scriptId, configDir, paramDefs);
        var window = new ScriptConfigWindow { DataContext = vm };
        window.Show();
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
        _configService.Save();
    }

    [RelayCommand]
    private async Task SingleCustomTask()
    {
        if (SelectedCustomTask == null) return;
        await SingleTask("CustomTask_" + SelectedCustomTask.Id);
    }

    public async Task GetTpConfigAsync()
    {
        if (_tpTasks.Length > 0) return;
        _tpTasks = await _backendService.GetTpConfigAsync();
        OnPropertyChanged(nameof(TpTaskNames));
        OnPropertyChanged(nameof(CurrentTpTaskLevels));
        OnPropertyChanged(nameof(GardenOfPlentyLevels1));
        OnPropertyChanged(nameof(GardenOfPlentyLevels2));
        OnPropertyChanged(nameof(PlanarFissureLevels));
        OnPropertyChanged(nameof(RealmOfTheStrangeLevels));
        OnPropertyChanged(nameof(CurrentTpTaskMaxSingleTimes));
    }

    [RelayCommand]
    private async Task SingleTask(string taskName)
    {
        await ControlPanelViewModel.StartSingleTask(taskName);
    }

    [RelayCommand]
    private async Task RefreshStrategies()
    {
        try
        {
            var strategies = await _backendService.GetStrategiesAsync();
            Cache.Strategies.Clear();
            foreach (var strategy in strategies)
                Cache.Strategies.Add(strategy);
            CurrencyWarsStrategyIndex = 0;
        }
        catch (Exception ex)
        {
            _commonModel.ShowErrorToast("攻略加载失败", ex.Message);
        }
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
        if (SelectedTaskItem is TrailblazePowerTaskItem item)
            TrailblazePowerConfig.TaskList.Remove(item);
    }

    [RelayCommand]
    private void AddTaskItem()
    {
        if (SelectedTpTaskLevel is null)
        {
            _commonModel.ShowInfoToast("提示", "请选择副本关卡后再添加任务");
            return;
        }

        TrailblazePowerConfig.TaskList.Add(new TrailblazePowerTaskItem
        {
            Name = _tpTasks[SelectedTpTaskIndex].Name,
            Id = _tpTasks[SelectedTpTaskIndex].Id,
            Level = SelectedTpTaskLevel.Id,
            LevelName = SelectedTpTaskLevel.Name,
            Count = TpTaskSingleTimes,
            RunTimes = TpTaskRunTimes,
            AutoDetect = IsTpTaskAutoDetect
        });
        OnPropertyChanged(nameof(TaskListText));
    }

    [RelayCommand]
    private async Task ShowTaskListControl()
    {
        await SukiMessageBox.ShowDialog(new SukiMessageBoxHost
        {
            Content = new TpTaskListControl { DataContext = this }
        });
        OnPropertyChanged(nameof(TaskListText));
    }

    [RelayCommand]
    private void ShowAddTaskControl()
    {
        SukiMessageBox.ShowDialog(new SukiMessageBoxHost
        {
            Content = new TpAddTaskControl { DataContext = this }
        });
    }
}
