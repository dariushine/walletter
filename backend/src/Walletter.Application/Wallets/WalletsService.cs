using Walletter.Application.Common;
using Walletter.Domain;
using Walletter.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Walletter.Application.Wallets;

public class WalletsService
{
    private readonly IAppDbContext _db;

    public WalletsService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<List<object>> List(CancellationToken ct = default)
    {
        var wallets = await _db.Wallets
            .Where(w => w.IsActive)
            .OrderBy(w => w.Id)
            .ToListAsync(ct);
        return wallets.Select(Map).ToList();
    }

    public async Task<List<object>> ListDeleted(CancellationToken ct = default)
    {
        var wallets = await _db.Wallets
            .Where(w => !w.IsActive)
            .OrderBy(w => w.Id)
            .ToListAsync(ct);
        return wallets.Select(Map).ToList();
    }

    public async Task<Wallet?> FindById(int id, CancellationToken ct = default)
    {
        return await _db.Wallets.FirstOrDefaultAsync(w => w.Id == id && w.IsActive, ct);
    }

    public async Task<object?> Detail(int id, CancellationToken ct = default)
    {
        var w = await FindById(id, ct);
        return w == null ? null : Map(w);
    }

    public async Task<object> Create(CreateWalletCommand cmd, CancellationToken ct = default)
    {
        var wallet = new Wallet
        {
            Name = cmd.Name,
            Type = cmd.Type,
            Currency = cmd.Currency,
            Balance = Money.ToInt(cmd.Balance ?? 0),
            Alias = cmd.Alias,
            Description = cmd.Description,
            Icon = cmd.Icon,
            Color = cmd.Color,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Wallets.Add(wallet);
        await _db.SaveChangesAsync(ct);
        return Map(wallet);
    }

    public async Task<object> Update(int id, UpdateWalletCommand cmd, CancellationToken ct = default)
    {
        var existing = await FindById(id, ct) ?? throw new NotFoundException("Billetera no encontrada");
        if (cmd.Name != null) existing.Name = cmd.Name;
        if (cmd.Alias != null) existing.Alias = cmd.Alias;
        if (cmd.Description != null) existing.Description = cmd.Description;
        if (cmd.Icon != null) existing.Icon = cmd.Icon;
        if (cmd.Color != null) existing.Color = cmd.Color;
        if (cmd.Balance is decimal balance) existing.Balance = Money.ToInt(balance);
        await _db.SaveChangesAsync(ct);
        return Map(existing);
    }

    public async Task<object> Remove(int id, CancellationToken ct = default)
    {
        var existing = await FindById(id, ct) ?? throw new NotFoundException("Billetera no encontrada");
        existing.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return Map(existing);
    }

    public async Task<object> Reactivate(int id, CancellationToken ct = default)
    {
        var existing = await _db.Wallets.FirstOrDefaultAsync(w => w.Id == id && !w.IsActive, ct)
            ?? throw new NotFoundException("Billetera no encontrada o ya activa");
        existing.IsActive = true;
        await _db.SaveChangesAsync(ct);
        return Map(existing);
    }

    public async Task<object> Report(int id, CancellationToken ct = default)
    {
        var wallet = await FindById(id, ct) ?? throw new NotFoundException("Billetera no encontrada");
        var txs = await _db.Transactions
            .Include(t => t.Category)
            .Where(t => t.WalletId == id && !t.Deleted)
            .OrderBy(t => t.Id)
            .ToListAsync(ct);

        return new
        {
            wallet = Map(wallet),
            transactions = txs.Select(t => new
            {
                id = t.Id,
                type = t.Type,
                amount = Money.ToNum(t.Amount),
                fee = Money.ToNum(t.Fee),
                category = t.Category?.Name,
                description = t.Description,
                datetimeUtc = t.DatetimeUtc,
                parentTransactionId = t.ParentId,
            }).ToList(),
        };
    }

    private static object Map(Wallet w) => new
    {
        id = w.Id,
        name = w.Name,
        alias = w.Alias,
        type = w.Type,
        currency = w.Currency,
        balance = Money.ToNum(w.Balance),
        description = w.Description,
        icon = w.Icon,
        color = w.Color,
        isActive = w.IsActive,
        excludeFromTotal = w.ExcludeFromTotal,
        hideInDashboard = w.HideInDashboard,
        createdAt = w.CreatedAt,
    };
}
