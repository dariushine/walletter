using System.Data;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Walletter.Application.Categories;
using Walletter.Application.Common;
using Walletter.Application.Exchanges;
using Walletter.Application.Transactions;
using Walletter.Domain;
using Walletter.Domain.Entities;
using Walletter.Infrastructure.Persistence;
using Xunit;

namespace Walletter.UnitTests;

public class ConcurrencyTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;

    public ConcurrencyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"walletter_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath};Cache=Shared";

        var services = new ServiceCollection();
        services.AddDbContext<WalletterDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<WalletterDbContext>());
        services.AddScoped<CategoriesService>();
        services.AddScoped<TransactionsService>();
        services.AddScoped<ExchangesService>();

        _serviceProvider = services.BuildServiceProvider();

        // Inicializar esquema
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();
        db.Database.EnsureCreated();

        // Sembrar categorías del sistema
        db.Categories.AddRange(
            new Category { Name = "exchange_out", Type = "expense", IsActive = true, CreatedAt = DateTime.UtcNow },
            new Category { Name = "exchange_in", Type = "income", IsActive = true, CreatedAt = DateTime.UtcNow },
            new Category { Name = "fee", Type = "expense", IsActive = true, CreatedAt = DateTime.UtcNow }
        );
        db.SaveChanges();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    private async Task<int> CreateWalletAsync(string name, decimal balance)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();
        var wallet = new Wallet
        {
            Name = name,
            Currency = "VES",
            Balance = Money.ToInt(balance),
            Type = "bank",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.Wallets.Add(wallet);
        await db.SaveChangesAsync();
        return wallet.Id;
    }

    private async Task<TransactionCreatedResult> CreateTxAsync(int walletId, decimal amount, string cat = "comida",
        string type = "expense", decimal fee = 0m)
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TransactionsService>();
        return await service.Create(new CreateTransactionCommand
        {
            WalletId = walletId,
            CategoryName = cat,
            Type = type,
            Amount = amount,
            Fee = fee,
            Date = "2026-08-29",
            Time = "11:34",
            Description = cat,
            Tz = "America/Caracas",
        });
    }

    private async Task<decimal> GetBalanceAsync(int walletId)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();
        var w = await db.Wallets.AsNoTracking().FirstAsync(x => x.Id == walletId);
        return Money.ToNum(w.Balance);
    }

    private static int ExtractExchangeId(object result)
    {
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("exchange").GetProperty("id").GetInt32();
    }

    // ============ 1. TRANSACTIONS: CREATE (Caso real de hoy) ============

    [Fact]
    public async Task ConcurrentExpenses_DoNotLoseUpdates_AndMatchExpectedBalance()
    {
        var walletId = await CreateWalletAsync("Ubii Test", 26625m);

        var amounts = new decimal[] { 300m, 900m, 5621.06m, 9377m, 8533m };
        var totalExpenses = amounts.Sum();
        var expectedBalance = 26625m - totalExpenses; // 1893.94m

        var tasks = amounts.Select(async (amount, idx) =>
        {
            using var scope = _serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TransactionsService>();
            return await service.Create(new CreateTransactionCommand
            {
                WalletId = walletId,
                CategoryName = idx < 2 ? "Telefonía" : "comida",
                Type = "expense",
                Amount = amount,
                Date = "2026-08-29",
                Time = "11:34",
                Description = $"Concurrent test tx #{idx + 1}",
                Tz = "America/Caracas",
            });
        });

        var results = await Task.WhenAll(tasks);
        Assert.Equal(5, results.Length);
        Assert.Equal(expectedBalance, await GetBalanceAsync(walletId));
    }

    // ============ 2. TRANSACTIONS: UPDATE ============

    [Fact]
    public async Task ConcurrentUpdates_DoNotLoseUpdates_AndMatchExpectedBalance()
    {
        var walletId = await CreateWalletAsync("Ubii Update", 10000m);

        // 5 gastos de 1000 secuenciales -> balance 5000
        var ids = new List<int>();
        for (int i = 0; i < 5; i++)
            ids.Add((await CreateTxAsync(walletId, 1000m)).Id);
        Assert.Equal(5000m, await GetBalanceAsync(walletId));

        // Concurrencia: actualizar cada monto de 1000 -> 2000 en paralelo
        var tasks = ids.Select(id => Task.Run(async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TransactionsService>();
            await service.Update(id, new UpdateTransactionCommand { Amount = 2000m, Tz = "America/Caracas" });
        }));
        await Task.WhenAll(tasks);

        // Balance final = 10000 - 5*2000 = 0
        Assert.Equal(0m, await GetBalanceAsync(walletId));
    }

    // ============ 3. TRANSACTIONS: REMOVE ============

    [Fact]
    public async Task ConcurrentRemove_DoNotLoseUpdates_AndMatchExpectedBalance()
    {
        var walletId = await CreateWalletAsync("Ubii Remove", 5000m);

        var ids = new List<int>();
        for (int i = 0; i < 5; i++)
            ids.Add((await CreateTxAsync(walletId, 500m)).Id);
        Assert.Equal(2500m, await GetBalanceAsync(walletId));

        // Concurrencia: borrar las 5 en paralelo -> revierte todo el gasto
        var tasks = ids.Select(id => Task.Run(async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TransactionsService>();
            await service.Remove(id);
        }));
        await Task.WhenAll(tasks);

        Assert.Equal(5000m, await GetBalanceAsync(walletId));
    }

    // ============ 4. TRANSACTIONS: ADD FEE ============

    [Fact]
    public async Task ConcurrentAddFee_DoNotLoseUpdates_AndMatchExpectedBalance()
    {
        var walletId = await CreateWalletAsync("Ubii Fee", 5000m);
        var parentId = (await CreateTxAsync(walletId, 1000m)).Id; // balance 4000

        // Concurrencia: 5 comisiones de 100 al mismo padre en paralelo -> balance 3500
        var tasks = Enumerable.Range(1, 5).Select(_ => Task.Run(async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TransactionsService>();
            await service.AddFee(parentId, new AddFeeCommand
            {
                Amount = 100m,
                Date = "2026-08-29",
                Time = "12:00",
                Tz = "America/Caracas",
            });
        }));
        await Task.WhenAll(tasks);

        Assert.Equal(3500m, await GetBalanceAsync(walletId));
    }

    // ============ 5. TRANSACTIONS: ASSOCIATE ============

    [Fact]
    public async Task ConcurrentAssociate_DoNotLoseUpdates_AndMatchExpectedBalance()
    {
        // Creamos wallet con 5000 y una transacción de gasto padre de 500
        // Balance resultante tras crear el padre: 5000 - 500 = 4500
        var walletId = await CreateWalletAsync("Ubii Assoc", 5000m);
        var parentId = (await CreateTxAsync(walletId, 500m, "comida", "expense")).Id;
        Assert.Equal(4500m, await GetBalanceAsync(walletId));

        // Concurrencia: 5 transacciones asociadas (hijas) de 500 c/u en paralelo -> resta 2500 más
        var tasks = Enumerable.Range(1, 5).Select(_ => Task.Run(async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TransactionsService>();
            await service.Associate(parentId, new AssociateTransactionCommand
            {
                Amount = 500m,
                Type = "expense",
                CategoryName = "comida",
                Date = "2026-08-29",
                Time = "12:00",
                Tz = "America/Caracas",
            });
        }));
        await Task.WhenAll(tasks);

        // 4500 - 5*500 = 2000
        Assert.Equal(2000m, await GetBalanceAsync(walletId));
    }

    // ============ 6. EXCHANGES: CREATE ============

    [Fact]
    public async Task ConcurrentExchanges_DoNotLoseUpdates_AndMatchExpectedBalances()
    {
        var fromId = await CreateWalletAsync("USD Source", 1000m);
        var toId = await CreateWalletAsync("VES Dest", 0m);

        var tasks = Enumerable.Range(1, 5).Select(i => Task.Run(async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ExchangesService>();
            await service.Create(new CreateExchangeCommand
            {
                FromWalletId = fromId,
                ToWalletId = toId,
                FromAmount = 100m,
                ToAmount = 3600m,
                Date = "2026-08-29",
                Time = "12:00",
                Description = $"Exchange #{i}",
                Tz = "America/Caracas",
            });
        }));
        await Task.WhenAll(tasks);

        Assert.Equal(500m, await GetBalanceAsync(fromId));
        Assert.Equal(18000m, await GetBalanceAsync(toId));
    }

    // ============ 7. EXCHANGES: UPDATE ============

    [Fact]
    public async Task ConcurrentExchangeUpdates_DoNotLoseUpdates_AndMatchExpectedBalances()
    {
        var fromId = await CreateWalletAsync("USD Upd", 1000m);
        var toId = await CreateWalletAsync("VES Upd", 0m);

        // Crear 5 exchanges de $100 -> 3600 secuenciales
        var exchangeIds = new List<int>();
        for (int i = 0; i < 5; i++)
        {
            using var scope = _serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ExchangesService>();
            var res = await service.Create(new CreateExchangeCommand
            {
                FromWalletId = fromId,
                ToWalletId = toId,
                FromAmount = 100m,
                ToAmount = 3600m,
                Date = "2026-08-29",
                Time = "12:00",
                Description = $"Exchange #{i}",
                Tz = "America/Caracas",
            });
            exchangeIds.Add(ExtractExchangeId(res));
        }
        Assert.Equal(500m, await GetBalanceAsync(fromId));
        Assert.Equal(18000m, await GetBalanceAsync(toId));

        // Concurrencia: actualizar cada uno a $50 -> 1800 (en paralelo)
        var tasks = exchangeIds.Select(id => Task.Run(async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ExchangesService>();
            await service.Update(id, new UpdateExchangeCommand { FromAmount = 50m, ToAmount = 1800m, Tz = "America/Caracas" });
        }));
        await Task.WhenAll(tasks);

        // from: 500 + 5*(100-50) = 750 ; to: 18000 - 5*(3600-1800) = 9000
        Assert.Equal(750m, await GetBalanceAsync(fromId));
        Assert.Equal(9000m, await GetBalanceAsync(toId));
    }

    // ============ 8. EXCHANGES: REMOVE ============

    [Fact]
    public async Task ConcurrentExchangeRemove_DoNotLoseUpdates_AndMatchExpectedBalances()
    {
        var fromId = await CreateWalletAsync("USD Rem", 1000m);
        var toId = await CreateWalletAsync("VES Rem", 0m);

        // Crear 5 exchanges de $100 -> 3600 secuenciales
        var exchangeIds = new List<int>();
        for (int i = 0; i < 5; i++)
        {
            using var scope = _serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ExchangesService>();
            var res = await service.Create(new CreateExchangeCommand
            {
                FromWalletId = fromId,
                ToWalletId = toId,
                FromAmount = 100m,
                ToAmount = 3600m,
                Date = "2026-08-29",
                Time = "12:00",
                Description = $"Exchange #{i}",
                Tz = "America/Caracas",
            });
            exchangeIds.Add(ExtractExchangeId(res));
        }
        Assert.Equal(500m, await GetBalanceAsync(fromId));
        Assert.Equal(18000m, await GetBalanceAsync(toId));

        // Concurrencia: borrar los 5 en paralelo -> revierte todo
        var tasks = exchangeIds.Select(id => Task.Run(async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ExchangesService>();
            await service.Remove(id);
        }));
        await Task.WhenAll(tasks);

        Assert.Equal(1000m, await GetBalanceAsync(fromId));
        Assert.Equal(0m, await GetBalanceAsync(toId));
    }

    // ============ 9. OPERACIONES MIXTAS CONCURRENTES (Stress Test real) ============

    [Fact]
    public async Task MixedConcurrentOperations_AllBalanceMutationsStayConsistent()
    {
        // Balance inicial: 10.000
        var walletId = await CreateWalletAsync("Stress Test", 10000m);

        // Creamos 2 txs iniciales para tener qué editar y qué borrar:
        // tx1 (gasto 1000) -> balance 9000
        // tx2 (gasto 1000) -> balance 8000
        var tx1 = (await CreateTxAsync(walletId, 1000m)).Id;
        var tx2 = (await CreateTxAsync(walletId, 1000m)).Id;
        Assert.Equal(8000m, await GetBalanceAsync(walletId));

        // Ahora disparamos en paralelo:
        // - Crear 2 nuevos gastos de 500 c/u (-1000)
        // - Crear 1 nuevo ingreso de 2000 (+2000)
        // - Editar tx1 de 1000 -> 1500 (-500)
        // - Borrar tx2 (+1000)
        // - Agregar comisión de 100 a tx1 (-100)
        // Resultado esperado: 8000 - 1000 + 2000 - 500 + 1000 - 100 = 9400
        var taskCreate1 = Task.Run(async () =>
        {
            using var s = _serviceProvider.CreateScope();
            await s.ServiceProvider.GetRequiredService<TransactionsService>().Create(new CreateTransactionCommand
            {
                WalletId = walletId,
                CategoryName = "comida",
                Type = "expense",
                Amount = 500m,
                Date = "2026-08-29",
                Time = "12:00",
                Tz = "America/Caracas",
            });
        });

        var taskCreate2 = Task.Run(async () =>
        {
            using var s = _serviceProvider.CreateScope();
            await s.ServiceProvider.GetRequiredService<TransactionsService>().Create(new CreateTransactionCommand
            {
                WalletId = walletId,
                CategoryName = "comida",
                Type = "expense",
                Amount = 500m,
                Date = "2026-08-29",
                Time = "12:00",
                Tz = "America/Caracas",
            });
        });

        var taskIncome = Task.Run(async () =>
        {
            using var s = _serviceProvider.CreateScope();
            await s.ServiceProvider.GetRequiredService<TransactionsService>().Create(new CreateTransactionCommand
            {
                WalletId = walletId,
                CategoryName = "Inicio",
                Type = "income",
                Amount = 2000m,
                Date = "2026-08-29",
                Time = "12:00",
                Tz = "America/Caracas",
            });
        });

        var taskUpdate = Task.Run(async () =>
        {
            using var s = _serviceProvider.CreateScope();
            await s.ServiceProvider.GetRequiredService<TransactionsService>().Update(tx1, new UpdateTransactionCommand
            {
                Amount = 1500m,
                Tz = "America/Caracas",
            });
        });

        var taskRemove = Task.Run(async () =>
        {
            using var s = _serviceProvider.CreateScope();
            await s.ServiceProvider.GetRequiredService<TransactionsService>().Remove(tx2);
        });

        var taskFee = Task.Run(async () =>
        {
            using var s = _serviceProvider.CreateScope();
            await s.ServiceProvider.GetRequiredService<TransactionsService>().AddFee(tx1, new AddFeeCommand
            {
                Amount = 100m,
                Date = "2026-08-29",
                Time = "12:00",
                Tz = "America/Caracas",
            });
        });

        await Task.WhenAll(taskCreate1, taskCreate2, taskIncome, taskUpdate, taskRemove, taskFee);

        Assert.Equal(9400m, await GetBalanceAsync(walletId));
    }
}
