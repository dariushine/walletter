using System.Net.Http;
using System.Text.Json;
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

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Resultado tipado de la tasa efectiva.</summary>
    public record EffectiveRate(string Date, decimal Bcv, decimal Paralelo, string? Note = null);

    private static string TodayInTz()
    {
        var (d, _) = TimeZoneHelper.UtcToWallClock(DateTime.UtcNow, TimeZoneHelper.DefaultTimeZone);
        return d;
    }

    /// <summary>
    /// Tasa del día (o de una fecha específica). Estrategia:
    /// 1. Consulta la BDD en la fecha objetivo. Si existe y tiene BCV real, la devuelve.
    /// 2. Si no existe o está marcada como día sin BCV (bcv == 0), retrocede un día
    ///    y repite, consultando a dolarapi para fechas recientes.
    /// 3. Al obtener de dolarapi, guarda la tasa en la BDD con su fecha real. Si esa
    ///    fecha no trae BCV (día sin tasa, p.ej. fin de semana), guarda bcv = 0 para
    ///    marcarlo y así la próxima vez que se consulte sepa saltar a un día anterior.
    /// 4. Fallback: última tasa guardada con valor real.
    /// </summary>
    public async Task<EffectiveRate> Effective(string? date = null, CancellationToken ct = default)
    {
        var target = DateTime.ParseExact(date ?? TodayInTz(), "yyyy-MM-dd", null);

        for (var i = 0; i < 92; i++)
        {
            var day = target.AddDays(-i);
            var dayStr = day.ToString("yyyy-MM-dd");
            var row = await _db.DailyRates.FirstOrDefaultAsync(r => r.Date == dayStr, ct);

            if (row == null)
            {
                // No guardada: intenta obtenerla (solo tiene sentido para fechas recientes).
                var rate = await FetchAndStore(day, ct);
                if (rate != null)
                {
                    if (rate.Value.Bcv > 0) // BCV presente → tasa real del día.
                        return new EffectiveRate(dayStr, rate.Value.Bcv, rate.Value.Paralelo);
                    // Solo paralela (bcv == 0): día sin BCV ya quedó marcado en BDD. Retrocede.
                    continue;
                }
                continue; // origen no disponible; sigue retrocediendo.
            }

            if (row.Bcv > 0) // Tasa real guardada.
                return new EffectiveRate(row.Date, Money.ToRateNum(row.Bcv), Money.ToRateNum(row.Paralelo));
            if (row.Paralelo > 0)
                continue; // Solo paralela (marcada como día sin BCV) → retrocede.
            // bcv == 0 y paralelo == 0: placeholder → retrocede.
        }

        var latest = await _db.DailyRates
            .Where(r => r.Bcv > 0 || r.Paralelo > 0)
            .OrderByDescending(r => r.Date)
            .FirstOrDefaultAsync(ct);
        if (latest != null)
            return new EffectiveRate(latest.Date, Money.ToRateNum(latest.Bcv), Money.ToRateNum(latest.Paralelo), "fallback a la tasa más reciente");

        throw new BusinessException("No hay tasas disponibles");
    }

    /// <summary>Consulta dolarapi y persiste la tasa. Devuelve null si no hay datos.</summary>
    private async Task<(decimal Bcv, decimal Paralelo)?> FetchAndStore(DateTime day, CancellationToken ct)
    {
        var (bcv, paralelo, fecha) = await FetchFromDolarApi(ct);
        if (bcv == 0 && paralelo == 0) return null;

        // Guarda con la fecha real de la fuente (si existe), si no con el día consultado.
        var dateStr = (fecha ?? day).ToString("yyyy-MM-dd");
        var existing = await _db.DailyRates.FirstOrDefaultAsync(r => r.Date == dateStr, ct);
        if (existing == null)
        {
            _db.DailyRates.Add(new DailyRate
            {
                Date = dateStr,
                Bcv = Money.ToRateInt(bcv),
                Paralelo = Money.ToRateInt(paralelo),
                Source = "dolarapi",
                CreatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.Bcv = Money.ToRateInt(bcv);
            existing.Paralelo = Money.ToRateInt(paralelo);
            existing.Source = "dolarapi";
        }
        await _db.SaveChangesAsync(ct);
        return (bcv, paralelo);
    }

    /// <summary>Obtiene BCV (oficial) y paralelo desde dolarapi.</summary>
    private async Task<(decimal Bcv, decimal Paralelo, DateTime? Fecha)> FetchFromDolarApi(CancellationToken ct)
    {
        decimal bcv = 0, paralelo = 0;
        DateTime? fecha = null;

        var oficial = await GetDolar(ct, "oficial");
        if (oficial != null)
        {
            bcv = oficial.Avg;
            fecha = oficial.Fecha;
        }

        var paral = await GetDolar(ct, "paralelo");
        if (paral != null)
        {
            paralelo = paral.Avg;
            if (fecha == null) fecha = paral.Fecha;
        }
        else if (bcv > 0)
        {
            // Si no hay paralelo propio, usa 'blue' como aproximación.
            var blue = await GetDolar(ct, "blue");
            if (blue != null) paralelo = blue.Avg;
        }

        return (bcv, paralelo, fecha);
    }

    private record DolarRate(decimal Avg, DateTime? Fecha);

    private async Task<DolarRate?> GetDolar(CancellationToken ct, string casa)
    {
        try
        {
            using var resp = await _http.GetAsync($"https://dolarapi.com/v1/dolares/{casa}", ct);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var compra = doc.RootElement.TryGetProperty("compra", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDecimal() : 0;
            var venta = doc.RootElement.TryGetProperty("venta", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0;
            if (compra <= 0 && venta <= 0) return null;
            var avg = compra > 0 && venta > 0 ? (compra + venta) / 2m : Math.Max(compra, venta);
            DateTime? f = null;
            if (doc.RootElement.TryGetProperty("fechaActualizacion", out var fa) && fa.ValueKind == JsonValueKind.String
                && DateTime.TryParse(fa.GetString(), out var parsed))
                f = parsed;
            return new DolarRate(avg, f);
        }
        catch
        {
            return null;
        }
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
