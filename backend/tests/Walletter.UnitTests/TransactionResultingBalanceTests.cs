using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Walletter.Application.Categories;
using Walletter.Application.Common;
using Walletter.Application.Transactions;
using Walletter.Domain;
using Walletter.Domain.Entities;
using Walletter.Infrastructure.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace Walletter.UnitTests;

/// <summary>
/// BUG 5 (bajo pero engañoso): el detalle de transacción devolvía
/// `resultingBalance` = saldo ACTUAL de la billetera, no el saldo que quedó tras
/// esa fila. Se leía como histórico y descuadraba auditorías. Ahora es un running
/// sum real (saldo actual menos el efecto de las filas posteriores), calculado en
/// vivo y nunca persistido.
/// </summary>
public class TransactionResultingBalanceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly ITestOutputHelper _out;

    public TransactionResultingBalanceTests(ITestOutputHelper output)
    {
        _out = output;
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"walletter_rb_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath};Cache=Shared";

        var services = new ServiceCollection();
        services.AddDbContext<WalletterDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<WalletterDbContext>());
        services.AddScoped<CategoriesService>();
        services.AddScoped<TransactionsService>();
        services.AddScoped<Walletter.Application.Rates.RatesService>();
        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();
        db.Database.EnsureCreated();
        db.Categories.AddRange(
            new Category { Name = "comida", Type = "expense", IsActive = true, CreatedAt = DateTime.UtcNow },
            new Category { Name = "salario", Type = "income", IsActive = true, CreatedAt = DateTime.UtcNow },
            new Category { Name = "fee", Type = "expense", IsActive = true, CreatedAt = DateTime.UtcNow }
        );
        db.SaveChanges();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        SqliteConnection.ClearAllPools();
        try { if (System.IO.File.Exists(_dbPath)) System.IO.File.Delete(_dbPath); } catch { }
    }

    private async Task<int> CreateWalletAsync(string name, decimal balance)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();
        var w = new Wallet { Name = name, Currency = "USD", Balance = Money.ToInt(balance), Type = "bank", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Wallets.Add(w);
        await db.SaveChangesAsync();
        return w.Id;
    }

    private async Task<int> CreateTxAsync(int walletId, decimal amount, string cat, string type, string date, string time, decimal fee = 0m)
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TransactionsService>();
        var r = await svc.Create(new CreateTransactionCommand
        {
            WalletId = walletId, CategoryName = cat, Type = type, Amount = amount, Fee = fee,
            Date = date, Time = time, Description = cat, Tz = "America/Caracas",
        });
        return r.Id;
    }

    private static decimal ResultingBalance(object detail)
    {
        var json = JsonSerializer.Serialize(detail);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("resultingBalance").GetDecimal();
    }

    [Fact]
    public async Task OldTransaction_DoesNotReturnCurrentBalance()
    {
        // Saldo inicial 1000. Ago: -100 (->900). Oct: +500 (->1400, saldo ACTUAL).
        var w = await CreateWalletAsync("RB", 1000m);
        await CreateTxAsync(w, 100m, "comida", "expense", "2026-08-28", "10:00");   // resulting 900
        var octId = await CreateTxAsync(w, 500m, "salario", "income", "2026-10-14", "10:00"); // resulting 1400 (última)

        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TransactionsService>();
        var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();

        // El saldo actual real de la billetera (para contrastar).
        var current = Money.ToNum((await db.Wallets.AsNoTracking().FirstAsync(x => x.Id == w)).Balance);
        Assert.Equal(1400m, current);

        var oldId = (await db.Transactions.AsNoTracking().OrderBy(t => t.DatetimeUtc).FirstAsync(t => t.WalletId == w)).Id;
        var oldDetail = await svc.Detail(oldId);

        var rbOld = ResultingBalance(oldDetail);
        _out.WriteLine($"[BUG5] tx vieja (ago) resultingBalance={rbOld} (saldo actual={current}); esperado 900");
        // Antes devolvía 1400 (el saldo actual). Debe devolver el saldo tras esa fila: 900.
        Assert.Equal(900m, rbOld);
        Assert.NotEqual(current, rbOld);

        // La última transacción sí coincide con el saldo actual.
        var lastDetail = await svc.Detail(octId);
        Assert.Equal(1400m, ResultingBalance(lastDetail));
    }

    [Fact]
    public async Task MiddleTransaction_ReturnsRunningSumAtThatRow()
    {
        var w = await CreateWalletAsync("RB2", 500m);
        await CreateTxAsync(w, 100m, "comida", "expense", "2026-09-01", "09:00");  // 400
        var midId = await CreateTxAsync(w, 200m, "salario", "income", "2026-09-05", "09:00"); // 600
        await CreateTxAsync(w, 50m, "comida", "expense", "2026-09-10", "09:00");   // 550

        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TransactionsService>();
        var rb = ResultingBalance(await svc.Detail(midId));
        _out.WriteLine($"[BUG5] tx intermedia (05-sep) resultingBalance={rb}; esperado 600");
        Assert.Equal(600m, rb);
    }

    [Fact]
    public async Task SameInstantTransactions_UseIdAsTiebreaker()
    {
        // Tres filas con el MISMO instante (misma fecha/hora): el orden desempata por Id.
        var w = await CreateWalletAsync("RB3", 1000m);
        var t1 = await CreateTxAsync(w, 100m, "comida", "expense", "2026-10-01", "12:00"); // 900
        var t2 = await CreateTxAsync(w, 100m, "comida", "expense", "2026-10-01", "12:00"); // 800
        var t3 = await CreateTxAsync(w, 100m, "comida", "expense", "2026-10-01", "12:00"); // 700 (última)

        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TransactionsService>();
        Assert.Equal(900m, ResultingBalance(await svc.Detail(t1)));
        Assert.Equal(800m, ResultingBalance(await svc.Detail(t2)));
        Assert.Equal(700m, ResultingBalance(await svc.Detail(t3)));
    }

    [Fact]
    public async Task DeletedLaterTransaction_DoesNotAffectResultingBalance()
    {
        // Una fila posterior BORRADA no debe contar en el running sum.
        var w = await CreateWalletAsync("RB4", 1000m);
        var first = await CreateTxAsync(w, 100m, "comida", "expense", "2026-10-01", "10:00"); // 900
        var second = await CreateTxAsync(w, 300m, "salario", "income", "2026-10-02", "10:00"); // 1200

        using (var scope = _serviceProvider.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<TransactionsService>();
            await svc.Remove(second); // borra la posterior: saldo vuelve a 900
        }

        using (var scope = _serviceProvider.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<TransactionsService>();
            var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();
            var current = Money.ToNum((await db.Wallets.AsNoTracking().FirstAsync(x => x.Id == w)).Balance);
            _out.WriteLine($"[BUG5] tras borrar la posterior: current={current} rb(tx1)={ResultingBalance(await svc.Detail(first))}");
            Assert.Equal(900m, current);
            Assert.Equal(900m, ResultingBalance(await svc.Detail(first)));
        }
    }
}
