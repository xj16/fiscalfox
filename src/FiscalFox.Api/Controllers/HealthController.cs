using Microsoft.AspNetCore.Mvc;

namespace FiscalFox.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "ok",
        service = "FiscalFox.Api",
        utc = DateTime.UtcNow
    });
}
