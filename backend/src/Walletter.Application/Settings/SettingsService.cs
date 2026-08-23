using Walletter.Application.Common;
using Walletter.Domain;
using Walletter.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Walletter.Application.Settings;

public class SettingsService
{
    private readonly IAppDbContext _db;

    public SettingsService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<string> GetUserTimeZone(CancellationToken ct = default)
    {
        var s = await _db.Settings.FindAsync(new object?[] { "user_timezone" }, ct);
        if (s?.Value == null)
        {
            var setting = new Setting { Key = "user_timezone", Value = TimeZoneHelper.DefaultTimeZone, UpdatedAt = DateTime.UtcNow };
            _db.Settings.Add(setting);
            await _db.SaveChangesAsync(ct);
            return TimeZoneHelper.DefaultTimeZone;
        }
        return s.Value;
    }

    public async Task<object> Get(CancellationToken ct = default)
    {
        var tz = await GetUserTimeZone(ct);
        return new { timezone = tz, name = "Finance API", version = "2.0.0" };
    }

    public async Task<object> SetTimeZone(string tz, CancellationToken ct = default)
    {
        var existing = await _db.Settings.FindAsync(new object?[] { "user_timezone" }, ct);
        if (existing == null)
        {
            _db.Settings.Add(new Setting { Key = "user_timezone", Value = tz, UpdatedAt = DateTime.UtcNow });
        }
        else
        {
            existing.Value = tz;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        return new { success = true, timezone = tz };
    }
}
