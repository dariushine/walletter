using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Walletter.Api.Controllers;

[ApiController]
[Route("health")]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Health()
        => Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            service = "Finance API (ASP.NET Core + EF Core)",
            version = "2.0.0",
        });
}
