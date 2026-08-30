using Walletter.Application.Categories;
using Walletter.Application.Common;
using Walletter.Application.Transactions;
using Walletter.Domain;
using Walletter.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Walletter.Application.Recurring;

public class RecurringService
{
    private readonly IAppDbContext _db;
    private readonly CategoriesService _categories;
    private readonly TransactionsService _transactions;

    public RecurringService(IAppDbContext db, CategoriesService categories, TransactionsService transactions)
    {
        _db = db;
        _categories = categories;
        _transactions = transactions;
    }

    public async Task<List<object>> List(CancellationToken ct = default)
    {
        var rows = await _db.RecurringPayments
            .Include(r => r.Category)
            .Where(r => r.IsActive)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<object> Create(CreateRecurringCommand cmd, CancellationToken ct = default)
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

        var row = new RecurringPayment
        {
            Name = cmd.Name,
            Description = cmd.Description,
            Amount = Money.ToInt(cmd.Amount),
            Fee = Money.ToInt(cmd.Fee),
            Currency = cmd.Currency,
            Type = cmd.Type,
            CategoryId = categoryId.Value,
            WalletId = cmd.WalletId,
            IsActive = true,
            IsSubscription = cmd.IsSubscription,
            BillingDay = cmd.BillingDay,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.RecurringPayments.Add(row);
        await _db.SaveChangesAsync(ct);
        var loadedCategory = await _db.Categories.FindAsync(new object?[] { row.CategoryId }, ct);
        row.Category = loadedCategory!;
        return Map(row);
    }

    public async Task<object> Remove(int id, CancellationToken ct = default)
    {
        var row = await _db.RecurringPayments.FindAsync(new object?[] { id }, ct)
            ?? throw new NotFoundException("Pago recurrente no encontrado");
        row.IsActive = false;
        await _db.SaveChangesAsync(ct);
        var loadedCategory = await _db.Categories.FindAsync(new object?[] { row.CategoryId }, ct);
        row.Category = loadedCategory!;
        return Map(row);
    }

    public async Task<object> Detail(int id, CancellationToken ct = default)
    {
        var row = await _db.RecurringPayments
            .Include(r => r.Category)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("Pago recurrente no encontrado");
        return new
        {
            id = row.Id,
            name = row.Name,
            description = row.Description,
            amount = Money.ToNum(row.Amount),
            fee = Money.ToNum(row.Fee),
            currency = row.Currency,
            type = row.Type,
            category = row.Category?.Name,
            categoryId = row.CategoryId,
            walletId = row.WalletId,
            isActive = row.IsActive,
            isSubscription = row.IsSubscription,
            billingDay = row.BillingDay,
        };
    }

    public async Task<object> Update(int id, UpdateRecurringCommand cmd, CancellationToken ct = default)
    {
        var existing = await _db.RecurringPayments.FindAsync(new object?[] { id }, ct)
            ?? throw new NotFoundException("Pago recurrente no encontrado");
        if (cmd.Name != null) existing.Name = cmd.Name;
        if (cmd.Description != null) existing.Description = cmd.Description;
        if (cmd.Amount is decimal a) existing.Amount = Money.ToInt(a);
        if (cmd.Fee is decimal f) existing.Fee = Money.ToInt(f);
        if (cmd.Currency != null) existing.Currency = cmd.Currency;
        if (cmd.Type != null) existing.Type = cmd.Type;
        if (cmd.WalletId is int w) existing.WalletId = w;
        if (cmd.IsSubscription is bool sub) existing.IsSubscription = sub;
        if (cmd.BillingDay is int bd) existing.BillingDay = bd;
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
    /// Ejecuta un pago recurrente: delega en TransactionsService.Create (reusa
    /// la lógica atómica de fee + balance), evitando duplicar código.
    /// </summary>
    public async Task<object> Execute(int id, ExecuteRecurringCommand cmd, CancellationToken ct = default)
    {
        var row = await _db.RecurringPayments.FindAsync(new object?[] { id }, ct)
            ?? throw new NotFoundException("Pago recurrente no encontrado");

        var walletId = cmd.OverrideWalletId ?? cmd.WalletId ?? row.WalletId;
        if (walletId == null)
            throw new BusinessException("El pago recurrente no tiene billetera asignada");

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

        return new
        {
            success = true,
            transactionId = created.Id,
            feeTransactionId = created.FeeTransactionId,
        };
    }

    private static object Map(RecurringPayment r) => new
    {
        id = r.Id,
        name = r.Name,
        description = r.Description,
        amount = Money.ToNum(r.Amount),
        fee = Money.ToNum(r.Fee),
        currency = r.Currency,
        type = r.Type,
        category = r.Category?.Name,
        categoryId = r.CategoryId,
        walletId = r.WalletId,
        isActive = r.IsActive,
        isSubscription = r.IsSubscription,
        billingDay = r.BillingDay,
    };
}
