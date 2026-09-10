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
        return rows.Select(Map).ToList();
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

        var row = new PendingPayment
        {
            Name = cmd.Name,
            Description = cmd.Description,
            Amount = Money.ToInt(cmd.Amount),
            Fee = Money.ToInt(cmd.Fee),
            Currency = cmd.Currency,
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
        return Map(row);
    }

    public async Task<PendingPaymentDto> Remove(int id, CancellationToken ct = default)
    {
        var row = await GetActiveAsync(id, ct);
        row.IsCancelled = true;
        await _db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<PendingPaymentDto> Detail(int id, CancellationToken ct = default)
    {
        var row = await _db.PendingPayments
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Pago pendiente no encontrado");
        return Map(row);
    }

    public async Task<PendingPaymentDto> Update(int id, UpdatePendingPaymentCommand cmd, CancellationToken ct = default)
    {
        var existing = await GetActiveAsync(id, ct);
        if (cmd.Name != null) existing.Name = cmd.Name;
        if (cmd.Description != null) existing.Description = cmd.Description;
        if (cmd.Amount is decimal a) existing.Amount = Money.ToInt(a);
        if (cmd.Fee is decimal f) existing.Fee = Money.ToInt(f);
        if (cmd.Currency != null) existing.Currency = cmd.Currency;
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
        return Map(existing);
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
        return Map(row);
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

    private static PendingPaymentDto Map(PendingPayment p) => new(
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
        DueDate: p.DueDate,
        IsPaid: p.IsPaid,
        PaidAt: p.PaidAt,
        TransactionId: p.TransactionId,
        IsCancelled: p.IsCancelled);
}
