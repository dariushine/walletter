using Walletter.Application.Common;
using Walletter.Domain;
using Walletter.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Walletter.Application.Stats;

public class StatsService
{
    private readonly IAppDbContext _db;

    public StatsService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<object> Overview(CancellationToken ct = default)
    {
        var income = await _db.Transactions
            .Where(t => !t.Deleted && t.Type == TransactionTypes.Income)
            .SumAsync(t => (long?)t.Amount, ct) ?? 0;
        var expense = await _db.Transactions
            .Where(t => !t.Deleted && t.Type == TransactionTypes.Expense)
            .SumAsync(t => (long?)t.Amount, ct) ?? 0;
        var count = await _db.Transactions.CountAsync(t => !t.Deleted, ct);
        var balance = await _db.Wallets
            .Where(w => w.IsActive)
            .SumAsync(w => (long?)w.Balance, ct) ?? 0;

        var totalIncome = Math.Round(Money.ToNum((int)income), 2);
        var totalExpense = Math.Round(Money.ToNum((int)expense), 2);
        var totalBalance = Money.ToNum((int)balance);
        var net = Math.Round(totalIncome - totalExpense, 2);

        return new
        {
            total_income = totalIncome,
            total_expense = totalExpense,
            net_balance = net,
            total_balance = totalBalance,
            transaction_count = count,
            summary = new
            {
                totalTransactions = count,
                totalIncome,
                totalExpenses = totalExpense,
                net,
                totalBalance,
            },
        };
    }

    public async Task<List<object>> ByCategory(CancellationToken ct = default)
    {
        var grouped = await _db.Transactions
            .Where(t => !t.Deleted)
            .GroupBy(t => new { t.CategoryId, t.Type })
            .Select(g => new { g.Key.CategoryId, g.Key.Type, Sum = g.Sum(x => (long)x.Amount) })
            .ToListAsync(ct);

        var cats = await _db.Categories.AsNoTracking().ToListAsync(ct);
        var nameById = cats.ToDictionary(c => c.Id, c => c.Name);

        return grouped.Select(g => (object)new
        {
            name = nameById.TryGetValue(g.CategoryId, out var n) ? n : "sin categoría",
            type = g.Type,
            total = Money.ToNum((int)g.Sum),
        }).ToList();
    }
}
