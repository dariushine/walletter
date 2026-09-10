using Walletter.Application.Common;
using Walletter.Application.Rates;
using Walletter.Domain;
using Walletter.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Walletter.Application.Reports;

/// <summary>
/// Reportes financieros en USD. Cada transacción en VES se convierte a USD
/// usando la tasa efectiva de su fecha (BCV o paralelo según el toggle), no una
/// tasa global. Los exchanges se excluyen del resumen de ingresos/gastos y solo
/// se reportan en su propia sección. Los balances de billetera se muestran en su
/// moneda nativa.
///
/// Rango: el front puede pedir un periodo relativo ("month" = mes en curso,
/// "year" = año en curso), uno navegado ("month" + ref "2026-08", "year" + ref
/// "2024") o uno absoluto ("custom" + from/to YYYY-MM-DD).
/// Granularidad: cómo se agrupa el performance (day/month/year), independiente
/// del rango elegido.
/// </summary>
public class ReportsService
{
    private readonly IAppDbContext _db;
    private readonly RatesService _rates;

    public ReportsService(IAppDbContext db, RatesService rates)
    {
        _db = db;
        _rates = rates;
    }

    private static string DefaultTz() => TimeZoneHelper.DefaultTimeZone;

    public async Task<object> Overview(
        string? period,
        string? rateType,
        string? tz,
        string? refDate = null,   // "YYYY-MM" para month, "YYYY" para year
        string? from = null,      // YYYY-MM-DD (custom)
        string? to = null,        // YYYY-MM-DD (custom, inclusivo)
        string? granularity = null, // day | month | year
        string? sortBy = null,    // key | income | expense | net | transactionCount
        string? sortDir = null,   // asc | desc
        int? page = null,
        int? limit = null,
        CancellationToken ct = default)
    {
        var userTz = tz ?? DefaultTz();
        var useParalelo = string.Equals(rateType, "paralelo", StringComparison.OrdinalIgnoreCase);
        var gran = NormalizeGranularity(granularity);
        var today = TodayInTz(userTz);

        // Rango del reporte (bordes "de pared" del usuario).
        var range = ResolveRange(period, refDate, from, to, today, userTz);

        // --- Escritura del performance con la CLASIFICACIÓN ORIGINAL del servicio ---
        // (exchange_out/exchange_in y sus fees excluidos del resumen; el resto
        //  igual que antes: ingresos/gastos/neto/conteo agrupados por clave).
        var grouped = new SortedDictionary<string, Monthly>();
        decimal totalIncome = 0, totalExpense = 0;
        var byCat = new Dictionary<string, (string Name, decimal Total, int Count)>();

        var txns = await _db.Transactions
            .AsNoTracking()
            .Include(t => t.Wallet)
            .Include(t => t.Category)
            .Include(t => t.Parent).ThenInclude(p => p!.Category)
            .Where(t => !t.Deleted && t.DatetimeUtc >= range.Start && t.DatetimeUtc < range.End)
            .ToListAsync(ct);

        var rateCache = new Dictionary<string, decimal?>();
        async Task<decimal?> RateFor(string date)
        {
            if (rateCache.TryGetValue(date, out var v)) return v;
            var eff = await _rates.Effective(date, ct);
            var r = useParalelo ? (eff.Paralelo > 0 ? eff.Paralelo : eff.Bcv) : (eff.Bcv > 0 ? eff.Bcv : eff.Paralelo);
            rateCache[date] = r;
            return r;
        }

        foreach (var t in txns)
        {
            var cat = t.Category?.Name ?? "";
            var catLower = cat.ToLowerInvariant();
            var parentCat = t.Parent?.Category?.Name?.ToLowerInvariant();

            bool isExchangeTx = catLower is "exchange_out" or "exchange_in"
                || (catLower == "fee" && parentCat is "exchange_out" or "exchange_in");
            if (isExchangeTx) continue;

            var wall = TimeZoneHelper.UtcToWallClock(t.DatetimeUtc, userTz);
            var date = wall.Date;
            var amountUsd = await ToUsd(t, date, useParalelo, RateFor, ct);
            // Clave de agrupación del performance según granularidad.
            var key = gran switch
            {
                "day" => date,                              // YYYY-MM-DD
                "year" => date[..4],                        // YYYY
                _ => date[..7],                             // YYYY-MM (month)
            };

            if (t.Type == TransactionTypes.Income)
            {
                totalIncome += amountUsd;
                GetMonth(grouped, key).Income += amountUsd;
            }
            else
            {
                totalExpense += amountUsd;
                GetMonth(grouped, key).Expense += amountUsd;
            }
            var mc = GetMonth(grouped, key);
            mc.Count++;

            // Por categoría (solo gastos, como en el diseño).
            if (t.Type == TransactionTypes.Expense)
            {
                if (!byCat.TryGetValue(cat, out var catAgg)) catAgg = (cat, 0, 0);
                catAgg.Total += amountUsd;
                catAgg.Count++;
                byCat[cat] = catAgg;
            }
        }

        var performance = grouped.Select(kv => new PerformanceRow
        {
            Key = kv.Key,
            Income = Round(kv.Value.Income),
            Expense = Round(kv.Value.Expense),
            Net = Round(kv.Value.Income - kv.Value.Expense),
            TransactionCount = kv.Value.Count,
        }).ToList();

        if (performance.Count > 0 && sortBy == null)
        {
            // Orden por defecto: fecha-hora descendente (periodo más reciente primero),
            // aplica a cualquier granularidad (day/month/year).
            performance = performance
                .OrderByDescending(p => p.Key)
                .ToList();
        }
        else if (sortBy != null)
        {
            performance = ApplySort(performance, sortBy, sortDir);
        }

        // Tendencia: neto del periodo ANTERIOR en orden descendente (referencia usada
        // por el front para el chip de tendencia). Se calcula sobre la serie completa
        // (antes de paginar) para que sea correcta aunque la página no empiece en 1.
        decimal? prevNet = null;
        {
            var desc = performance
                .OrderByDescending(r => r.Key)
                .ToList();
            var firstKey = performance.Count > 0 ? performance[0].Key : null;
            for (var i = 0; i < desc.Count; i++)
            {
                if (firstKey != null && desc[i].Key == firstKey)
                {
                    if (i + 1 < desc.Count) prevNet = desc[i + 1].Net;
                    break;
                }
            }
        }

        // Paginación server-side: totalSIempre sobre la serie completa.
        var perfTotal = performance.Count;
        if (page != null && limit != null && limit > 0)
        {
            var start = (page.Value - 1) * limit.Value;
            performance = performance.Skip(start).Take(limit.Value).ToList();
        }

        var performanceDto = performance.Select(r => new
        {
            key = r.Key,
            income = r.Income,
            expense = r.Expense,
            net = r.Net,
            transactionCount = r.TransactionCount,
        }).ToList();

        var byCategory = byCat.Values
            .OrderByDescending(c => c.Total)
            .Select(c => (object)new { category = c.Name, count = c.Count, total = Round(c.Total) })
            .ToList();
        var byCategoryTotal = byCat.Values.Sum(c => c.Total);

        // Billeteras activas en su moneda nativa (no convertido).
        var wallets = await _db.Wallets.AsNoTracking()
            .Where(w => w.IsActive)
            .OrderBy(w => w.Name)
            .Select(w => new { name = w.Name, balance = w.Balance, currency = w.Currency })
            .ToListAsync(ct);
        var walletBalances = wallets.Select(w => (object)new
        {
            name = w.name,
            balance = Money.ToNum(w.balance),
            currency = w.currency,
        }).ToList();

        // Estadísticas de exchanges (en USD, convertidos por su fecha).
        var exchStats = await ExchangeStats(range.Start, range.End, useParalelo, userTz, RateFor, ct);

        var net = totalIncome - totalExpense;
        return new
        {
            summary = new
            {
                totalIncome = Round(totalIncome),
                totalExpenses = Round(totalExpense),
                totalTransactions = txns.Count(x => !IsExchangeTx(x)),
                net = Round(net),
                walletCount = walletBalances.Count,
            },
            performance = performanceDto,
            performanceTotal = perfTotal,
            // Compatibilidad: 'monthly' queda como alias de performance para no
            // romper consumidores viejos. El front nuevo usa 'performance'.
            monthly = performanceDto,
            byCategory,
            byCategoryTotal = Round(byCategoryTotal),
            walletBalances,
            exchangeStats = exchStats,
            meta = new
            {
                period = period,
                rateType = useParalelo ? "paralelo" : "bcv",
                from = range.FromWall == DateTime.MinValue ? null : range.FromWall.ToString("yyyy-MM-dd"),
                to = range.ToWall == DateTime.MinValue ? null : range.ToWall.ToString("yyyy-MM-dd"),
                granularity = gran,
                sortBy = sortBy,
                sortDir = NormalizeSortDir(sortDir),
                prevNet = prevNet,
            },
        };
    }

    private static bool IsExchangeTx(Transaction t)
    {
        var cat = t.Category?.Name?.ToLowerInvariant();
        if (cat is "exchange_out" or "exchange_in") return true;
        if (cat == "fee")
        {
            var pcat = t.Parent?.Category?.Name?.ToLowerInvariant();
            if (pcat is "exchange_out" or "exchange_in") return true;
        }
        return false;
    }

    private async Task<object> ExchangeStats(DateTime from, DateTime to, bool useParalelo, string userTz, Func<string, Task<decimal?>> rateFor, CancellationToken ct)
    {
        var exchs = await _db.Exchanges
            .AsNoTracking()
            .Include(e => e.From)
            .Include(e => e.To)
            .Where(e => !e.Deleted && e.CreatedAt >= from && e.CreatedAt <= to)
            .ToListAsync(ct);

        decimal totalFee = 0, totalFrom = 0, totalTo = 0;
        foreach (var e in exchs)
        {
            var date = TimeZoneHelper.UtcToWallClock(e.CreatedAt, userTz).Date;
            var fromCurrency = e.From.Currency.ToUpperInvariant();
            var toCurrency = e.To.Currency.ToUpperInvariant();

            totalFrom += await AmountToUsd(e.FromAmount, fromCurrency, date, useParalelo, rateFor, ct);
            totalTo += await AmountToUsd(e.ToAmount, toCurrency, date, useParalelo, rateFor, ct);
            // Comisiones: débito se paga en moneda de origen, crédito en moneda de destino.
            totalFee += await AmountToUsd(e.Fee, fromCurrency, date, useParalelo, rateFor, ct);
            totalFee += await AmountToUsd(e.CreditFee, toCurrency, date, useParalelo, rateFor, ct);
        }

        return new
        {
            totalExchanges = exchs.Count,
            totalFromAmount = Round(totalFrom),
            totalToAmount = Round(totalTo),
            totalFee = Round(totalFee),
        };
    }

    private static async Task<decimal> ToUsd(Transaction t, string date, bool useParalelo, Func<string, Task<decimal?>> rateFor, CancellationToken ct)
    {
        var cur = t.Wallet?.Currency?.ToUpperInvariant() ?? "USD";
        return await AmountToUsd(t.Amount, cur, date, useParalelo, rateFor, ct);
    }

    private static async Task<decimal> AmountToUsd(int amountUnits, string currency, string date, bool useParalelo, Func<string, Task<decimal?>> rateFor, CancellationToken ct)
    {
        if (currency != "VES") return Money.ToNum(amountUnits);
        var r = await rateFor(date);
        if (r is null || r.Value <= 0) return 0;
        return Money.ToNum(amountUnits) / r.Value;
    }

    private static Monthly GetMonth(SortedDictionary<string, Monthly> map, string key)
    {
        if (!map.TryGetValue(key, out var m)) { m = new Monthly(); map[key] = m; }
        return m;
    }

    private static decimal Round(decimal v) => Math.Round(v, 2);

    private sealed class PerformanceRow
    {
        public string Key { get; set; } = "";
        public decimal Income { get; set; }
        public decimal Expense { get; set; }
        public decimal Net { get; set; }
        public int TransactionCount { get; set; }
    }

    private static string NormalizeSortDir(string? sortDir)
        => string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";

    /// <summary>Ordena la serie de performance por columna (server-side).</summary>
    private static List<PerformanceRow> ApplySort(List<PerformanceRow> rows, string? sortBy, string? sortDir)
    {
        var desc = NormalizeSortDir(sortDir) == "desc";
        return sortBy?.ToLowerInvariant() switch
        {
            "key" => desc
                ? rows.OrderByDescending(r => r.Key).ToList()
                : rows.OrderBy(r => r.Key).ToList(),
            "income" => desc
                ? rows.OrderByDescending(r => r.Income).ThenBy(r => r.Key).ToList()
                : rows.OrderBy(r => r.Income).ThenBy(r => r.Key).ToList(),
            "expense" => desc
                ? rows.OrderByDescending(r => r.Expense).ThenBy(r => r.Key).ToList()
                : rows.OrderBy(r => r.Expense).ThenBy(r => r.Key).ToList(),
            "net" => desc
                ? rows.OrderByDescending(r => r.Net).ThenBy(r => r.Key).ToList()
                : rows.OrderBy(r => r.Net).ThenBy(r => r.Key).ToList(),
            "transactionCount" => desc
                ? rows.OrderByDescending(r => r.TransactionCount).ThenBy(r => r.Key).ToList()
                : rows.OrderBy(r => r.TransactionCount).ThenBy(r => r.Key).ToList(),
            _ => desc
                ? rows.OrderByDescending(r => r.Key).ToList()
                : rows.OrderBy(r => r.Key).ToList(),
        };
    }

    private sealed class Monthly
    {
        public decimal Income { get; set; }
        public decimal Expense { get; set; }
        public int Count { get; set; }
    }

    private static string TodayInTz(string tz)
        => TimeZoneHelper.UtcToWallClock(DateTime.UtcNow, tz).Date;

    /// <summary>Normaliza la granularidad pedida (day|month|year), default: month.</summary>
    private static string NormalizeGranularity(string? granularity)
        => granularity?.ToLowerInvariant() switch
        {
            "day" => "day",
            "year" => "year",
            _ => "month",
        };

    /// <summary>
    /// Resuelve el rango del reporte en instantes UTC (lo mismo que
    /// TransactionsService) para que la comparación contra DatetimeUtc (guardado
    /// en UTC) sea correcta.
    ///
    /// period:
    ///   "month" -> mes de refDate ("YYYY-MM", default: mes en curso). Si es el
    ///              mes en curso, corta en hoy (1ro → hoy); si es pasado, mes completo.
    ///   "year"  -> año de refDate ("YYYY", default: año en curso). Si es el año
    ///              en curso, corta en hoy (1ro ene → hoy); si es pasado, año completo.
    ///   "custom"-> from..to (YYYY-MM-DD, inclusivo).
    ///   viejos ("1m","3m","6m","1y","all") -> rolling hasta hoy (compatibilidad).
    ///
    /// Start/End son instantes UTC; FromWall/ToWall son las fechas "de pared"
    /// (para el meta, en la zona del usuario).
    /// </summary>
    private static (DateTime Start, DateTime End, DateTime FromWall, DateTime ToWall) ResolveRange(
        string? period, string? refDate, string? from, string? to, string todayStr, string tz)
    {
        var today = DateTime.ParseExact(todayStr, "yyyy-MM-dd", null).Date;

        switch (period?.ToLowerInvariant())
        {
            case "month":
            {
                var (y, m) = ParseMonthRef(refDate, today);
                var startWall = new DateTime(y, m, 1);
                // Periodo en curso: hasta hoy (exclusivo: hoy+1). Pasado: mes completo.
                var isCurrent = y == today.Year && m == today.Month;
                var endWall = isCurrent ? today.AddDays(1) : startWall.AddMonths(1);
                return (ToUtc(startWall, tz), ToUtc(endWall, tz), startWall, endWall.AddDays(-1));
            }
            case "year":
            {
                var y = ParseYearRef(refDate, today);
                var startWall = new DateTime(y, 1, 1);
                // Periodo en curso: hasta hoy (exclusivo: hoy+1). Pasado: año completo.
                var isCurrent = y == today.Year;
                var endWall = isCurrent ? today.AddDays(1) : startWall.AddYears(1);
                return (ToUtc(startWall, tz), ToUtc(endWall, tz), startWall, endWall.AddDays(-1));
            }
            case "custom":
            {
                var startWall = ParseDate(from, DateTime.MinValue);
                var endWall = ParseDate(to, today);
                if (endWall < startWall) (startWall, endWall) = (endWall, startWall);
                var endExclusive = endWall.AddDays(1);
                // Sin 'from' no hay borde inferior real: usar MinValue sin convertir (evita DST en año 1).
                if (startWall == DateTime.MinValue)
                    return (DateTime.MinValue, ToUtc(endExclusive, tz), startWall, endWall);
                return (ToUtc(startWall, tz), ToUtc(endExclusive, tz), startWall, endWall);
            }
            default:
            {
                // Rolling (compatibilidad): incluye el mes actual y los N-1 anteriores.
                // end = día siguiente a hoy (exclusivo).
                var endWall = today.AddDays(1);
                int months = period?.ToLowerInvariant() switch
                {
                    "1m" => 1,
                    "3m" => 3,
                    "6m" => 6,
                    "1y" => 12,
                    _ => 0, // all / null
                };
                DateTime startWall;
                if (months == 0)
                {
                    startWall = DateTime.MinValue;
                    return (DateTime.MinValue, ToUtc(endWall, tz), startWall, today);
                }
                startWall = today.AddMonths(-(months - 1)).AddDays(-today.Day + 1);
                return (ToUtc(startWall, tz), ToUtc(endWall, tz), startWall, today);
            }
        }
    }

    /// <summary>Convierte una fecha "de pared" (00:00 en la zona del usuario) a instante UTC.</summary>
    private static DateTime ToUtc(DateTime wallDate, string tz)
        => TimeZoneHelper.ToUtcInstant(wallDate.ToString("yyyy-MM-dd"), "00:00", tz);

    /// <summary>Parsea "YYYY-MM" (default: mes en curso).</summary>
    private static (int Year, int Month) ParseMonthRef(string? refDate, DateTime today)
    {
        if (!string.IsNullOrWhiteSpace(refDate))
        {
            var parts = refDate.Split('-');
            if (parts.Length == 2
                && int.TryParse(parts[0], out var y)
                && int.TryParse(parts[1], out var m)
                && m >= 1 && m <= 12)
                return (y, m);
        }
        return (today.Year, today.Month);
    }

    /// <summary>Parsea "YYYY" (default: año en curso).</summary>
    private static int ParseYearRef(string? refDate, DateTime today)
    {
        if (!string.IsNullOrWhiteSpace(refDate)
            && int.TryParse(refDate, out var y)
            && y >= 1 && y <= 9999)
            return y;
        return today.Year;
    }

    /// <summary>Parsea YYYY-MM-DD con fallback a un valor por defecto.</summary>
    private static DateTime ParseDate(string? date, DateTime fallback)
    {
        if (!string.IsNullOrWhiteSpace(date)
            && DateTime.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d))
            return d.Date;
        return fallback.Date;
    }

    public class PerformanceResponse
    {
        public List<Monthly> Performance { get; set; } = new();
        public int PerformanceTotal { get; set; }
        public Meta? Meta { get; set; }
    }

    public async Task<PerformanceResponse> PerformanceOnly(
        string? period,
        string? rateType,
        string? tz,
        string? refDate = null,
        string? from = null,
        string? to = null,
        string? granularity = null,
        string? sortBy = null,
        string? sortDir = null,
        int? page = null,
        int? limit = null,
        CancellationToken ct = default)
    {
        var userTz = tz ?? DefaultTz();
        var useParalelo = string.Equals(rateType, "paralelo", StringComparison.OrdinalIgnoreCase);
        var gran = NormalizeGranularity(granularity);
        var today = TodayInTz(userTz);
        var range = ResolveRange(period, refDate, from, to, today, userTz);
        var txns = await _db.Transactions
            .AsNoTracking()
            .Include(t => t.Wallet)
            .Include(t => t.Category)
            .Include(t => t.Parent).ThenInclude(p => p!.Category)
            .Where(t => !t.Deleted && t.DatetimeUtc >= range.Start && t.DatetimeUtc < range.End)
            .ToListAsync(ct);

        var rateCache = new Dictionary<string, decimal?>();
        async Task<decimal?> RateFor(string date)
        {
            if (rateCache.TryGetValue(date, out var v)) return v;
            var eff = await _rates.Effective(date, ct);
            var r = useParalelo ? (eff.Paralelo > 0 ? eff.Paralelo : eff.Bcv) : (eff.Bcv > 0 ? eff.Bcv : eff.Paralelo);
            rateCache[date] = r;
            return r;
        }

        var grouped = new SortedDictionary<string, Monthly>();
        decimal totalIncome = 0, totalExpense = 0;
        // Solo transacciones no exchange, no fee
        foreach (var t in txns.Where(t => t.Category?.SystemName != "exchange_out" && t.Category?.SystemName != "exchange_in" && t.Category?.SystemName != "fee" && t.ParentId == null))
        {
            var local = TimeZoneHelper.FromUtc(t.DatetimeUtc, userTz);
            var key = gran switch
            {
                "day" => local.ToString("yyyy-MM-dd"),
                "year" => local.ToString("yyyy"),
                _ => local.ToString("yyyy-MM"),
            };
            var usd = await ConvertToUsd(t.Amount, t.Wallet.Currency, local.ToString("yyyy-MM-dd"), RateFor);
            if (!usd.HasValue) continue;
            if (!grouped.TryGetValue(key, out var m))
                m = grouped[key] = new Monthly { Key = key, Income = 0, Expense = 0, Net = 0, TransactionCount = 0 };
            m.TransactionCount++;
            if (t.Type == TransactionType.Income)
            {
                m.Income += usd.Value;
                totalIncome += usd.Value;
            }
            else
            {
                m.Expense += usd.Value;
                totalExpense += usd.Value;
            }
            m.Net = m.Income - m.Expense;
        }

        var performance = grouped.Values.ToList();
        // Ordenar
        var sortProp = (sortBy ?? "key").ToLowerInvariant();
        var dir = (sortDir ?? "desc").ToLowerInvariant() == "desc" ? -1 : 1;
        performance.Sort((a, b) =>
        {
            int cmp = sortProp switch
            {
                "income" => a.Income.CompareTo(b.Income),
                "expense" => a.Expense.CompareTo(b.Expense),
                "net" => a.Net.CompareTo(b.Net),
                "transactioncount" => a.TransactionCount.CompareTo(b.TransactionCount),
                _ => string.CompareOrdinal(a.Key, b.Key),
            };
            return cmp * dir;
        });

        // Paginación
        var total = performance.Count;
        if (page.HasValue && limit.HasValue && limit.Value > 0)
        {
            var skip = (page.Value - 1) * limit.Value;
            performance = performance.Skip(skip).Take(limit.Value).ToList();
        }

        // PrevNet: neto del periodo anterior (misma granularidad)
        decimal? prevNet = null;
        if (gran == "month" && period == "month" && refDate != null)
        {
            // Lógica simplificada: calcular neto del mes anterior
            var (y, m) = ParseMonthRef(refDate, today);
            var prevDate = new DateTime(y, m, 1).AddMonths(-1);
            var prevKey = prevDate.ToString("yyyy-MM");
            var prev = grouped.TryGetValue(prevKey, out var p) ? p.Net : 0;
            prevNet = prev;
        }
        // Nota: para año/gran day se puede expandir después

        return new PerformanceResponse
        {
            Performance = performance,
            PerformanceTotal = total,
            Meta = new Meta { PrevNet = prevNet }
        };
    }
}
