using Walletter.Application.Categories;
using Walletter.Application.Common;
using Walletter.Application.Transactions;
using Walletter.Domain;
using Walletter.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Walletter.Application.Pending;

/// <summary>DTO de pago pendiente que devuelve la API.</summary>
public record PendingPaymentDto(
    int Id,
    string Name,
    string? Description,
    decimal Amount,
    decimal Fee,
    string Currency,
    string Type,
    string? Category,
    int CategoryId,
    int? WalletId,
    string? WalletName,
    string? WalletCurrency,
    string? DueDate,
    bool IsPaid,
    DateTime? PaidAt,
    int? TransactionId,
    bool IsCancelled);

public class PendingPaymentsService
{
    private readonly IAppDbContext _db;
    private readonly CategoriesService _categories;
    private readonly TransactionsService _transactions;

    public PendingPaymentsService(IAppDbContext db, CategoriesService categories, TransactionsService transactions)
    {
        _db = db;
        _categories = categories;
        _transactions = transactions;
    }

    public async Task<List<PendingPaymentDto>> List(bool includePaid, CancellationToken ct = default)
    {
        var query = _db.PendingPayments
            .Include(p => p.Category)
            .Where(p => !p.IsCancelled && !p.IsPaid);

        // Historial: incluye también los ya pagados (para ver qué se saldó).
        if (includePaid)
            query = _db.PendingPayments.Include(p => p.Category).Where(p => !p.IsCancelled);

        var rows = await query
            .OrderBy(p => p.DueDate == null)
            .ThenBy(p => p.DueDate)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);

        // La entidad PendingPayment no tiene navegación a Wallet (se decidió no
        // tocar el schema ni el DbContext), así que resolvemos nombre/moneda de
        // la billetera con un lookup batcheado por WalletId.
        var wallets = await LoadWalletsAsync(rows.Select(p => p.WalletId), ct);
        return rows.Select(p => Map(p, WalletFor(wallets, p.WalletId))).ToList();
    }

    public async Task<PendingPaymentDto> Create(CreatePendingPaymentCommand cmd, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(cmd.Name) || cmd.Amount == 0)
            throw new BusinessException("name y amount son requeridos");

        int? categoryId = cmd.CategoryId;
        if (categoryId == null && !string.IsNullOrEmpty(cmd.CategoryName))
        {
            var cat = await _categories.GetOrCreateCategory(cmd.CategoryName, cmd.Type, ct);
            categoryId = cat.Id;
        }
        if (categoryId == null)
            throw new BusinessException("Categoría requerida");

        ValidateDueDate(cmd.DueDate);

        // Con billetera, la moneda efectiva se deriva de ella (y si el usuario
        // pidió otra, es un error). Sin billetera, se respeta la elegida.
        var currency = await ResolveCurrencyAsync(cmd.Currency, cmd.WalletId, ct);

        var row = new PendingPayment
        {
            Name = cmd.Name,
            Description = cmd.Description,
            Amount = Money.ToInt(cmd.Amount),
            Fee = Money.ToInt(cmd.Fee),
            Currency = currency,
            Type = cmd.Type,
            CategoryId = categoryId.Value,
            WalletId = cmd.WalletId,
            DueDate = string.IsNullOrWhiteSpace(cmd.DueDate) ? null : cmd.DueDate.Trim(),
            IsPaid = false,
            IsCancelled = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.PendingPayments.Add(row);
        await _db.SaveChangesAsync(ct);
        var loadedCategory = await _db.Categories.FindAsync(new object?[] { row.CategoryId }, ct);
        row.Category = loadedCategory!;
        var wallet = await LookupWalletAsync(row.WalletId, ct);
        return Map(row, wallet);
    }

    public async Task<PendingPaymentDto> Remove(int id, CancellationToken ct = default)
    {
        var row = await GetActiveAsync(id, ct);
        if (row.IsPaid)
            throw new BusinessException("No se puede eliminar un pago pendiente ya pagado");
        row.IsCancelled = true;
        await _db.SaveChangesAsync(ct);
        var wallet = await LookupWalletAsync(row.WalletId, ct);
        return Map(row, wallet);
    }

    public async Task<PendingPaymentDto> Detail(int id, CancellationToken ct = default)
    {
        var row = await _db.PendingPayments
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Pago pendiente no encontrado");
        var wallet = await LookupWalletAsync(row.WalletId, ct);
        return Map(row, wallet);
    }

    public async Task<PendingPaymentDto> Update(int id, UpdatePendingPaymentCommand cmd, CancellationToken ct = default)
    {
        var existing = await GetActiveAsync(id, ct);
        if (existing.IsPaid)
            throw new BusinessException("No se puede editar un pago pendiente ya pagado");

        // Validar el estado EFECTIVO antes de mutar (WalletId/Currency pueden
        // venir nulos = "no cambiar"). Si hay billetera, su moneda manda.
        var effectiveWalletId = cmd.WalletId ?? existing.WalletId;
        var effectiveCurrency = cmd.Currency ?? existing.Currency;
        var resolvedCurrency = await ResolveCurrencyAsync(effectiveCurrency, effectiveWalletId, ct);

        if (cmd.Name != null) existing.Name = cmd.Name;
        if (cmd.Description != null) existing.Description = cmd.Description;
        if (cmd.Amount is decimal a) existing.Amount = Money.ToInt(a);
        if (cmd.Fee is decimal f) existing.Fee = Money.ToInt(f);
        if (cmd.Currency != null || cmd.WalletId != null) existing.Currency = resolvedCurrency;
        if (cmd.Type != null) existing.Type = cmd.Type;
        if (cmd.WalletId is int w) existing.WalletId = w;
        if (cmd.DueDate != null)
        {
            ValidateDueDate(cmd.DueDate);
            existing.DueDate = string.IsNullOrWhiteSpace(cmd.DueDate) ? null : cmd.DueDate.Trim();
        }
        if (!string.IsNullOrEmpty(cmd.CategoryName))
        {
            var cat = await _categories.GetOrCreateCategory(cmd.CategoryName, existing.Type, ct);
            existing.CategoryId = cat.Id;
        }
        existing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        var loadedCategory = await _db.Categories.FindAsync(new object?[] { existing.CategoryId }, ct);
        existing.Category = loadedCategory!;
        var wallet = await LookupWalletAsync(existing.WalletId, ct);
        return Map(existing, wallet);
    }

    /// <summary>
    /// Paga el pendiente: delega en TransactionsService.Create (reusa la
    /// lógica atómica de fee + balance) y marca el pendiente como pagado.
    /// </summary>
    public async Task<object> Pay(int id, PayPendingPaymentCommand cmd, CancellationToken ct = default)
    {
        var row = await GetActiveAsync(id, ct);

        var walletId = cmd.OverrideWalletId ?? cmd.WalletId ?? row.WalletId;
        if (walletId == null)
            throw new BusinessException("El pago pendiente no tiene billetera asignada");

        var category = await _db.Categories.FindAsync(new object?[] { row.CategoryId }, ct);
        if (!string.IsNullOrEmpty(cmd.OverrideCategoryName))
        {
            var cat = await _categories.GetOrCreateCategory(cmd.OverrideCategoryName, row.Type, ct);
            category = cat;
        }
        if (category == null) throw new NotFoundException("Categoría no encontrada");

        var amount = cmd.OverrideAmount is decimal oa ? Money.ToNum(Money.ToInt(oa)) : Money.ToNum(row.Amount);
        var fee = cmd.OverrideFee is decimal of ? Money.ToNum(Money.ToInt(of)) : Money.ToNum(row.Fee);
        var description = string.IsNullOrWhiteSpace(cmd.Description) ? row.Description ?? row.Name : cmd.Description.Trim();

        var created = await _transactions.Create(new CreateTransactionCommand
        {
            WalletId = walletId.Value,
            CategoryName = category.Name,
            Type = row.Type,
            Amount = amount,
            Description = description,
            Fee = fee,
            Date = cmd.Date,
            Time = cmd.Time,
            Tz = cmd.Tz ?? TimeZoneHelper.DefaultTimeZone,
        }, ct);

        // La transacción ya se creó de verdad. Ahora marcamos el pendiente como
        // pagado. Si el Create falla (fondos insuficientes u otro error), la
        // excepción impide llegar aquí y el pendiente queda intacto.
        row.IsPaid = true;
        row.PaidAt = DateTime.UtcNow;
        row.TransactionId = created.Id;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new
        {
            success = true,
            transactionId = created.Id,
            feeTransactionId = created.FeeTransactionId,
        };
    }

    /// <summary>
    /// Marca un pendiente como pagado SIN crear transacción ni tocar saldos.
    /// Para cuando la transacción real ya se creó por otra vía (p. ej. el
    /// diálogo del front): aquí solo se actualiza el estado del pendiente.
    /// </summary>
    public async Task<PendingPaymentDto> MarkPaid(int id, MarkPendingPaidCommand cmd, CancellationToken ct = default)
    {
        var row = await GetActiveAsync(id, ct);
        if (row.IsPaid)
            throw new BusinessException("El pago pendiente ya está marcado como pagado");

        row.IsPaid = true;
        row.PaidAt = DateTime.UtcNow;
        row.TransactionId = cmd.TransactionId;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        var loadedCategory = await _db.Categories.FindAsync(new object?[] { row.CategoryId }, ct);
        row.Category = loadedCategory!;
        var wallet = await LookupWalletAsync(row.WalletId, ct);
        return Map(row, wallet);
    }

    private async Task<PendingPayment> GetActiveAsync(int id, CancellationToken ct)
    {
        var row = await _db.PendingPayments.FindAsync(new object?[] { id }, ct)
            ?? throw new NotFoundException("Pago pendiente no encontrado");
        if (row.IsCancelled)
            throw new NotFoundException("Pago pendiente no encontrado");
        return row;
    }

    private static void ValidateDueDate(string? dueDate)
    {
        if (dueDate == null) return;
        if (!DateTime.TryParseExact(dueDate, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out _))
            throw new BusinessException("Fecha de vencimiento inválida (use yyyy-MM-dd)");
    }

    /// <summary>
    /// Resuelve la moneda efectiva de un pendiente sin navegación a Wallet.
    /// Si hay billetera, la moneda debe coincidir con la de ella (y se
    /// normaliza a la de la billetera); si no, se usa la elegida (default USD).
    /// </summary>
    private async Task<string> ResolveCurrencyAsync(string? requested, int? walletId, CancellationToken ct)
    {
        if (walletId == null)
            return string.IsNullOrWhiteSpace(requested) ? "USD" : requested!;

        var wallet = await _db.Wallets.FindAsync(new object?[] { walletId.Value }, ct)
            ?? throw new BusinessException("Billetera no encontrada");

        if (!string.IsNullOrWhiteSpace(requested) &&
            !string.Equals(requested, wallet.Currency, StringComparison.OrdinalIgnoreCase))
            throw new BusinessException(
                $"La moneda del pendiente ({requested}) no coincide con la de la billetera {wallet.Name} ({wallet.Currency})");

        return wallet.Currency;
    }

    private async Task<Wallet?> LookupWalletAsync(int? walletId, CancellationToken ct)
        => walletId is int id ? await _db.Wallets.FindAsync(new object?[] { id }, ct) : null;

    /// <summary>Carga en un solo query las billeteras referenciadas por la página.</summary>
    private async Task<Dictionary<int, Wallet>> LoadWalletsAsync(IEnumerable<int?> walletIds, CancellationToken ct)
    {
        var ids = walletIds.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, Wallet>();
        var wallets = await _db.Wallets.Where(w => ids.Contains(w.Id)).ToListAsync(ct);
        return wallets.ToDictionary(w => w.Id);
    }

    private static Wallet? WalletFor(Dictionary<int, Wallet> wallets, int? walletId)
        => walletId is int id && wallets.TryGetValue(id, out var w) ? w : null;

    private static PendingPaymentDto Map(PendingPayment p, Wallet? wallet) => new(
        Id: p.Id,
        Name: p.Name,
        Description: p.Description,
        Amount: Money.ToNum(p.Amount),
        Fee: Money.ToNum(p.Fee),
        Currency: p.Currency,
        Type: p.Type,
        Category: p.Category?.Name,
        CategoryId: p.CategoryId,
        WalletId: p.WalletId,
        WalletName: wallet?.Name,
        WalletCurrency: wallet?.Currency,
        DueDate: p.DueDate,
        IsPaid: p.IsPaid,
        PaidAt: p.PaidAt,
        TransactionId: p.TransactionId,
        IsCancelled: p.IsCancelled);
}
