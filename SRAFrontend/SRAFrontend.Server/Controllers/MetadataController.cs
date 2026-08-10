using Microsoft.AspNetCore.Mvc;
using SRAFrontend.Models;
using SRAFrontend.Services;

namespace SRAFrontend.Server.Controllers;

[ApiController]
[Route("[controller]")]
public class MetadataController(IBackendService backendService, ILogger<MetadataController> logger) : Controller
{
    private static readonly TimeSpan BackendStartTimeout = TimeSpan.FromSeconds(5);

    [HttpGet("trailblaze-power/tasks")]
    [ProducesResponseType(200)]
    [ProducesResponseType(503)]
    public async Task<IActionResult> GetTrailblazePowerTasks()
    {
        backendService.StartBackend("--inline --no-admin");
        if (!await WaitForBackendReadyAsync())
            return StatusCode(503, new R(false, "Backend failed to start while loading trailblaze power metadata."));

        var tasks = await backendService.GetTpConfigAsync();
        if (tasks.Length == 0)
        {
            logger.LogWarning("Trailblaze power metadata returned no tasks.");
            return StatusCode(503, new R(false, "Trailblaze power metadata is unavailable."));
        }

        return Ok(tasks.Select((item, index) => new
        {
            index,
            item.Id,
            item.Name,
            item.Cost,
            item.MaxSingleTimes,
            Levels = item.Levels.Select(level => new
            {
                index = level.Id,
                name = level.Name
            })
        }));
    }

    private async Task<bool> WaitForBackendReadyAsync()
    {
        var deadline = DateTimeOffset.UtcNow + BackendStartTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await backendService.SendInputAsync("help"))
                return true;

            await Task.Delay(150);
        }

        return false;
    }
}
