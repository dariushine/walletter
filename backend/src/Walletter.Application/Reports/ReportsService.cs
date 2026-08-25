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

    public async Task<object> Overview(string? period, string? rateType, string? tz, CancellationToken ct = default)
    {
        var userTz = tz ?? DefaultTz();
        var useParalelo = string.Equals(rateType, "paralelo", StringComparison.OrdinalIgnoreCase);
        var today = TodayInTz(userTz);
        var from = ResolveFrom(period, today, userTz);

        // Transacciones del rango con su categoría (para excluir exchanges y sus
        // comisiones). Se proyecta a la zona del usuario para conocer la fecha real.
        var txns = await _db.Transactions
            .AsNoTracking()
            .Include(t => t.Wallet)
            .Include(t => t.Category)
            .Include(t => t.Parent).ThenInclude(p => p!.Category)
            .Where(t => !t.Deleted && t.DatetimeUtc >= from.Start && t.DatetimeUtc < from.End)
            .ToListAsync(ct);

        // Cache de tasas por fecha (evita re-consultar la misma fecha).
        var rateCache = new Dictionary<string, decimal?>();
        async Task<decimal?> RateFor(string date)
        {
            if (rateCache.TryGetValue(date, out var v)) return v;
            var eff = await _rates.Effective(date, ct);
            var r = useParalelo ? (eff.Paralelo > 0 ? eff.Paralelo : eff.Bcv) : (eff.Bcv > 0 ? eff.Bcv : eff.Paralelo);
            rateCache[date] = r;
            return r;
        }

        // Clasifica cada transacción: valores "efectivos" (tipo, monto USD, fecha).
        // Excluye débitos/créditos de exchange y sus comisiones del resumen.
        var monthly = new SortedDictionary<string, Monthly>();
        decimal totalIncome = 0, totalExpense = 0;
        var byCat = new Dictionary<string, (string Name, decimal Total, int Count)>();

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
            var monthKey = date[..7];

            if (t.Type == TransactionTypes.Income)
            {
                totalIncome += amountUsd;
                GetMonth(monthly, monthKey).Income += amountUsd;
            }
            else
            {
                totalExpense += amountUsd;
                GetMonth(monthly, monthKey).Expense += amountUsd;
            }
            var mc = GetMonth(monthly, monthKey);
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

        // Monthly ordenado y neto calculado.
        var monthlyList = monthly.Select(kv => new
        {
            month = kv.Key,
            income = Round(kv.Value.Income),
            expense = Round(kv.Value.Expense),
            net = Round(kv.Value.Income - kv.Value.Expense),
            transactionCount = kv.Value.Count,
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
        var exchStats = await ExchangeStats(from.Start, from.End, useParalelo, userTz, RateFor, ct);

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
            monthly = monthlyList,
            byCategory,
            byCategoryTotal = Round(byCategoryTotal),
            walletBalances,
            exchangeStats = exchStats,
            meta = new { period = period, rateType = useParalelo ? "paralelo" : "bcv", from = from.Start.ToString("yyyy-MM-dd"), to = from.End.ToString("yyyy-MM-dd") },
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

    private sealed class Monthly
    {
        public decimal Income { get; set; }
        public decimal Expense { get; set; }
        public int Count { get; set; }
    }

    private static string TodayInTz(string tz)
        => TimeZoneHelper.UtcToWallClock(DateTime.UtcNow, tz).Date;

    /// <summary>
    /// Resuelve el rango en instantes UTC (lo mismo que TransactionsService)
    /// para que la comparación contra DatetimeUtc (guardado en UTC) sea correcta.
    /// start = primer día del periodo (00:00 hora local del usuario → UTC).
    /// end   = día siguiente a hoy (00:00 hora local del usuario → UTC), EXCLUSIVO.
    /// </summary>
    private static (DateTime Start, DateTime End) ResolveFrom(string? period, string todayStr, string tz)
    {
        // 'Hoy' (hora local del usuario) y el día siguiente (borde final, exclusivo).
        var today = DateTime.ParseExact(todayStr, "yyyy-MM-dd", null).Date;
        var end = today.AddDays(1);
        int months = period switch
        {
            "1m" => 1,
            "3m" => 3,
            "6m" => 6,
            "1y" => 12,
            _ => 0, // all
        };

        // 'Últimos N meses': incluye el mes actual y los N-1 anteriores (hasta hoy).
        var start = months == 0 ? DateTime.MinValue : today.AddMonths(-(months - 1)).AddDays(-today.Day + 1);

        // Convierte los bordes (hora local del usuario) a instantes UTC absolutos,
        // igual que TransactionsService.List hace con ToUtcInstant(fecha, "00:00", tz).
        DateTime startUtc = start == DateTime.MinValue
            ? DateTime.MinValue
            : TimeZoneHelper.ToUtcInstant(start.ToString("yyyy-MM-dd"), "00:00", tz);
        var endUtc = TimeZoneHelper.ToUtcInstant(end.ToString("yyyy-MM-dd"), "00:00", tz);
        return (startUtc, endUtc);
    }
}
