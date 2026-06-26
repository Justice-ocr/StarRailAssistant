namespace SRAFrontend.Server.Config;

public sealed class AppSettings
{
    public WebHostOptions WebUi { get; set; } = new();
    public SraServerOptions SraServer { get; set; } = new();
    public LogOptions Logs { get; set; } = new();
}

public sealed class WebHostOptions
{
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5074;
    public bool AutoOpenBrowser { get; set; } = true;
    public bool RemoteAccess { get; set; } = false;
    public string AuthToken { get; set; } = "";
    public bool ReadonlyMode { get; set; } = false;
}

public sealed class SraServerOptions
{
    public string ExecutablePath { get; set; } = @"..\SRA-local-output\SRA-server.exe";
    public string WorkingDirectory { get; set; } = "";
    public int Port { get; set; } = 5073;
    public string ApiKey { get; set; } = "";
    public bool AutoStart { get; set; } = true;
}

public sealed class LogOptions
{
    public int TailLines { get; set; } = 300;
    public bool StreamEnabled { get; set; } = true;
}
