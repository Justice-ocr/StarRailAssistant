using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SRAFrontend.Services;

namespace SRAFrontend.Desktop.ViewModels;

/// <summary>单个任务的通知配置项。</summary>
public partial class TaskNotificationItem : ObservableObject
{
    public string ClassName    { get; init; } = "";
    public string DisplayName  { get; init; } = "";
    public bool   IsCustomTask { get; init; }
    [ObservableProperty] private bool _notifyOnStart;
    [ObservableProperty] private bool _notifyOnComplete;
}

public partial class TaskNotificationWindowViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly Window _window;

    private static readonly List<(string ClassName, string DisplayName)> BuiltinTasks =
    [
        ("StartGameTask",         "启动游戏"),
        ("TrailblazePowerTask",   "清体力"),
        ("ReceiveRewardsTask",    "领取奖励"),
        ("CosmicStrifeTask",      "旷宇纷争"),
        ("MissionAccomplishTask", "任务完成"),
    ];

    public List<TaskNotificationItem> TaskNotifications { get; } = [];

    public TaskNotificationWindowViewModel(SettingsService settingsService,
                                           ConfigService configService,
                                           Window window)
    {
        _settingsService = settingsService;
        _window = window;

        var onStart    = settingsService.Settings.Notification.OnStart;
        var onComplete = settingsService.Settings.Notification.OnCompleted;

        // 内置任务
        foreach (var (cls, display) in BuiltinTasks)
        {
            TaskNotifications.Add(new TaskNotificationItem
            {
                ClassName        = cls,
                DisplayName      = display,
                NotifyOnStart    = onStart.Contains(cls),
                NotifyOnComplete = onComplete.Contains(cls),
            });
        }

        // 自定义任务
        var customTasks = configService.TasksConfig?.CustomTasks ?? [];
        foreach (var ct in customTasks)
        {
            if (!ct.IsEnabled) continue;
            var key = $"CustomTask_{ct.Id}";
            TaskNotifications.Add(new TaskNotificationItem
            {
                ClassName        = key,
                DisplayName      = ct.Name,
                NotifyOnStart    = onStart.Contains(key),
                NotifyOnComplete = onComplete.Contains(key),
                IsCustomTask     = true,
            });
        }
    }

    [RelayCommand]
    private void Save()
    {
        var onStart    = TaskNotifications.Where(t => t.NotifyOnStart).Select(t => t.ClassName).ToList();
        var onComplete = TaskNotifications.Where(t => t.NotifyOnComplete).Select(t => t.ClassName).ToList();

        _settingsService.Settings.Notification.OnStart.Clear();
        foreach (var task in onStart)
            _settingsService.Settings.Notification.OnStart.Add(task);

        _settingsService.Settings.Notification.OnCompleted.Clear();
        foreach (var task in onComplete)
            _settingsService.Settings.Notification.OnCompleted.Add(task);
        _settingsService.Save();

        _window.Close();
    }

    [RelayCommand]
    private void Cancel() => _window.Close();
}
