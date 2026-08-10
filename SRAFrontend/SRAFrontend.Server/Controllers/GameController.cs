using Microsoft.AspNetCore.Mvc;

namespace SRAFrontend.Server.Controllers;

[ApiController]
[Route("[controller]")]
public class GameController : Controller
{
    [HttpGet("screenshot")]
    [EndpointSummary("截取游戏窗口")]
    [ProducesResponseType(200, Type = typeof(FileResult))]
    [ProducesResponseType(404)]
    public IActionResult GetScreenshot()
    {
        if (!OperatingSystem.IsWindows())
            return NotFound(new R(false, "截图功能仅支持 Windows"));

        var png = Utils.GameScreenshot.CaptureGameWindowPng(out var error);
        if (png is null || png.Length == 0)
            return NotFound(new R(false, $"截图失败：{error}"));

        return File(png, "image/png");
    }
}
