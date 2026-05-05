using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SRAFrontend.Services;

namespace SRAFrontend.ViewModels;

/// <summary>单个任务的通知配置项（含分组标记）</summary>
public partial class TaskNotificationItem : ObservableObject
{
    public string ClassName { get; set; } = "";
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private bool _notifyOnStart;
    [ObservableProperty] private bool _notifyOnComplete;
    public bool IsCustomTask { get; set; }  // 用于 UI 区分内置/自定义任务
}

public partial class TaskNotificationWindowViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly Window _window;

    private static readonly List<(string ClassName, string DisplayName)> BuiltinTasks =
    [
        ("StartGameTask",         "启动游戏"),
        ("TrailblazePowerTask",   "清开拓力"),
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
        var onComplete = settingsService.Settings.Notification.OnComplete;

        // 内置任务
        foreach (var (cls, display) in BuiltinTasks)
        {
            TaskNotifications.Add(new TaskNotificationItem
            {
                ClassName        = cls,
                DisplayName      = display,
                NotifyOnStart    = onStart.Contains(cls),
                NotifyOnComplete = onComplete.Contains(cls),
                IsCustomTask     = false,
            });
        }

        // 自定义任务（从当前配置读取）
        var customTasks = configService.Config?.CustomTasks ?? [];
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

        _settingsService.Settings.Notification.OnStart    = onStart;
        _settingsService.Settings.Notification.OnComplete = onComplete;
        _settingsService.Save();

        _window.Close();
    }

    [RelayCommand]
    private void Cancel() => _window.Close();
}
