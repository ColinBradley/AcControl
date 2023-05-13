namespace AcControl.Server.Controllers;

using AcControl.Server.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

[Route("api/ring/dings")]
[ApiController]
public class RingRecordingController : ControllerBase
{
    private readonly RingDevicesService mRingService;

    public RingRecordingController(RingDevicesService ringService)
    {
        mRingService = ringService;
    }

    [HttpGet("{dingId}")]
    public async Task<IActionResult> Get(string dingId)
    {
        var stream = await mRingService.Session.GetDoorbotHistoryRecording(dingId);
        
        return this.File(stream, "binary/octet-stream");
    }
}
