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
/// Filtros de búsqueda de GET /transactions (texto libre, categoría, tipo).
/// Deben aplicarse SERVER-SIDE en la query LINQ, antes de CountAsync/Skip/Take,
/// para que el total refleje las filas filtradas y no solo la página.
/// </summary>
public class TransactionFilterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly ITestOutputHelper _out;

    public TransactionFilterTests(ITestOutputHelper output)
    {
        _out = output;
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"walletter_filters2_{Guid.NewGuid():N}.db");
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

    private async Task<int> CreateTxAsync(int walletId, decimal amount, string cat, string type, string date, string description)
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TransactionsService>();
        var r = await svc.Create(new CreateTransactionCommand
        {
            WalletId = walletId, CategoryName = cat, Type = type, Amount = amount, Fee = 0m,
            Date = date, Time = "10:00", Description = description, Tz = "America/Caracas",
        });
        return r.Id;
    }

    private static (int Total, int Count) Inspect(object result)
    {
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return (root.GetProperty("total").GetInt32(), root.GetProperty("data").GetArrayLength());
    }

    private async Task<int> CategoryIdAsync(string name)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();
        return (await db.Categories.AsNoTracking().FirstAsync(c => c.Name == name)).Id;
    }

    [Fact]
    public async Task Query_filter_matches_description_case_insensitively()
    {
        var w = await CreateWalletAsync("W1", 5000m);
        await CreateTxAsync(w, 10m, "comida", "expense", "2026-10-01", "Supermercado Central");
        await CreateTxAsync(w, 20m, "comida", "expense", "2026-10-02", "Cafe de la esquina");
        await CreateTxAsync(w, 30m, "salario", "income", "2026-10-03", "Pago mensual");

        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TransactionsService>();

        var (total, count) = Inspect(await svc.List(new ListTransactionsQuery
        {
            WalletId = w, Query = "superMER", Limit = 20,
        }));
        _out.WriteLine($"[FILTER] query='superMER': total={total} filas={count}; esperado 1");
        Assert.Equal(1, total);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CategoryId_filter_restricts_rows_and_total()
    {
        var w = await CreateWalletAsync("W2", 5000m);
        await CreateTxAsync(w, 10m, "comida", "expense", "2026-10-01", "A");
        await CreateTxAsync(w, 20m, "comida", "expense", "2026-10-02", "B");
        await CreateTxAsync(w, 30m, "salario", "income", "2026-10-03", "C");

        var comidaId = await CategoryIdAsync("comida");

        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TransactionsService>();

        var (total, count) = Inspect(await svc.List(new ListTransactionsQuery
        {
            WalletId = w, CategoryId = comidaId, Limit = 20,
        }));
        _out.WriteLine($"[FILTER] categoryId={comidaId}: total={total} filas={count}; esperado 2");
        Assert.Equal(2, total);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Type_filter_restricts_rows_and_total()
    {
        var w = await CreateWalletAsync("W3", 5000m);
        await CreateTxAsync(w, 10m, "comida", "expense", "2026-10-01", "A");
        await CreateTxAsync(w, 30m, "salario", "income", "2026-10-03", "C");
        await CreateTxAsync(w, 40m, "salario", "income", "2026-10-04", "D");

        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TransactionsService>();

        var (total, count) = Inspect(await svc.List(new ListTransactionsQuery
        {
            WalletId = w, Type = "income", Limit = 20,
        }));
        _out.WriteLine($"[FILTER] type=income: total={total} filas={count}; esperado 2");
        Assert.Equal(2, total);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Filters_combine_with_date_range_and_total_is_filtered_count()
    {
        var w = await CreateWalletAsync("W4", 5000m);
        await CreateTxAsync(w, 10m, "comida", "expense", "2026-10-01", "Super compra A");
        await CreateTxAsync(w, 20m, "comida", "expense", "2026-10-15", "Super compra B");
        await CreateTxAsync(w, 30m, "comida", "expense", "2026-11-01", "Super compra C");
        await CreateTxAsync(w, 40m, "salario", "income", "2026-10-20", "Super ingreso");

        var comidaId = await CategoryIdAsync("comida");

        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TransactionsService>();

        // Rango oct + categoría comida + texto "super" → solo A y B (C es de nov, D es income).
        var (total, count) = Inspect(await svc.List(new ListTransactionsQuery
        {
            WalletId = w, CategoryId = comidaId, Query = "super", From = "2026-10-01", To = "2026-10-31", Limit = 20,
        }));
        _out.WriteLine($"[FILTER] combo (categoría+texto+rango): total={total} filas={count}; esperado 2");

        // El total DEBE ser el número de filas filtradas, no el total de la tabla.
        var (allTotal, _) = Inspect(await svc.List(new ListTransactionsQuery { WalletId = w, Limit = 20 }));
        Assert.Equal(4, allTotal);
        Assert.Equal(2, total);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Filter_plus_pagination_total_stays_filtered()
    {
        var w = await CreateWalletAsync("W5", 5000m);
        for (int i = 1; i <= 5; i++)
            await CreateTxAsync(w, 10m, "comida", "expense", $"2026-10-0{i}", $"compra {i}");

        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TransactionsService>();

        // 5 coinciden; limit=2 → página 1 devuelve 2 filas pero total=5.
        var (total, count) = Inspect(await svc.List(new ListTransactionsQuery
        {
            WalletId = w, Query = "compra", Page = 1, Limit = 2,
        }));
        _out.WriteLine($"[FILTER] query='compra' limit=2 page=1: total={total} filas={count}; esperado total=5 filas=2");
        Assert.Equal(5, total);
        Assert.Equal(2, count);
    }
}
