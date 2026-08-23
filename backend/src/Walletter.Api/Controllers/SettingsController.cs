using Walletter.Application.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Walletter.Api.Controllers;

[ApiController]
[Route("settings")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly SettingsService _service;

    public SettingsController(SettingsService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(await _service.Get(ct));

    // El frontend usa PUT /api/settings/user_timezone
    [HttpPut("user_timezone")]
    public async Task<IActionResult> SetTimeZone([FromBody] SetTimezoneDto dto, CancellationToken ct)
        => Ok(await _service.SetTimeZone(dto.Timezone, ct));

    // Forma alternativa: PUT /api/settings
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] SetTimezoneDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Timezone))
            throw new Walletter.Application.Common.BusinessException("timezone requerida");
        return Ok(await _service.SetTimeZone(dto.Timezone, ct));
    }
}

public class SetTimezoneDto
{
    public string Timezone { get; set; } = string.Empty;
}
