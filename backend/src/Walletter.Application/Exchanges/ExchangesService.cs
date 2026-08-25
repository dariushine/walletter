using Walletter.Application.Common;
using Walletter.Domain;
using Walletter.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Walletter.Application.Exchanges;

/// <summary>
/// Lógica de negocio de exchanges (cambio de divisa). Crea atómicamente las
/// transacciones débito/crédito (+ comisiones) y el registro del exchange, y
/// ajusta los balances. El update recalcula deltas de balance.
/// </summary>
public class ExchangesService
{
    private readonly IAppDbContext _db;

    public ExchangesService(IAppDbContext db)
    {
        _db = db;
    }

    private static string TimeZone() => TimeZoneHelper.DefaultTimeZone;

    public async Task<object> Create(CreateExchangeCommand cmd, CancellationToken ct = default)
    {
        if (cmd.FromWalletId == 0 || cmd.ToWalletId == 0 || cmd.FromAmount == 0 || cmd.ToAmount == 0)
            throw new BusinessException("Faltan campos requeridos: fromWalletId, toWalletId, fromAmount, toAmount");
        if (cmd.FromWalletId == cmd.ToWalletId)
            throw new BusinessException("Las billeteras origen y destino deben ser diferentes");
        if (cmd.FromAmount <= 0 || cmd.ToAmount <= 0)
            throw new BusinessException("Los montos deben ser mayores a 0");
        if (string.IsNullOrEmpty(cmd.Date) || string.IsNullOrEmpty(cmd.Time))
            throw new BusinessException("La fecha (YYYY-MM-DD) y hora (HH:MM) son obligatorias");

        var tz = cmd.Tz ?? TimeZone();
        var datetimeUtc = TimeZoneHelper.ToUtcInstant(cmd.Date, cmd.Time, tz);
        var fromAmountInt = Money.ToInt(cmd.FromAmount);
        var toAmountInt = Money.ToInt(cmd.ToAmount);
        var commission = Money.ToInt(cmd.Fee);
        var creditCommission = Money.ToInt(cmd.CreditFee);
        var rate = Money.ToRateInt(cmd.ToAmount / cmd.FromAmount);

        var fromWallet = await _db.Wallets.FirstOrDefaultAsync(w => w.Id == cmd.FromWalletId && w.IsActive, ct)
            ?? throw new BusinessException("Billetera origen no encontrada");
        var toWallet = await _db.Wallets.FirstOrDefaultAsync(w => w.Id == cmd.ToWalletId && w.IsActive, ct)
            ?? throw new BusinessException("Billetera destino no encontrada");

        var fromTotal = fromAmountInt + commission;
        if (fromWallet.Balance < fromTotal)
        {
            throw new BusinessException(
                $"Fondos insuficientes en {fromWallet.Name}. Balance actual: {Money.ToNum(fromWallet.Balance)} {fromWallet.Currency}, necesita {Money.ToNum(fromTotal)}");
        }
        if (toWallet.Balance + toAmountInt < creditCommission)
        {
            throw new BusinessException(
                $"Fondos insuficientes en {toWallet.Name}. Balance actual: {Money.ToNum(toWallet.Balance)} {toWallet.Currency}, necesita {Money.ToNum(creditCommission)}");
        }

        var exchangeOut = await _db.Categories.FirstOrDefaultAsync(
            c => c.Name == "exchange_out" && c.Type == TransactionTypes.Expense && c.IsActive, ct);
        var exchangeIn = await _db.Categories.FirstOrDefaultAsync(
            c => c.Name == "exchange_in" && c.Type == TransactionTypes.Income && c.IsActive, ct);
        if (exchangeOut == null || exchangeIn == null)
            throw new BusinessException("Faltan categorías de sistema (exchange_out/exchange_in)");
        var feeCategory = await _db.Categories.FirstOrDefaultAsync(
            c => c.Name == "fee" && c.Type == TransactionTypes.Expense && c.IsActive, ct);

        // Débito
        var debit = new Transaction
        {
            WalletId = fromWallet.Id,
            CategoryId = exchangeOut.Id,
            Type = TransactionTypes.Expense,
            Amount = fromAmountInt,
            Description = $"{cmd.Description ?? "Exchange"} → {toWallet.Name}",
            DatetimeUtc = datetimeUtc,
            Fee = 0,
            ParentId = null,
            Deleted = false,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Transactions.Add(debit);

        int? debitFeeTx = null;
        if (commission > 0 && feeCategory != null)
        {
            var feeTx = new Transaction
            {
                WalletId = fromWallet.Id,
                CategoryId = feeCategory.Id,
                Type = TransactionTypes.Expense,
                Amount = commission,
                Description = $"Comisión débito: {cmd.Description ?? "Exchange"} → {toWallet.Name}",
                DatetimeUtc = datetimeUtc,
                Fee = 0,
                Parent = debit,
                Deleted = false,
                CreatedAt = DateTime.UtcNow,
            };
            _db.Transactions.Add(feeTx);
            debitFeeTx = feeTx.Id;
            debit.Fee = commission;
        }

        // Crédito
        var credit = new Transaction
        {
            WalletId = toWallet.Id,
            CategoryId = exchangeIn.Id,
            Type = TransactionTypes.Income,
            Amount = toAmountInt,
            Description = $"{cmd.Description ?? "Exchange"} ← {fromWallet.Name}",
            DatetimeUtc = datetimeUtc,
            Fee = 0,
            ParentId = null,
            Deleted = false,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Transactions.Add(credit);

        int? creditFeeTx = null;
        if (creditCommission > 0 && feeCategory != null)
        {
            var feeTx = new Transaction
            {
                WalletId = toWallet.Id,
                CategoryId = feeCategory.Id,
                Type = TransactionTypes.Expense,
                Amount = creditCommission,
                Description = $"Comisión crédito: {cmd.Description ?? "Exchange"} ← {fromWallet.Name}",
                DatetimeUtc = datetimeUtc,
                Fee = 0,
                Parent = credit,
                Deleted = false,
                CreatedAt = DateTime.UtcNow,
            };
            _db.Transactions.Add(feeTx);
            creditFeeTx = feeTx.Id;
            credit.Fee = creditCommission;
        }

        var ex = new Exchange
        {
            Debit = debit,
            Credit = credit,
            FromWalletId = fromWallet.Id,
            ToWalletId = toWallet.Id,
            FromAmount = fromAmountInt,
            ToAmount = toAmountInt,
            Rate = rate,
            Fee = commission,
            CreditFee = creditCommission,
            Description = cmd.Description ?? "",
            Deleted = false,
            DatetimeUtc = datetimeUtc,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Exchanges.Add(ex);

        fromWallet.Balance = fromWallet.Balance - fromAmountInt - commission;
        toWallet.Balance = toWallet.Balance + toAmountInt - creditCommission;

        await _db.SaveChangesAsync(ct);

        return new
        {
            success = true,
            message = "Exchange registrado exitosamente",
            exchange = new
            {
                id = ex.Id,
                rate = Money.ToRateNum(rate),
                fromWallet = fromWallet.Name,
                toWallet = toWallet.Name,
                fromAmount = cmd.FromAmount,
                toAmount = cmd.ToAmount,
                fromCurrency = fromWallet.Currency,
                toCurrency = toWallet.Currency,
                description = cmd.Description ?? "",
            },
            transactions = new
            {
                debit = new { id = debit.Id, feeTransactionId = debitFeeTx },
                credit = new { id = credit.Id, feeTransactionId = creditFeeTx },
            },
        };
    }

    public async Task<object> List(ListExchangesQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(query.Page ?? 1, 1);
        var limit = Math.Clamp(query.Limit ?? 20, 1, 100);
        var offset = (page - 1) * limit;

        var q = _db.Exchanges.AsNoTracking().Where(e => !e.Deleted);
        var total = await q.CountAsync(ct);
        var rows = await q
            .Include(e => e.From)
            .Include(e => e.To)
            .OrderByDescending(e => e.DatetimeUtc)
            .ThenByDescending(e => e.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);

        return new
        {
            data = rows.Select(e => (object)new
            {
                id = e.Id,
                fromWalletId = e.FromWalletId,
                toWalletId = e.ToWalletId,
                fromAmount = Money.ToNum(e.FromAmount),
                toAmount = Money.ToNum(e.ToAmount),
                rate = Money.ToRateNum(e.Rate),
                fee = Money.ToNum(e.Fee),
                creditFee = Money.ToNum(e.CreditFee),
                description = e.Description,
                datetimeUtc = e.DatetimeUtc,
                createdAt = e.CreatedAt,
                debitTransactionId = e.DebitTransactionId,
                creditTransactionId = e.CreditTransactionId,
                fromWalletName = e.From.Name,
                toWalletName = e.To.Name,
                fromCurrency = e.From.Currency,
                toCurrency = e.To.Currency,
            }).ToList(),
            total,
            page,
            limit,
        };
    }

    public async Task<object> Detail(int id, CancellationToken ct = default)
    {
        var ex = await _db.Exchanges
            .Include(e => e.From)
            .Include(e => e.To)
            .Include(e => e.Debit)
            .Include(e => e.Credit)
            .FirstOrDefaultAsync(e => e.Id == id && !e.Deleted, ct)
            ?? throw new NotFoundException("Exchange no encontrado");

        // Transacciones del exchange: el débito, el crédito y sus comisiones (hijas).
        var ids = new[] { ex.DebitTransactionId, ex.CreditTransactionId };
        var txns = await _db.Transactions
            .AsNoTracking()
            .Include(t => t.Wallet)
            .Include(t => t.Category)
            .Where(t => !t.Deleted && (ids.Contains(t.Id) || (t.ParentId != null && ids.Contains(t.ParentId.Value))))
            .OrderBy(t => t.DatetimeUtc)
            .ThenBy(t => t.Id)
            .ToListAsync(ct);

        var tz = TimeZone();
        var transactions = txns.Select(t => Project(t, tz)).ToList();

        return new
        {
            id = ex.Id,
            fromWalletId = ex.FromWalletId,
            toWalletId = ex.ToWalletId,
            fromAmount = Money.ToNum(ex.FromAmount),
            toAmount = Money.ToNum(ex.ToAmount),
            rate = Money.ToRateNum(ex.Rate),
            fee = Money.ToNum(ex.Fee),
            creditFee = Money.ToNum(ex.CreditFee),
            description = ex.Description,
            datetimeUtc = ex.DatetimeUtc,
            createdAt = ex.CreatedAt,
            fromWalletName = ex.From.Name,
            toWalletName = ex.To.Name,
            fromCurrency = ex.From.Currency,
            toCurrency = ex.To.Currency,
            debitTransactionId = ex.DebitTransactionId,
            creditTransactionId = ex.CreditTransactionId,
            transactions,
        };
    }

    private static object Project(Transaction t, string tz)
    {
        var (date, time) = TimeZoneHelper.UtcToWallClock(t.DatetimeUtc, tz);
        return new
        {
            id = t.Id,
            category = t.Category?.Name,
            type = t.Type,
            amount = Money.ToNum(t.Amount),
            description = t.Description,
            walletCurrency = t.Wallet?.Currency,
            date,
            time,
        };
    }

    public async Task<object> Remove(int id, CancellationToken ct = default)
    {
        var ex = await _db.Exchanges
            .Include(e => e.From)
            .Include(e => e.To)
            .FirstOrDefaultAsync(e => e.Id == id && !e.Deleted, ct)
            ?? throw new NotFoundException("Exchange no encontrado");

        // Revertir los balances del origen y destino (inverso del Create).
        // Origen: se había restado (fromAmount + fee). Destino: se había sumado (toAmount - creditFee).
        ex.From.Balance += ex.FromAmount + ex.Fee;
        ex.To.Balance -= ex.ToAmount - ex.CreditFee;

        // Eliminar lógicamente las transacciones del exchange (débito, crédito y comisiones).
        var ids = new[] { ex.DebitTransactionId, ex.CreditTransactionId };
        var txns = await _db.Transactions
            .Where(t => ids.Contains(t.Id) || (t.ParentId != null && ids.Contains(t.ParentId.Value)))
            .ToListAsync(ct);
        foreach (var tx in txns) tx.Deleted = true;

        ex.Deleted = true;
        await _db.SaveChangesAsync(ct);
        return new { success = true };
    }

    public async Task<object> Update(int id, UpdateExchangeCommand cmd, CancellationToken ct = default)
    {
        var ex = await _db.Exchanges
            .Include(e => e.Debit)
            .Include(e => e.Credit)
            .Include(e => e.From)
            .Include(e => e.To)
            .FirstOrDefaultAsync(e => e.Id == id && !e.Deleted, ct)
            ?? throw new NotFoundException("Exchange no encontrado");

        var newFrom = Money.ToInt(cmd.FromAmount ?? 0);
        if (newFrom == 0) newFrom = ex.FromAmount;
        var newTo = Money.ToInt(cmd.ToAmount ?? 0);
        if (newTo == 0) newTo = ex.ToAmount;
        var newFee = cmd.Fee is decimal f ? Money.ToInt(f) : ex.Fee;
        var newCreditFee = cmd.CreditFee is decimal cf ? Money.ToInt(cf) : ex.CreditFee;
        var newDesc = cmd.Description ?? ex.Description;
        var newRate = Money.ToRateInt((decimal)newTo / newFrom);

        // Fecha efectiva: si viene date/time en el update, recalcula y actualiza
        // el exchange y sus transacciones débito/crédito (+ comisiones).
        var newDatetimeUtc = ex.DatetimeUtc;
        if (!string.IsNullOrEmpty(cmd.Date) && !string.IsNullOrEmpty(cmd.Time))
        {
            newDatetimeUtc = TimeZoneHelper.ToUtcInstant(cmd.Date, cmd.Time, cmd.Tz ?? TimeZone());
        }

        var fromDelta = -(newFrom + newFee) - -(ex.FromAmount + ex.Fee);
        var toDelta = (newTo - newCreditFee) - (ex.ToAmount - ex.CreditFee);

        var newFromBalance = ex.From.Balance + fromDelta;
        if (newFromBalance < 0)
            throw new BusinessException("Fondos insuficientes en la billetera origen tras el cambio");
        var newToBalance = ex.To.Balance + toDelta;
        if (newToBalance < 0)
            throw new BusinessException("Fondos insuficientes en la billetera destino tras el cambio");

        ex.Debit.Amount = newFrom;
        ex.Credit.Amount = newTo;

        if (cmd.Fee is decimal)
        {
            var feeTx = await _db.Transactions.FirstOrDefaultAsync(
                x => x.ParentId == ex.DebitTransactionId && x.Category!.Name == "fee", ct);
            if (feeTx != null) feeTx.Amount = newFee;
        }
        if (cmd.CreditFee is decimal)
        {
            var cfTx = await _db.Transactions.FirstOrDefaultAsync(
                x => x.ParentId == ex.CreditTransactionId && x.Category!.Name == "fee", ct);
            if (cfTx != null) cfTx.Amount = newCreditFee;
        }

        ex.FromAmount = newFrom;
        ex.ToAmount = newTo;
        ex.Rate = newRate;
        ex.Fee = newFee;
        ex.CreditFee = newCreditFee;
        ex.Description = newDesc ?? "";

        if (newDatetimeUtc != ex.DatetimeUtc)
        {
            ex.DatetimeUtc = newDatetimeUtc;
            if (ex.Debit != null) ex.Debit.DatetimeUtc = newDatetimeUtc;
            if (ex.Credit != null) ex.Credit.DatetimeUtc = newDatetimeUtc;
            // Comisiones hijas
            var parentIds = new[] { ex.DebitTransactionId, ex.CreditTransactionId };
            var feeTxns = await _db.Transactions
                .Where(x => x.ParentId != null && parentIds.Contains(x.ParentId.Value))
                .ToListAsync(ct);
            foreach (var ft in feeTxns) ft.DatetimeUtc = newDatetimeUtc;
        }

        ex.From.Balance = newFromBalance;
        ex.To.Balance = newToBalance;

        await _db.SaveChangesAsync(ct);
        return new { success = true, message = "Exchange actualizado" };
    }
}
