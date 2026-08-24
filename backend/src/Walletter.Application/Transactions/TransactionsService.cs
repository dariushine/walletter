using Walletter.Application.Categories;
using Walletter.Application.Common;
using Walletter.Domain;
using Walletter.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Walletter.Application.Transactions;

/// <summary>
/// Lógica de negocio de transacciones. Replica fielmente la lógica del rework
/// NestJS/Prisma: montos en centavos, instante UTC, fee inline atómico,
/// actualización de balance, soft-delete y proyección a zona horaria.
/// </summary>
public class TransactionsService
{
    private readonly IAppDbContext _db;
    private readonly CategoriesService _categories;

    public TransactionsService(IAppDbContext db, CategoriesService categories)
    {
        _db = db;
        _categories = categories;
    }

    private static string TimeZone() => TimeZoneHelper.DefaultTimeZone;

    public async Task<TransactionCreatedResult> Create(CreateTransactionCommand cmd, CancellationToken ct = default)
    {
        var tz = cmd.Tz ?? TimeZone();
        var amountInt = Money.ToInt(cmd.Amount);
        var commission = Money.ToInt(cmd.Fee);
        var datetimeUtc = TimeZoneHelper.ToUtcInstant(cmd.Date, cmd.Time, tz);

        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.Id == cmd.WalletId && w.IsActive, ct)
            ?? throw new BusinessException("Wallet no encontrada");

        var category = await _categories.GetOrCreateCategory(cmd.CategoryName, cmd.Type, ct);

        var total = amountInt + commission;
        if (cmd.Type == TransactionTypes.Expense && wallet.Balance < total)
        {
            throw new BusinessException(
                $"Fondos insuficientes. Balance actual: {Money.ToNum(wallet.Balance)} {wallet.Currency}, necesita {Money.ToNum(total)}");
        }

        var newBalance = cmd.Type == TransactionTypes.Expense
            ? wallet.Balance - total
            : wallet.Balance + amountInt - commission;

        var created = new Transaction
        {
            WalletId = wallet.Id,
            CategoryId = category.Id,
            Type = cmd.Type,
            Amount = amountInt,
            Description = cmd.Description ?? "",
            DatetimeUtc = datetimeUtc,
            Fee = 0,
            ParentId = null,
            Deleted = false,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Transactions.Add(created);
        await _db.SaveChangesAsync(ct);

        int? feeTransactionId = null;
        if (commission > 0)
        {
            var feeCategory = await _db.Categories.FirstOrDefaultAsync(
                c => c.Name == "fee" && c.Type == TransactionTypes.Expense && c.IsActive, ct);
            var fcId = feeCategory?.Id ?? category.Id;
            var side = cmd.CategoryName == "exchange_out" ? " débito"
                : cmd.CategoryName == "exchange_in" ? " crédito" : "";
            var feeTx = new Transaction
            {
                WalletId = wallet.Id,
                CategoryId = fcId,
                Type = TransactionTypes.Expense,
                Amount = commission,
                Description = $"Comisión{side}: {cmd.Description ?? category.Name}",
                DatetimeUtc = datetimeUtc,
                Fee = 0,
                ParentId = created.Id,
                Deleted = false,
                CreatedAt = DateTime.UtcNow,
            };
            _db.Transactions.Add(feeTx);
            await _db.SaveChangesAsync(ct);
            feeTransactionId = feeTx.Id;

            created.Fee = commission;
            await _db.SaveChangesAsync(ct);
        }

        wallet.Balance = newBalance;
        await _db.SaveChangesAsync(ct);

        return new TransactionCreatedResult(created.Id, feeTransactionId);
    }

    /// <summary>
    /// Crea una transacción y devuelve el response completo (para la API).
    /// </summary>
    public async Task<object> CreateFull(CreateTransactionCommand cmd, CancellationToken ct = default)
    {
        var tz = cmd.Tz ?? TimeZone();
        var amountInt = Money.ToInt(cmd.Amount);
        var commission = Money.ToInt(cmd.Fee);
        var datetimeUtc = TimeZoneHelper.ToUtcInstant(cmd.Date, cmd.Time, tz);

        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.Id == cmd.WalletId && w.IsActive, ct)
            ?? throw new BusinessException("Wallet no encontrada");
        var category = await _categories.GetOrCreateCategory(cmd.CategoryName, cmd.Type, ct);

        var total = amountInt + commission;
        var newBalance = cmd.Type == TransactionTypes.Expense
            ? wallet.Balance - total
            : wallet.Balance + amountInt - commission;

        var result = await Create(cmd, ct);

        var after = await _db.Wallets.AsNoTracking().FirstAsync(w => w.Id == cmd.WalletId, ct);

        return new
        {
            id = result.Id,
            feeTransactionId = result.FeeTransactionId,
            wallet = wallet.Name,
            currency = wallet.Currency,
            amount = Money.ToNum(amountInt),
            type = cmd.Type,
            newBalance = Money.ToNum(after.Balance),
            category = category.Name,
            fee = Money.ToNum(commission),
            datetime_utc = datetimeUtc,
        };
    }

    public async Task<object> List(ListTransactionsQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(query.Page ?? 1, 1);
        var limit = Math.Clamp(query.Limit ?? 20, 1, 100);
        var offset = (page - 1) * limit;
        var tz = TimeZone();

        var q = _db.Transactions.AsNoTracking().Where(t => !t.Deleted);
        if (query.WalletId is int wid)
            q = q.Where(t => t.WalletId == wid);
        if (!string.IsNullOrEmpty(query.From) || !string.IsNullOrEmpty(query.To))
        {
            if (!string.IsNullOrEmpty(query.From))
            {
                var from = TimeZoneHelper.ToUtcInstant(query.From, "00:00", tz);
                q = q.Where(t => t.DatetimeUtc >= from);
            }
            if (!string.IsNullOrEmpty(query.To))
            {
                var to = TimeZoneHelper.ToUtcInstant(query.To, "00:00", tz);
                q = q.Where(t => t.DatetimeUtc < to);
            }
        }

        var total = await q.CountAsync(ct);
        var rows = await q
            .Include(t => t.Wallet)
            .Include(t => t.Category)
            .OrderByDescending(t => t.DatetimeUtc)
            .ThenByDescending(t => t.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);

        return new
        {
            data = rows.Select(r => Projected(r, tz)).ToList(),
            total,
            page,
            limit,
            tz,
        };
    }

    private static object Projected(Transaction r, string tz)
    {
        var (date, time) = TimeZoneHelper.UtcToWallClock(r.DatetimeUtc, tz);
        return new
        {
            id = r.Id,
            walletId = r.WalletId,
            walletName = r.Wallet?.Name,
            walletCurrency = r.Wallet?.Currency,
            category = r.Category?.Name,
            type = r.Type,
            amount = Money.ToNum(r.Amount),
            description = r.Description,
            datetimeUtc = r.DatetimeUtc,
            fee = Money.ToNum(r.Fee),
            parentTransactionId = r.ParentId,
            date,
            time,
        };
    }

    public async Task<object> Detail(int id, CancellationToken ct = default)
    {
        var t = await _db.Transactions
            .Include(x => x.Wallet)
            .Include(x => x.Category)
            .Include(x => x.Parent).ThenInclude(p => p!.Category)
            .Include(x => x.Children).ThenInclude(c => c.Wallet)
            .Include(x => x.Children).ThenInclude(c => c.Category)
            .FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct)
            ?? throw new NotFoundException("Transacción no encontrada");

        var tz = TimeZone();
        var (date, time) = TimeZoneHelper.UtcToWallClock(t.DatetimeUtc, tz);
        var associated = t.Children
            .Where(c => !c.Deleted)
            .OrderByDescending(c => c.DatetimeUtc)
            .ThenByDescending(c => c.Id)
            .Select(c => Projected(c, tz))
            .ToList();

        return new
        {
            id = t.Id,
            walletId = t.WalletId,
            walletName = t.Wallet?.Name,
            walletCurrency = t.Wallet?.Currency,
            category = t.Category?.Name,
            type = t.Type,
            amount = Money.ToNum(t.Amount),
            description = t.Description,
            datetimeUtc = t.DatetimeUtc,
            fee = Money.ToNum(t.Fee),
            parentTransactionId = t.ParentId,
            date,
            time,
            // Balance en vivo de la billetera = saldo resultante tras esta transacción.
            resultingBalance = Money.ToNum(t.Wallet?.Balance ?? 0),
            // true si es débito/crédito de exchange, o una comisión cuyo padre lo es.
            isExchange = IsExchangeTransaction(t),
            // id del exchange al que pertenece (si es transacción de exchange). Null si no.
            exchangeId = await ResolveExchangeId(t, ct),
            associated,
        };
    }

    /// <summary>
    /// Resuelve el id del exchange al que pertenece una transacción:
    /// débito/crédito (DebitTransactionId/CreditTransactionId) o, si es una
    /// comisión (fee), el del exchange de su padre. Devuelve null si no es de
    /// exchange.
    /// </summary>
    private async Task<int?> ResolveExchangeId(Transaction t, CancellationToken ct)
    {
        var cat = t.Category?.Name?.ToLowerInvariant();
        if (cat is "exchange_out" or "exchange_in")
        {
            var ex = await _db.Exchanges.AsNoTracking()
                .FirstOrDefaultAsync(x => !x.Deleted && (x.DebitTransactionId == t.Id || x.CreditTransactionId == t.Id), ct);
            return ex?.Id;
        }
        if (cat == "fee" && t.ParentId != null)
        {
            var parent = await _db.Transactions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == t.ParentId, ct);
            if (parent != null)
            {
                var ex = await _db.Exchanges.AsNoTracking()
                    .FirstOrDefaultAsync(x => !x.Deleted && (x.DebitTransactionId == parent.Id || x.CreditTransactionId == parent.Id), ct);
                return ex?.Id;
            }
        }
        return null;
    }

    /// <summary>
    /// Devuelve true si la transacción pertenece a un exchange: débito/crédito
    /// (categorías exchange_out/exchange_in) o una comisión cuyo padre lo es.
    /// </summary>
    private static bool IsExchangeTransaction(Transaction t)
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

    public async Task<object> Update(int id, UpdateTransactionCommand cmd, CancellationToken ct = default)
    {
        var t = await _db.Transactions
            .Include(x => x.Wallet)
            .FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct)
            ?? throw new NotFoundException("Transacción no encontrada");

        var tz = cmd.Tz ?? TimeZone();
        var oldAmount = t.Amount;
        var oldFee = t.Fee;
        if (cmd.Description != null) t.Description = cmd.Description;
        if (cmd.Amount is decimal amount) t.Amount = Money.ToInt(amount);
        if (!string.IsNullOrEmpty(cmd.CategoryName))
        {
            var cat = await _categories.GetOrCreateCategory(cmd.CategoryName, t.Type, ct);
            t.CategoryId = cat.Id;
        }
        if (!string.IsNullOrEmpty(cmd.Date) && !string.IsNullOrEmpty(cmd.Time))
        {
            t.DatetimeUtc = TimeZoneHelper.ToUtcInstant(cmd.Date, cmd.Time, tz);
        }

        // Ajustar el balance por el cambio de monto (el fee denormalizado no cambia
        // en este update). Se aplica la fórmula del Create con el efecto nuevo vs. el original.
        if (t.Wallet != null && t.Amount != oldAmount)
        {
            var oldEffect = t.Type == TransactionTypes.Income ? oldAmount - oldFee : -(oldAmount + oldFee);
            var newEffect = t.Type == TransactionTypes.Income ? t.Amount - oldFee : -(t.Amount + oldFee);
            var newBalance = t.Wallet.Balance + (newEffect - oldEffect);
            if (newBalance < 0)
                throw new BusinessException("Fondos insuficientes en la billetera tras el cambio");
            t.Wallet.Balance = newBalance;
        }

        await _db.SaveChangesAsync(ct);
        return new { success = true, id = t.Id };
    }

    public async Task<object> Remove(int id, CancellationToken ct = default)
    {
        var t = await _db.Transactions
            .Include(x => x.Wallet)
            .Include(x => x.Children).ThenInclude(c => c.Category)
            .FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct)
            ?? throw new NotFoundException("Transacción no encontrada");

        if (t.Wallet != null)
        {
            // Revertir el efecto del padre sobre el balance (fórmula del Create:
            // income = +monto - fee, expense = -(monto + fee); t.Fee denormalizado
            // ya incluye los fees hijos).
            var parentEffect = t.Type == TransactionTypes.Income
                ? t.Amount - t.Fee
                : -(t.Amount + t.Fee);
            t.Wallet.Balance -= parentEffect;

            // Revertir y eliminar hijos NO-fee (asociadas) con su propio efecto.
            // Los hijos de categoría fee ya están reflejados en t.Fee del padre.
            foreach (var child in t.Children.Where(c => !c.Deleted))
            {
                if (!string.Equals(child.Category?.Name, "fee", StringComparison.OrdinalIgnoreCase))
                {
                    var childEffect = child.Type == TransactionTypes.Income
                        ? child.Amount - child.Fee
                        : -(child.Amount + child.Fee);
                    t.Wallet.Balance -= childEffect;
                }
                child.Deleted = true;
            }
        }

        t.Deleted = true;
        await _db.SaveChangesAsync(ct);
        return new { success = true };
    }

    public async Task<object> AddFee(int id, AddFeeCommand cmd, CancellationToken ct = default)
    {
        var t = await _db.Transactions
            .Include(x => x.Wallet)
            .Include(x => x.Category)
            .FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct)
            ?? throw new NotFoundException("Transacción no encontrada");

        if (t.Category?.Name == "fee")
            throw new BusinessException("No puedes agregar comisión a una comisión (fee).");

        var tz = cmd.Tz ?? TimeZone();
        var feeAmount = Money.ToInt(cmd.Amount);
        var datetimeUtc = TimeZoneHelper.ToUtcInstant(cmd.Date, cmd.Time, tz);

        var feeCategory = await _db.Categories.FirstOrDefaultAsync(
            c => c.Name == "fee" && c.Type == TransactionTypes.Expense && c.IsActive, ct)
            ?? throw new BusinessException("Categoría fee no disponible");

        if (t.Wallet!.Balance < feeAmount)
        {
            throw new BusinessException(
                $"Fondos insuficientes. Balance actual: {Money.ToNum(t.Wallet.Balance)} {t.Wallet.Currency}, necesita {Money.ToNum(feeAmount)}");
        }

        var feeTx = new Transaction
        {
            WalletId = t.WalletId,
            CategoryId = feeCategory.Id,
            Type = TransactionTypes.Expense,
            Amount = feeAmount,
            Description = $"Comisión: {t.Description ?? ""}".Trim(),
            DatetimeUtc = datetimeUtc,
            Fee = 0,
            ParentId = id,
            Deleted = false,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Transactions.Add(feeTx);
        t.Wallet.Balance -= feeAmount;
        await _db.SaveChangesAsync(ct);

        // Sincroniza el fee denormalizado del padre.
        t.Fee = await SumChildFees(id, ct);
        await _db.SaveChangesAsync(ct);

        return new { success = true, feeId = feeTx.Id };
    }

    public async Task<object> Associate(int id, AssociateTransactionCommand cmd, CancellationToken ct = default)
    {
        var t = await _db.Transactions
            .Include(x => x.Wallet)
            .FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct)
            ?? throw new NotFoundException("Transacción no encontrada");

        var tz = cmd.Tz ?? TimeZone();
        var amountInt = Money.ToInt(cmd.Amount);
        var commission = Money.ToInt(cmd.Fee);
        var datetimeUtc = TimeZoneHelper.ToUtcInstant(cmd.Date, cmd.Time, tz);
        var category = await _categories.GetOrCreateCategory(cmd.CategoryName, cmd.Type, ct);

        // Replica la lógica de comisión del Create: en gasto el total es monto + fee;
        // en ingreso el fee se resta del monto recibido.
        var effect = cmd.Type == TransactionTypes.Income
            ? amountInt - commission
            : -(amountInt + commission);
        var required = cmd.Type == TransactionTypes.Income ? Math.Max(commission, 0) : amountInt + commission;
        if (t.Wallet!.Balance < required)
        {
            throw new BusinessException(
                $"Fondos insuficientes. Balance actual: {Money.ToNum(t.Wallet!.Balance)}");
        }

        var child = new Transaction
        {
            WalletId = t.WalletId,
            CategoryId = category.Id,
            Type = cmd.Type,
            Amount = amountInt,
            Description = cmd.Description ?? "",
            DatetimeUtc = datetimeUtc,
            Fee = commission,
            ParentId = id,
            Deleted = false,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Transactions.Add(child);
        t.Wallet!.Balance += effect;
        await _db.SaveChangesAsync(ct);

        return new { success = true, associateId = child.Id };
    }

    private async Task<int> SumChildFees(int parentId, CancellationToken ct)
    {
        return await _db.Transactions
            .Where(x => x.ParentId == parentId && !x.Deleted && x.Category!.Name == "fee")
            .SumAsync(x => (int?)x.Amount, ct) ?? 0;
    }
}
