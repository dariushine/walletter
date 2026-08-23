using Walletter.Application.Common;
using Walletter.Domain;
using Walletter.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Walletter.Application.Rates;

public class RatesService
{
    private readonly IAppDbContext _db;

    public RatesService(IAppDbContext db)
    {
        _db = db;
    }

    private static string Today() => DateTime.UtcNow.ToString("yyyy-MM-dd");

    /// <summary>Resultado tipado de la tasa efectiva.</summary>
    public record EffectiveRate(string Date, decimal Bcv, decimal Paralelo, string? Note = null);

    /// <summary>
    /// Tasa del día (o de una fecha específica). Si no hay histórico real se
    /// devuelve la más reciente (fallback), igual que el backend previo.
    /// </summary>
    public async Task<EffectiveRate> Effective(string? date = null, CancellationToken ct = default)
    {
        var target = date ?? Today();
        var row = await _db.DailyRates.FirstOrDefaultAsync(r => r.Date == target, ct);
        if (row != null)
        {
            return new EffectiveRate(row.Date, Money.ToRateNum(row.Bcv), Money.ToRateNum(row.Paralelo));
        }
        var latest = await _db.DailyRates.OrderByDescending(r => r.Date).FirstOrDefaultAsync(ct);
        if (latest != null)
        {
            return new EffectiveRate(latest.Date, Money.ToRateNum(latest.Bcv), Money.ToRateNum(latest.Paralelo), "fallback a la tasa más reciente");
        }
        throw new BusinessException("No hay tasas disponibles");
    }

    public async Task<object> List(CancellationToken ct = default)
    {
        var rows = await _db.DailyRates.OrderByDescending(r => r.Date).Take(60).ToListAsync(ct);
        var data = rows.Select(r => (object)new
        {
            id = r.Id,
            date = r.Date,
            bcv = Money.ToRateNum(r.Bcv),
            paralelo = Money.ToRateNum(r.Paralelo),
            source = r.Source,
        }).ToList();
        return new { data, total = data.Count };
    }

    public async Task<object> Upsert(UpsertDailyRateCommand cmd, CancellationToken ct = default)
    {
        var existing = await _db.DailyRates.FirstOrDefaultAsync(r => r.Date == cmd.Date, ct);
        if (existing == null)
        {
            var row = new DailyRate
            {
                Date = cmd.Date,
                Bcv = Money.ToRateInt(cmd.Bcv),
                Paralelo = Money.ToRateInt(cmd.Paralelo),
                Source = cmd.Source ?? "manual",
                CreatedAt = DateTime.UtcNow,
            };
            _db.DailyRates.Add(row);
            await _db.SaveChangesAsync(ct);
            return row;
        }
        existing.Bcv = Money.ToRateInt(cmd.Bcv);
        existing.Paralelo = Money.ToRateInt(cmd.Paralelo);
        existing.Source = cmd.Source ?? "manual";
        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<object> GetById(int id, CancellationToken ct = default)
    {
        var row = await _db.DailyRates.FindAsync(new object?[] { id }, ct)
            ?? throw new NotFoundException("Tasa no encontrada");
        return new
        {
            id = row.Id,
            date = row.Date,
            bcv = Money.ToRateNum(row.Bcv),
            paralelo = Money.ToRateNum(row.Paralelo),
            source = row.Source,
        };
    }

    public async Task<object> UpdateById(int id, UpdateDailyRateCommand cmd, CancellationToken ct = default)
    {
        var existing = await _db.DailyRates.FindAsync(new object?[] { id }, ct)
            ?? throw new NotFoundException("Tasa no encontrada");
        if (cmd.Bcv is decimal b) existing.Bcv = Money.ToRateInt(b);
        if (cmd.Paralelo is decimal p) existing.Paralelo = Money.ToRateInt(p);
        if (cmd.Source != null) existing.Source = cmd.Source;
        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<object> DeleteById(int id, CancellationToken ct = default)
    {
        var existing = await _db.DailyRates.FindAsync(new object?[] { id }, ct)
            ?? throw new NotFoundException("Tasa no encontrada");
        _db.DailyRates.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return new { success = true };
    }
}
