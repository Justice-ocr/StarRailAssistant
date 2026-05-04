using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SRAFrontend.Data;
using SRAFrontend.Models;

namespace SRAFrontend.Services;

/// <summary>
/// 脚本仓库服务：负责仓库管理、脚本列表拉取、下载安装、本地扫描
/// </summary>
public class ScriptService
{
    private static readonly string ScriptsDir =
        Path.Combine(PathString.AppDataDir, "scripts");
    private static readonly string ReposConfigPath =
        Path.Combine(PathString.AppDataDir, "script_repos.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;

    public ScriptService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
        Directory.CreateDirectory(ScriptsDir);
    }

    // ===== 仓库管理 =====

    public List<ScriptRepo> LoadRepos()
    {
        if (!File.Exists(ReposConfigPath)) return [];
        try
        {
            var json = File.ReadAllText(ReposConfigPath);
            var root = JsonSerializer.Deserialize<ReposConfig>(json, JsonOpts);
            return root?.Repos ?? [];
        }
        catch { return []; }
    }

    private void SaveRepos(List<ScriptRepo> repos)
    {
        var json = JsonSerializer.Serialize(new ReposConfig { Repos = repos }, JsonOpts);
        File.WriteAllText(ReposConfigPath, json);
    }

    public bool AddRepo(string name, string url)
    {
        var repos = LoadRepos();
        if (repos.Exists(r => r.Url == url)) return false;
        repos.Add(new ScriptRepo { Name = name, Url = url });
        SaveRepos(repos);
        return true;
    }

    public void RemoveRepo(string url)
    {
        var repos = LoadRepos();
        repos.RemoveAll(r => r.Url == url);
        SaveRepos(repos);
    }

    // ===== 远程脚本列表 =====

    /// <summary>
    /// 将用户输入的 GitHub 仓库 URL 转换为 repo.json 的直链地址
    /// </summary>
    private static string ResolveRepoJsonUrl(string url)
    {
        url = url.TrimEnd('/');
        // 已经是直链（raw / 非 github.com 域名）
        if (!url.Contains("github.com") || url.Contains("raw.githubusercontent.com"))
            return url;
        // github.com/user/repo/blob/branch/path → raw URL
        if (url.Contains("/blob/"))
            return url.Replace("github.com", "raw.githubusercontent.com").Replace("/blob/", "/");
        // github.com/user/repo → 补上 /main/repo.json
        // 去掉可能的 /tree/branch 前缀
        var uri = new Uri(url);
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length >= 2)
        {
            var user = segments[0];
            var repo = segments[1];
            var branch = segments.Length >= 4 && segments[2] == "tree" ? segments[3] : "main";
            return $"https://raw.githubusercontent.com/{user}/{repo}/{branch}/repo.json";
        }
        return url;
    }

    public async Task<List<RepoScriptInfo>> FetchRepoScriptsAsync(ScriptRepo repo)
    {
        var client = _httpClientFactory.CreateClient("GlobalClient");
        var resolvedUrl = ResolveRepoJsonUrl(repo.Url);
        var json = await client.GetStringAsync(resolvedUrl);
        var root = JsonSerializer.Deserialize<RepoIndex>(json, JsonOpts);
        if (root?.Scripts == null) return [];

        var installed = GetInstalledScripts();
        var installedMap = new Dictionary<string, string>();
        foreach (var s in installed) installedMap[s.Id] = s.Version;

        var result = new List<RepoScriptInfo>();
        foreach (var item in root.Scripts)
        {
            var info = new RepoScriptInfo
            {
                Id = item.Id ?? "",
                Name = item.Name ?? "",
                Version = item.Version ?? "0.0.0",
                Description = item.Description ?? "",
                Author = item.Author ?? "",
                LastUpdated = item.LastUpdated ?? "",
                DownloadUrl = item.DownloadUrl ?? "",
                RepoPath = item.RepoPath ?? "",
            };
            if (installedMap.TryGetValue(info.Id, out var ver))
            {
                info.InstalledVersion = ver;
                info.HasUpdate = info.Version != ver;
            }
            result.Add(info);
        }
        return result;
    }

    // ===== 本地脚本 =====

    public List<ScriptManifest> GetInstalledScripts()
    {
        var result = new List<ScriptManifest>();
        if (!Directory.Exists(ScriptsDir)) return result;
        foreach (var dir in Directory.GetDirectories(ScriptsDir))
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;
            try
            {
                var json = File.ReadAllText(manifestPath);
                var m = JsonSerializer.Deserialize<ManifestJson>(json, JsonOpts);
                if (m == null) continue;
                var manifest = new ScriptManifest
                {
                    Id = m.Id ?? Path.GetFileName(dir),
                    Name = m.Name ?? "",
                    Version = m.Version ?? "0.0.0",
                    Description = m.Description ?? "",
                    Author = m.Author ?? "",
                };
                foreach (var t in m.Tasks ?? [])
                    manifest.Tasks.Add(new ScriptTaskDef
                    {
                        Name = t.Name ?? "",
                        Entry = t.Entry ?? "",
                        Class = t.Class ?? "",
                    });
                // 加载 settings.json -> LoadedParams
                var settingsPath = Path.Combine(dir, "settings.json");
                if (File.Exists(settingsPath))
                {
                    try
                    {
                        var settingsJson = File.ReadAllText(settingsPath);
                        var defs = System.Text.Json.JsonSerializer.Deserialize<List<ScriptParamDef>>(
                            settingsJson, JsonOpts);
                        if (defs != null) manifest.LoadedParams.AddRange(defs);
                    }
                    catch { /* 忽略解析失败 */ }
                }
                result.Add(manifest);
            }
            catch { /* 跳过损坏的 manifest */ }
        }
        return result;
    }

    public string GetScriptDir(string scriptId) =>
        Path.Combine(ScriptsDir, scriptId);

    /// <summary>读取脚本目录下 settings.json，解析为参数定义列表</summary>
    public List<ScriptParamDef> LoadScriptParamDefs(string scriptId)
    {
        var path = Path.Combine(ScriptsDir, scriptId, "settings.json");
        if (!File.Exists(path)) return [];
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<ScriptParamDef>>(json, JsonOpts) ?? [];
        }
        catch { return []; }
    }

    /// <summary>读取已安装脚本的本地 README.md</summary>
    public string? ReadLocalReadme(string scriptId)
    {
        var path = Path.Combine(ScriptsDir, scriptId, "README.md");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>从仓库远程拉取 README（通过 repo_path 构造 raw URL）</summary>
    public Task<string?> FetchReadmeAsync(RepoScriptInfo info)
    {
        if (string.IsNullOrEmpty(info.DownloadUrl)) return Task.FromResult<string?>(null);
        // download_url 形如 https://github.com/xxx/sra-scripts/archive/refs/heads/main.zip
        // 尝试先看本地已安装
        var local = ReadLocalReadme(info.Id);
        if (local != null) return Task.FromResult<string?>(local);

        // 如果未安装，从 GitHub raw 拉取（download_url 转换为 raw 地址）
        // 由于 download_url 是 zip，只能提示用户安装后查看
        return Task.FromResult<string?>("_请先安装脚本以查看本地 README，或访问仓库主页查阅文档。_");
    }

    // ===== 下载安装 =====

    /// <summary>
    /// 从 raw.githubusercontent.com 直接逐文件下载脚本，比下载整个仓库 zip 更快。
    /// 返回 null 表示无法使用此方式（fallback 到 zip），true/false 表示成功/失败。
    /// </summary>
    private async Task<bool?> TryInstallFromRawAsync(
        RepoScriptInfo info,
        IProgress<(int Percent, string Message)>? progress)
    {
        try
        {
            // download_url: https://github.com/user/repo/archive/refs/heads/main.zip
            // 转换为 raw base: https://raw.githubusercontent.com/user/repo/main/
            // 构造 raw base URL
            string rawBase;
            if (info.DownloadUrl.Contains("raw.githubusercontent.com"))
            {
                // 已经是 raw base URL，直接使用
                rawBase = info.DownloadUrl.TrimEnd('/') + "/";
            }
            else if (info.DownloadUrl.Contains("/archive/"))
            {
                // archive zip URL 转换
                rawBase = info.DownloadUrl
                    .Replace("github.com", "raw.githubusercontent.com")
                    .Replace("/archive/refs/heads/", "/")
                    .Replace(".zip", "/");
                if (!string.IsNullOrEmpty(info.RepoPath))
                    rawBase = rawBase.TrimEnd('/') + "/" + info.RepoPath.TrimEnd('/') + "/";
            }
            else
            {
                return null; // 无法识别的 URL 格式，fallback
            }

            // 先下载 manifest.json 获取文件列表
            var manifestUrl = rawBase + "manifest.json";
            var client = _httpClientFactory.CreateClient("GlobalClient");

            progress?.Report((5, "正在获取脚本清单..."));
            var manifestResp = await client.GetAsync(manifestUrl);
            if (!manifestResp.IsSuccessStatusCode) return null; // fallback

            var manifestJson = await manifestResp.Content.ReadAsStringAsync();
            if (manifestJson.TrimStart().StartsWith('<')) return null; // 返回了 HTML，fallback

            var manifest = JsonSerializer.Deserialize<ManifestJson>(manifestJson, JsonOpts);
            if (manifest == null) return null;

            // 收集需要下载的文件（manifest.json + settings.json + README.md + 所有 task 文件）
            var files = new List<string> { "manifest.json" };
            if (manifest.Tasks != null)
                foreach (var task in manifest.Tasks)
                    if (!string.IsNullOrEmpty(task.Entry) && !files.Contains(task.Entry))
                        files.Add(task.Entry);
            // 尝试下载可选文件
            foreach (var optional in new[] { "settings.json", "README.md" })
                files.Add(optional);

            var scriptDir = GetScriptDir(info.Id);
            if (Directory.Exists(scriptDir)) Directory.Delete(scriptDir, true);
            Directory.CreateDirectory(scriptDir);

            int downloaded = 0;
            foreach (var file in files)
            {
                var fileUrl = rawBase + file;
                var resp = await client.GetAsync(fileUrl);
                downloaded++;
                var pct = 10 + (int)(downloaded * 85.0 / files.Count);
                if (!resp.IsSuccessStatusCode)
                {
                    // 可选文件不存在不报错
                    if (file is "settings.json" or "README.md") continue;
                    progress?.Report((0, $"下载失败: {file} ({(int)resp.StatusCode})"));
                    if (Directory.Exists(scriptDir)) Directory.Delete(scriptDir, true);
                    return false;
                }
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(Path.Combine(scriptDir, file), bytes);
                progress?.Report((pct, $"已下载 {file}"));
            }

            progress?.Report((100, "安装完成"));
            return true;
        }
        catch (Exception ex)
        {
            progress?.Report((0, $"直接下载失败，尝试备用方式: {ex.Message}"));
            return null; // fallback 到 zip 方式
        }
    }

        public async Task<bool> DownloadAndInstallAsync(
        RepoScriptInfo info,
        IProgress<(int Percent, string Message)>? progress = null)
    {
        if (string.IsNullOrEmpty(info.DownloadUrl)) return false;

        // 优先用 raw 逐文件下载（download_url 为 raw base 或含 /archive/ 时均支持）
        var rawInstalled = await TryInstallFromRawAsync(info, progress);
        if (rawInstalled.HasValue) return rawInstalled.Value;
        // fallback 到 zip 方式（download_url 为完整 zip 地址时）

        var scriptDir = GetScriptDir(info.Id);
        var tmpZip = Path.Combine(PathString.AppDataDir, $"_tmp_{info.Id}.zip");

        try
        {
            progress?.Report((0, $"正在下载 {info.Name}..."));
            var client = _httpClientFactory.CreateClient("GlobalClient");

            using var resp = await client.GetAsync(
                info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength ?? 0;
            await using var stream = await resp.Content.ReadAsStreamAsync();
            await using var file = File.Create(tmpZip);

            var buffer = new byte[8192];
            long downloaded = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (total > 0)
                    progress?.Report(((int)(downloaded * 80 / total),
                        $"下载中 {downloaded / 1024}KB / {total / 1024}KB"));
            }

            progress?.Report((80, "正在解压..."));

            if (Directory.Exists(scriptDir))
                Directory.Delete(scriptDir, true);
            Directory.CreateDirectory(scriptDir);

            ExtractZipStrippingTopDir(tmpZip, scriptDir, info.Id);

            progress?.Report((100, "安装完成"));
            return true;
        }
        catch (Exception ex)
        {
            if (Directory.Exists(scriptDir))
                Directory.Delete(scriptDir, true);
            progress?.Report((0, $"安装失败: {ex.Message}"));
            return false;
        }
        finally
        {
            if (File.Exists(tmpZip)) File.Delete(tmpZip);
        }
    }

    private static void ExtractZipStrippingTopDir(string zipPath, string destDir, string scriptId)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        // 找到脚本子目录前缀：zip 内路径形如 sra-scripts-main/repo/divergent_universe/...
        var prefix = "";
        foreach (var entry in archive.Entries)
        {
            var parts = entry.FullName.Split('/');
            // 寻找第一个包含 scriptId 的目录层级
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (!parts[i].Equals(scriptId, StringComparison.OrdinalIgnoreCase)) continue;
                // parts[0..i] 是要剥离的前缀
                prefix = string.Join("/", parts[..( i + 1)]) + "/";
                break;
            }
            if (!string.IsNullOrEmpty(prefix)) break;
        }

        if (string.IsNullOrEmpty(prefix))
            throw new InvalidOperationException(
                $"在 ZIP 中未找到脚本目录 '{scriptId}'，请确认脚本仓库结构为 repo/{scriptId}/");

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var relative = entry.FullName[prefix.Length..];
            if (string.IsNullOrEmpty(relative)) continue;

            var target = Path.Combine(destDir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }
    }

    public bool Uninstall(string scriptId)
    {
        var dir = GetScriptDir(scriptId);
        if (!Directory.Exists(dir)) return false;
        Directory.Delete(dir, true);
        return true;
    }

    // ===== 内部 JSON 映射类 =====

    private class ReposConfig
    {
        [JsonPropertyName("repos")]
        public List<ScriptRepo> Repos { get; set; } = [];
    }

    private class RepoIndex
    {
        [JsonPropertyName("scripts")]
        public List<RepoScriptJson>? Scripts { get; set; }
    }

    private class RepoScriptJson
    {
        [JsonPropertyName("id")]          public string? Id { get; set; }
        [JsonPropertyName("name")]        public string? Name { get; set; }
        [JsonPropertyName("version")]     public string? Version { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("author")]      public string? Author { get; set; }
        [JsonPropertyName("last_updated")]public string? LastUpdated { get; set; }
        [JsonPropertyName("download_url")]public string? DownloadUrl { get; set; }
        [JsonPropertyName("repo_path")]   public string? RepoPath { get; set; }
    }

    private class ManifestJson
    {
        [JsonPropertyName("id")]          public string? Id { get; set; }
        [JsonPropertyName("name")]        public string? Name { get; set; }
        [JsonPropertyName("version")]     public string? Version { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("author")]      public string? Author { get; set; }
        [JsonPropertyName("tasks")]       public List<TaskJson>? Tasks { get; set; }
    }

    private class TaskJson
    {
        [JsonPropertyName("name")]  public string? Name { get; set; }
        [JsonPropertyName("entry")] public string? Entry { get; set; }
        [JsonPropertyName("class")] public string? Class { get; set; }
    }
}
