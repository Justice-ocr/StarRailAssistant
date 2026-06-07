using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SRAFrontend.Data;
using SRAFrontend.Desktop.Views;
using SRAFrontend.Models;
using SRAFrontend.Services;

namespace SRAFrontend.Desktop.ViewModels;

public partial class ScriptStorePageViewModel : PageViewModel
{
    private readonly ScriptService _scriptService;

    [ObservableProperty] private AvaloniaList<ScriptRepo> _repos = [];
    [ObservableProperty] private ScriptRepo? _selectedRepo;
    [ObservableProperty] private bool _isAddRepoOpen;
    [ObservableProperty] private string _newRepoName = "";
    [ObservableProperty] private string _newRepoUrl = "";

    [ObservableProperty] private AvaloniaList<RepoScriptInfo> _repoScripts = [];
    [ObservableProperty] private AvaloniaList<ScriptManifest> _installedScripts = [];
    [ObservableProperty] private RepoScriptInfo? _selectedScript;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private int _downloadProgress;

    [ObservableProperty] private string _readmeTitle = "";
    [ObservableProperty] private string _readmeContent = "";
    [ObservableProperty] private bool _isReadmeLoading;

    public ScriptStorePageViewModel(ScriptService scriptService)
        : base(PageName.ScriptStore, "\uE396")
    {
        _scriptService = scriptService;
        LoadRepos();
        RefreshInstalled();
    }

    private void LoadRepos()
    {
        Repos.Clear();
        foreach (var repo in _scriptService.LoadRepos())
            Repos.Add(repo);

        if (Repos.Count > 0 && SelectedRepo == null)
            SelectedRepo = Repos[0];
    }

    [RelayCommand]
    private void OpenAddRepo() => IsAddRepoOpen = true;

    [RelayCommand]
    private void CloseAddRepo()
    {
        IsAddRepoOpen = false;
        NewRepoName = "";
        NewRepoUrl = "";
    }

    [RelayCommand]
    private void ConfirmAddRepo()
    {
        if (string.IsNullOrWhiteSpace(NewRepoUrl)) return;

        var name = string.IsNullOrWhiteSpace(NewRepoName) ? NewRepoUrl.Trim() : NewRepoName.Trim();
        if (_scriptService.AddRepo(name, NewRepoUrl.Trim()))
            LoadRepos();

        CloseAddRepo();
    }

    [RelayCommand]
    private void RemoveRepo()
    {
        if (SelectedRepo == null) return;

        _scriptService.RemoveRepo(SelectedRepo.Url);
        Repos.Remove(SelectedRepo);
        SelectedRepo = Repos.FirstOrDefault();
        RepoScripts.Clear();
    }

    partial void OnSelectedRepoChanged(ScriptRepo? value)
    {
        RepoScripts.Clear();
        if (value != null)
            _ = FetchRepoScriptsAsync();
    }

    [RelayCommand]
    private Task FetchRepoScripts() => FetchRepoScriptsAsync();

    private async Task FetchRepoScriptsAsync()
    {
        if (SelectedRepo == null || IsLoading) return;

        IsLoading = true;
        StatusMessage = "Fetching scripts...";
        try
        {
            var scripts = await _scriptService.FetchRepoScriptsAsync(SelectedRepo);
            RepoScripts.Clear();
            foreach (var script in scripts)
                RepoScripts.Add(script);

            RefreshInstalled();
            foreach (var script in RepoScripts)
            {
                var installed = InstalledScripts.FirstOrDefault(i => i.Id == script.Id);
                script.InstalledVersion = installed?.Version;
                script.HasUpdate = installed != null && script.Version != installed.Version;
            }

            StatusMessage = $"{scripts.Count} scripts";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fetch failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task InstallScript(RepoScriptInfo? info)
    {
        if (info == null || IsLoading) return;

        IsLoading = true;
        StatusMessage = $"Installing {info.Name}...";
        try
        {
            var progress = new Progress<(int Percent, string Message)>(p =>
            {
                DownloadProgress = p.Percent;
                StatusMessage = p.Message;
            });

            var ok = await _scriptService.DownloadAndInstallAsync(info, progress);
            StatusMessage = ok ? $"{info.Name} installed" : $"{info.Name} install failed";
            if (ok)
            {
                RefreshInstalled();
                await FetchRepoScriptsAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Install failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            DownloadProgress = 0;
        }
    }

    [RelayCommand]
    private void UninstallScript(ScriptManifest? manifest)
    {
        if (manifest == null) return;

        _scriptService.Uninstall(manifest.Id);
        RefreshInstalled();
        StatusMessage = $"{manifest.Name} uninstalled";

        foreach (var script in RepoScripts.Where(s => s.Id == manifest.Id))
            script.InstalledVersion = null;
    }

    [RelayCommand]
    private async Task CheckUpdates()
    {
        if (IsLoading) return;

        await FetchRepoScriptsAsync();
        var updates = RepoScripts.Count(s => s.HasUpdate);
        StatusMessage = updates > 0 ? $"{updates} updates available" : "All scripts are up to date";
    }

    private void RefreshInstalled()
    {
        InstalledScripts.Clear();
        foreach (var script in _scriptService.GetInstalledScripts())
            InstalledScripts.Add(script);
    }

    [RelayCommand]
    private async Task OpenReadme(RepoScriptInfo? info)
    {
        if (info == null) return;

        ReadmeTitle = info.Name;
        ReadmeContent = "";
        IsReadmeLoading = true;
        ShowReadmeWindow();
        try
        {
            ReadmeContent = await _scriptService.FetchReadmeAsync(info) ?? "_No README_";
        }
        catch
        {
            ReadmeContent = "_Failed to load README_";
        }
        finally
        {
            IsReadmeLoading = false;
        }
    }

    [RelayCommand]
    private async Task OpenInstalledReadme(ScriptManifest? manifest)
    {
        if (manifest == null) return;

        ReadmeTitle = manifest.Name;
        ReadmeContent = "";
        IsReadmeLoading = true;
        ShowReadmeWindow();
        ReadmeContent = await Task.Run(() =>
            _scriptService.ReadLocalReadme(manifest.Id) ?? "_No README_");
        IsReadmeLoading = false;
    }

    private void ShowReadmeWindow()
    {
        var window = new ReadmeWindow { DataContext = this };
        window.Show();
    }
}
