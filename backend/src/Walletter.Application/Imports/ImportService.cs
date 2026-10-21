using Walletter.Application.Common;
using Walletter.Application.Exchanges;
using Walletter.Application.Transactions;
using Walletter.Domain;
using Walletter.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Walletter.Application.Imports;

/// <summary>
/// Carga por lote (CSV) de transacciones y exchanges. Cada fila se registra
/// con la MISMA lógica que la creación individual (TransactionsService.Create /
/// ExchangesService.Create), así que los balances, fees y reglas de negocio
/// quedan idénticos. Una fila fallida no aborta el resto: se reporta por fila.
/// </summary>
public class ImportService
{
    private static readonly string[] DateHeaders = { "fecha", "date" };
    private static readonly string[] TimeHeaders = { "hora", "time" };
    private static readonly string[] CategoryHeaders = { "categoria", "category" };
    private static readonly string[] TypeHeaders = { "tipo", "type" };
    private static readonly string[] WalletHeaders = { "billetera", "wallet", "walletid", "billeteraid", "monedero", "cuenta" };
    private static readonly string[] AmountHeaders = { "monto", "amount", "cantidad", "importe", "valor" };
    private static readonly string[] DebitHeaders = { "montoenviado", "fromamount", "montoorigen", "enviado", "salida", "debito", "montoenvio" };
    private static readonly string[] CreditHeaders = { "montorecibido", "toamount", "montodestino", "recibido", "entrada", "credito", "montorecibo" };
    private static readonly string[] FromHeaders = { "origen", "from", "fromwallet", "desde", "billeteraorigen", "cuentaorigen" };
    private static readonly string[] ToHeaders = { "destino", "to", "towallet", "hacia", "billeteradestino", "cuentadestino" };
    private static readonly string[] FeeHeaders = { "fee", "comision", "comisiondebito", "feedebito", "comisionorigen" };
    private static readonly string[] CreditFeeHeaders = { "creditfee", "comisioncredito", "comisiondestino" };
    private static readonly string[] DescHeaders = { "descripcion", "description", "detalle", "nota", "concepto" };

    private readonly IAppDbContext _db;
    private readonly TransactionsService _transactions;
    private readonly ExchangesService _exchanges;

    public ImportService(IAppDbContext db, TransactionsService transactions, ExchangesService exchanges)
    {
        _db = db;
        _transactions = transactions;
        _exchanges = exchanges;
    }

    // ================= TRANSACCIONES =================

    public async Task<ImportResult> ImportTransactions(ImportTransactionsCommand cmd, CancellationToken ct = default)
    {
        var tz = string.IsNullOrWhiteSpace(cmd.Tz) ? TimeZoneHelper.DefaultTimeZone : cmd.Tz!;
        var rows = CsvReader.Parse(cmd.Csv);
        if (rows.Count == 0)
            throw new BusinessException("El CSV está vacío");

        var header = MapHeader(rows[0]);
        RequireColumn(header, "Fecha", DateHeaders);
        RequireColumn(header, "Tipo", TypeHeaders);
        RequireColumn(header, "Billetera", WalletHeaders);
        RequireColumn(header, "Monto", AmountHeaders);

        var wallets = await LoadWallets(ct);
        var results = new List<ImportRowResult>();
        int imported = 0;

        for (int i = 1; i < rows.Count; i++)
        {
            var rowNumber = i; // 1-based sin contar el encabezado
            var r = rows[i];
            try
            {
                var dateRaw = GetCell(r, header, DateHeaders);
                var date = ResolveDate(dateRaw, tz);
                var time = GetCell(r, header, TimeHeaders);
                if (!ImportParsing.TryParseTime(time, out var timeCanonical))
                    timeCanonical = "12:00";

                if (!ImportParsing.TryParseType(GetCell(r, header, TypeHeaders), out var type))
                    throw new BusinessException("Tipo inválido (usa ingreso/gasto, income/expense)");

                var wallet = ResolveWallet(GetCell(r, header, WalletHeaders), wallets);
                var amount = ResolvePositiveAmount(GetCell(r, header, AmountHeaders), "Monto");
                var category = (GetCell(r, header, CategoryHeaders) ?? string.Empty).Trim();
                if (category.Length == 0)
                    throw new BusinessException("Categoría vacía");
                if (Walletter.Domain.SystemCategories.IsSystem(category))
                    throw new BusinessException($"La categoría '{category}' es de sistema; usa el endpoint de exchanges para importar exchanges.");
                var fee = ImportParsing.ParseMoney(GetCell(r, header, FeeHeaders)) ?? 0m;
                var description = (GetCell(r, header, DescHeaders) ?? string.Empty).Trim();

                if (cmd.DryRun)
                {
                    results.Add(new ImportRowResult(rowNumber, true, null, new
                    {
                        date,
                        time = timeCanonical,
                        type,
                        wallet = wallet.Name,
                        category,
                        amount,
                        fee,
                        description,
                    }));
                    imported++;
                    continue;
                }

                var created = await _transactions.Create(new CreateTransactionCommand
                {
                    WalletId = wallet.Id,
                    CategoryName = category,
                    Type = type,
                    Amount = amount,
                    Fee = fee,
                    Description = description.Length == 0 ? null : description,
                    Date = date,
                    Time = timeCanonical,
                    Tz = tz,
                }, ct);

                results.Add(new ImportRowResult(rowNumber, true, null, new
                {
                    id = created.Id,
                    feeTransactionId = created.FeeTransactionId,
                    type,
                    wallet = wallet.Name,
                    category,
                    amount,
                    fee,
                    date,
                    time = timeCanonical,
                }));
                imported++;
            }
            catch (Exception ex)
            {
                results.Add(new ImportRowResult(rowNumber, false, ex.Message));
            }
        }

        return new ImportResult(cmd.DryRun, rows.Count - 1, imported, results.Count(x => !x.Ok), results);
    }

    // ================= EXCHANGES =================

    public async Task<ImportResult> ImportExchanges(ImportExchangesCommand cmd, CancellationToken ct = default)
    {
        var tz = string.IsNullOrWhiteSpace(cmd.Tz) ? TimeZoneHelper.DefaultTimeZone : cmd.Tz!;
        var rows = CsvReader.Parse(cmd.Csv);
        if (rows.Count == 0)
            throw new BusinessException("El CSV está vacío");

        var header = MapHeader(rows[0]);
        RequireColumn(header, "Fecha", DateHeaders);
        RequireColumn(header, "Billetera origen", FromHeaders);
        RequireColumn(header, "Billetera destino", ToHeaders);
        RequireColumn(header, "Monto enviado", DebitHeaders);
        RequireColumn(header, "Monto recibido", CreditHeaders);

        var wallets = await LoadWallets(ct);
        var results = new List<ImportRowResult>();
        int imported = 0;

        for (int i = 1; i < rows.Count; i++)
        {
            var rowNumber = i;
            var r = rows[i];
            try
            {
                var date = ResolveDate(GetCell(r, header, DateHeaders), tz);
                var time = GetCell(r, header, TimeHeaders);
                if (!ImportParsing.TryParseTime(time, out var timeCanonical))
                    timeCanonical = "12:00";

                var from = ResolveWallet(GetCell(r, header, FromHeaders), wallets);
                var to = ResolveWallet(GetCell(r, header, ToHeaders), wallets);
                if (from.Id == to.Id)
                    throw new BusinessException("Las billeteras origen y destino deben ser diferentes");

                var fromAmount = ResolvePositiveAmount(GetCell(r, header, DebitHeaders), "Monto enviado");
                var toAmount = ResolvePositiveAmount(GetCell(r, header, CreditHeaders), "Monto recibido");
                var fee = ImportParsing.ParseMoney(GetCell(r, header, FeeHeaders)) ?? 0m;
                var creditFee = ImportParsing.ParseMoney(GetCell(r, header, CreditFeeHeaders)) ?? 0m;
                var description = (GetCell(r, header, DescHeaders) ?? string.Empty).Trim();

                if (cmd.DryRun)
                {
                    results.Add(new ImportRowResult(rowNumber, true, null, new
                    {
                        date,
                        time = timeCanonical,
                        fromWallet = from.Name,
                        toWallet = to.Name,
                        fromAmount,
                        toAmount,
                        fee,
                        creditFee,
                        description,
                    }));
                    imported++;
                    continue;
                }

                await _exchanges.Create(new CreateExchangeCommand
                {
                    FromWalletId = from.Id,
                    ToWalletId = to.Id,
                    FromAmount = fromAmount,
                    ToAmount = toAmount,
                    Fee = fee,
                    CreditFee = creditFee,
                    Description = description.Length == 0 ? null : description,
                    Date = date,
                    Time = timeCanonical,
                    Tz = tz,
                }, ct);

                results.Add(new ImportRowResult(rowNumber, true, null, new
                {
                    fromWallet = from.Name,
                    toWallet = to.Name,
                    fromAmount,
                    toAmount,
                    fee,
                    creditFee,
                    date,
                    time = timeCanonical,
                }));
                imported++;
            }
            catch (Exception ex)
            {
                results.Add(new ImportRowResult(rowNumber, false, ex.Message));
            }
        }

        return new ImportResult(cmd.DryRun, rows.Count - 1, imported, results.Count(x => !x.Ok), results);
    }

    // ================= helpers =================

    private async Task<List<Wallet>> LoadWallets(CancellationToken ct) =>
        await _db.Wallets.AsNoTracking().Where(w => w.IsActive).ToListAsync(ct);

    /// <summary>Resuelve la billetera por id numérico o por nombre/alias (case-insensitive).</summary>
    private static Wallet ResolveWallet(string? raw, List<Wallet> wallets)
    {
        var s = (raw ?? string.Empty).Trim();
        if (s.Length == 0) throw new BusinessException("Billetera vacía");

        if (int.TryParse(s, out var id))
        {
            var byId = wallets.FirstOrDefault(w => w.Id == id);
            if (byId != null) return byId;
            throw new BusinessException($"Billetera con id {id} no encontrada");
        }

        var match = wallets.FirstOrDefault(w =>
            string.Equals(w.Name?.Trim(), s, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(w.Alias) && string.Equals(w.Alias.Trim(), s, StringComparison.OrdinalIgnoreCase)));
        if (match != null) return match;
        throw new BusinessException($"Billetera '{s}' no encontrada");
    }

    private static string ResolveDate(string? raw, string tz)
    {
        if (!ImportParsing.TryParseDate(raw, out var date))
            throw new BusinessException($"Fecha inválida '{raw}' (usa YYYY-MM-DD o DD/MM/YYYY)");
        return date;
    }

    private static decimal ResolvePositiveAmount(string? raw, string field)
    {
        var value = ImportParsing.ParseMoney(raw);
        if (value is null) throw new BusinessException($"{field} inválido o vacío");
        if (value.Value <= 0) throw new BusinessException($"{field} debe ser mayor a 0");
        return value.Value;
    }

    /// <summary>Mapea cada alias de encabezado a su índice de columna (normalizado).</summary>
    private static Dictionary<string, int> MapHeader(List<string> headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < headerRow.Count; i++)
        {
            var key = CsvReader.NormalizeHeader(headerRow[i]);
            if (key.Length > 0 && !map.ContainsKey(key)) map[key] = i;
        }
        return map;
    }

    private static void RequireColumn(Dictionary<string, int> header, string label, string[] aliases)
    {
        if (AliasIndex(header, aliases) is null)
            throw new BusinessException($"Falta la columna '{label}' en el CSV");
    }

    private static int? AliasIndex(Dictionary<string, int> header, string[] aliases)
    {
        foreach (var a in aliases)
            if (header.TryGetValue(a, out var idx)) return idx;
        return null;
    }

    private static string? GetCell(List<string> row, Dictionary<string, int> header, string[] aliases)
    {
        var idx = AliasIndex(header, aliases);
        if (idx is null || idx.Value >= row.Count) return null;
        return row[idx.Value];
    }
}
