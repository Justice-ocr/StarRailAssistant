namespace SRAFrontend.Server.Services;

public sealed record SraStatusDto(bool Running, int? Pid, string ExecutablePath, int Port, string? Detail = null);

public sealed record SraStartResult(bool Ok, bool Started, int? Pid, string Message);
