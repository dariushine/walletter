using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace Walletter.Api.Controllers;

[ApiController]
[Route("health")]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Health()
    {
        var asm = Assembly.GetExecutingAssembly();
        var infoVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? asm.GetName().Version?.ToString() ?? "unknown";

        return Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            service = "Walletter API (ASP.NET Core + EF Core)",
            version = asm.GetName().Version?.ToString(),
            informationalVersion = infoVersion,
            commit = infoVersion.Split('+').Length > 1 ? infoVersion.Split('+')[1] : null,
        });
    }
}
