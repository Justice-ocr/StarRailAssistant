using System.Diagnostics;
using System.Net.Sockets;

namespace SRAFrontend.Server.Services;

public sealed class SraServerSupervisor(IConfiguration configuration)
{
    private readonly object _gate = new();
    private Process? _process;

    private int Port => configuration.GetValue("SraServer:Port", 5073);
    private string ExecutablePath => configuration.GetValue("SraServer:ExecutablePath", @"..\SRA-local-output\SRA-server.exe");
    private string WorkingDirectoryValue => configuration.GetValue("SraServer:WorkingDirectory", string.Empty);
    private string ApiKey => configuration.GetValue("SraServer:ApiKey", string.Empty);

    public async Task<object> GetStateAsync(CancellationToken ct)
    {
        var portOpen = await IsPortOpenAsync(ct);
        lock (_gate)
        {
            var running = (_process is { HasExited: false }) || portOpen;
            return new SraStatusDto(
                running,
                running ? _process?.Id : null,
                ResolveExecutablePath(ExecutablePath),
                Port,
                _process is { HasExited: false } ? "running" : (portOpen ? "listening" : "stopped"));
        }
    }

    public async Task<object> EnsureStartedAsync(CancellationToken ct)
    {
        if (await IsPortOpenAsync(ct))
            return new SraStartResult(true, false, null, "SRA server already listening");

        lock (_gate)
        {
            if (_process is not null && !_process.HasExited)
                return new SraStartResult(true, false, _process.Id, "SRA server already running");

            var exe = ResolveExecutablePath(ExecutablePath);
            if (!File.Exists(exe))
                return new SraStartResult(false, false, null, $"SRA server not found: {exe}");

            var workDir = string.IsNullOrWhiteSpace(WorkingDirectoryValue)
                ? Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory
                : ResolvePath(WorkingDirectoryValue);

            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = workDir,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (Port > 0)
                startInfo.ArgumentList.Add($"--urls=http://127.0.0.1:{Port}");
            if (!string.IsNullOrWhiteSpace(ApiKey))
                startInfo.Environment["ApiKey"] = ApiKey;

            _process = Process.Start(startInfo);
            return new SraStartResult(true, _process is not null, _process?.Id, "SRA server started");
        }
    }

    private static string ResolvePath(string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private string ResolveExecutablePath(string path) => ResolvePath(path);

    private async Task<bool> IsPortOpenAsync(CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync("127.0.0.1", Port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(250, ct));
            return completed == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
