namespace AcControl.Server.Controllers;

using AcControl.Server.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

[Route("api/ring/snapshots")]
[ApiController]
public class RingSnapshotsController : ControllerBase
{
    private readonly RingDevicesService mRingService;

    public RingSnapshotsController(RingDevicesService ringService)
    {
        mRingService = ringService;
    }

    [HttpGet("{doorbellId}")]
    public IActionResult Get(int doorbellId, [FromQuery] DateTime timestamp)
    {
        var device = mRingService.Devices.FirstOrDefault(device => device.Id == doorbellId);

        if (device is null)
        {
            return this.NotFound("device");
        }
        
        if (device.LatestSnapshotTime != null && Math.Abs((device.LatestSnapshotTime - timestamp).Value.TotalMinutes) > 5)
        {
            return this.NotFound("timestamp");
        }

        var content = device.LatestSnapshot;

        if (content is null)
        {
            return this.NotFound("snapshot");
        }

        return this.File(content, "image/jpg");
    }
}
