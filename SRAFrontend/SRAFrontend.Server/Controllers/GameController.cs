using Microsoft.AspNetCore.Mvc;
using SRAFrontend.Services;

namespace SRAFrontend.Server.Controllers;

[ApiController]
[Route("[controller]")]
public class GameController(IBackendService backendService) : Controller
{
    [HttpGet("screenshot")]
    [EndpointSummary("截取游戏窗口")]
    [ProducesResponseType(200, Type = typeof(FileResult))]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetScreenshot()
    {
        var png = await backendService.GetGameScreenshotBytesAsync();
        if (png.Length == 0)
            return NotFound(new R(false, "Game window not found or capture failed."));

        return File(png, "image/png");
    }
}
