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
/// BUG 2 (alto): el filtro de fechas de GET /transactions trataba `to` como
/// EXCLUSIVO, así que `from == to` devolvía cero filas y se perdía el último
/// día del rango. Estos tests fijan el contrato inclusivo del borde superior.
/// </summary>
public class TransactionDateFilterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly ITestOutputHelper _out;

    public TransactionDateFilterTests(ITestOutputHelper output)
    {
        _out = output;
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"walletter_filter_{Guid.NewGuid():N}.db");
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

    private async Task<int> CreateWalletAsync(string name, decimal balance, string currency = "USD")
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();
        var wallet = new Wallet
        {
            Name = name,
            Currency = currency,
            Balance = Money.ToInt(balance),
            Type = "bank",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.Wallets.Add(wallet);
        await db.SaveChangesAsync();
        return wallet.Id;
    }

    private async Task CreateTxAsync(int walletId, decimal amount, string cat, string type, string date)
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TransactionsService>();
        await service.Create(new CreateTransactionCommand
        {
            WalletId = walletId,
            CategoryName = cat,
            Type = type,
            Amount = amount,
            Fee = 0m,
            Date = date,
            Time = "10:00",
            Description = cat,
            Tz = "America/Caracas",
        });
    }

    /// <summary>Extrae el total y la fecha (YYYY-MM-DD) de cada fila del resultado.</summary>
    private static (int Total, int Count, string[] Dates) Inspect(object result)
    {
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var total = root.GetProperty("total").GetInt32();
        var data = root.GetProperty("data");
        var dates = new string[data.GetArrayLength()];
        for (int i = 0; i < dates.Length; i++)
            dates[i] = data[i].GetProperty("date").GetString() ?? "";
        return (total, dates.Length, dates);
    }

    [Fact]
    public async Task From_to_equal_date_is_inclusive()
    {
        var walletId = await CreateWalletAsync("FilterWallet", 1000m);
        await CreateTxAsync(walletId, 100m, "comida", "expense", "2026-10-14");
        await CreateTxAsync(walletId, 200m, "comida", "expense", "2026-10-15");
        await CreateTxAsync(walletId, 50m, "salario", "income", "2026-10-15");
        await CreateTxAsync(walletId, 300m, "comida", "expense", "2026-10-16");

        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TransactionsService>();

        // from == to = 2026-10-15 → las 2 filas de ese día (antes: 0 filas)
        var (total, count, dates) = Inspect(await service.List(new ListTransactionsQuery
        {
            WalletId = walletId,
            From = "2026-10-15",
            To = "2026-10-15",
            Limit = 20,
        }));
        _out.WriteLine($"[BUG2] from==to 15-oct: total={total} filas={count} dates={string.Join(",", dates)}");
        Assert.Equal(2, count);
        Assert.Equal(2, total);
        Assert.All(dates, d => Assert.Equal("2026-10-15", d));
    }

    [Fact]
    public async Task Range_includes_both_edges()
    {
        var walletId = await CreateWalletAsync("RangeWallet", 1000m);
        await CreateTxAsync(walletId, 100m, "comida", "expense", "2026-10-14");
        await CreateTxAsync(walletId, 200m, "comida", "expense", "2026-10-15");
        await CreateTxAsync(walletId, 50m, "salario", "income", "2026-10-15");
        await CreateTxAsync(walletId, 300m, "comida", "expense", "2026-10-16");

        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TransactionsService>();

        // 14 → 15 inclusive: 3 filas (14 y 15), la del 16 NO entra.
        var (total, count, dates) = Inspect(await service.List(new ListTransactionsQuery
        {
            WalletId = walletId,
            From = "2026-10-14",
            To = "2026-10-15",
            Limit = 20,
        }));
        _out.WriteLine($"[BUG2] from 14 to 15: total={total} filas={count} dates={string.Join(",", dates)}");
        Assert.Equal(3, count);
        Assert.DoesNotContain("2026-10-16", dates);

        // 14 → 16 inclusive: 4 filas (mes completo del 14 al 16).
        (total, count, dates) = Inspect(await service.List(new ListTransactionsQuery
        {
            WalletId = walletId,
            From = "2026-10-14",
            To = "2026-10-16",
            Limit = 20,
        }));
        _out.WriteLine($"[BUG2] from 14 to 16: total={total} filas={count}");
        Assert.Equal(4, count);
    }

    [Fact]
    public async Task From_equals_to_minus_one_day()
    {
        var walletId = await CreateWalletAsync("AdjacentWallet", 1000m);
        await CreateTxAsync(walletId, 500m, "comida", "expense", "2026-10-14");
        await CreateTxAsync(walletId, 100m, "comida", "expense", "2026-10-15");

        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TransactionsService>();

        // 14 → 15 debe incluir AMBOS días (el 15 es el borde superior inclusivo).
        var (total, count, dates) = Inspect(await service.List(new ListTransactionsQuery
        {
            WalletId = walletId,
            From = "2026-10-14",
            To = "2026-10-15",
            Limit = 20,
        }));
        _out.WriteLine($"[BUG2] from=14 to=15 (borde): total={total} filas={count} dates={string.Join(",", dates)}");
        Assert.Equal(2, count);
        Assert.Contains("2026-10-14", dates);
        Assert.Contains("2026-10-15", dates);
    }
}
