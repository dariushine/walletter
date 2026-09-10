using System.Data;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Walletter.Application.Categories;
using Walletter.Application.Common;
using Walletter.Application.Exchanges;
using Walletter.Application.Pending;
using Walletter.Application.Rates;
using Walletter.Application.Transactions;
using Walletter.Domain;
using Walletter.Domain.Entities;
using Walletter.Infrastructure.Persistence;
using Xunit;

namespace Walletter.UnitTests;

public class PendingPaymentsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;

    public PendingPaymentsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"walletter_pending_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath};Cache=Shared";

        var services = new ServiceCollection();
        services.AddDbContext<WalletterDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<WalletterDbContext>());
        services.AddScoped<CategoriesService>();
        services.AddScoped<TransactionsService>();
        services.AddScoped<ExchangesService>();
        services.AddScoped<RatesService>();
        services.AddScoped<PendingPaymentsService>();

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();
        db.Database.EnsureCreated();

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
            Currency = "USD",
            Balance = Money.ToInt(balance),
            Type = "bank",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.Wallets.Add(wallet);
        await db.SaveChangesAsync();
        return wallet.Id;
    }

    private async Task<PendingPaymentDto> CreatePendingAsync(
        string name, decimal amount, decimal fee = 0m, string? dueDate = null, int? walletId = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PendingPaymentsService>();
        return await service.Create(new CreatePendingPaymentCommand
        {
            Name = name,
            Amount = amount,
            Fee = fee,
            Currency = "USD",
            Type = "expense",
            CategoryName = "Servicios",
            WalletId = walletId,
            DueDate = dueDate,
        });
    }

    private async Task<decimal> GetBalanceAsync(int walletId)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();
        var w = await db.Wallets.AsNoTracking().FirstAsync(x => x.Id == walletId);
        return Money.ToNum(w.Balance);
    }

    private static T GetProp<T>(object result, string prop)
    {
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty(prop).Deserialize<T>()!;
    }

    // ============ CRUD BÁSICO ============

    [Fact]
    public async Task Create_SetsDefaults_AndIsPending()
    {
        var p = await CreatePendingAsync("Luz", 120.50m, 2.5m, "2026-09-30");
        Assert.Equal("Luz", p.Name);
        Assert.Equal(120.50m, p.Amount);
        Assert.Equal(2.5m, p.Fee);
        Assert.Equal("2026-09-30", p.DueDate);
        Assert.False(p.IsPaid);
        Assert.Null(p.PaidAt);
        Assert.Null(p.TransactionId);
        Assert.False(p.IsCancelled);
        Assert.Equal("Servicios", p.Category);
    }

    [Fact]
    public async Task Create_InvalidDueDate_Throws()
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PendingPaymentsService>();
        await Assert.ThrowsAsync<BusinessException>(() => service.Create(new CreatePendingPaymentCommand
        {
            Name = "Luz",
            Amount = 10m,
            Currency = "USD",
            Type = "expense",
            CategoryName = "Servicios",
            DueDate = "30-09-2026",
        }));
    }

    [Fact]
    public async Task List_OnlyActive_AndSortedByDueDate()
    {
        await CreatePendingAsync("Seguro", 100m, dueDate: "2026-10-01");
        await CreatePendingAsync("Luz", 50m, dueDate: "2026-09-15");
        await CreatePendingAsync("Sin vencimiento", 30m);

        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PendingPaymentsService>();
        var all = await service.List(false);
        Assert.Equal(3, all.Count);
        // Sin vencimiento al final, resto ordenado por dueDate.
        Assert.Equal("Luz", all[0].Name);
        Assert.Equal("Seguro", all[1].Name);
        Assert.Equal("Sin vencimiento", all[2].Name);
    }

    [Fact]
    public async Task Update_ChangesFields_AndKeepsPending()
    {
        var p = await CreatePendingAsync("Luz", 100m);
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PendingPaymentsService>();
        var updated = await service.Update(p.Id, new UpdatePendingPaymentCommand
        {
            Amount = 150m,
            DueDate = "2026-12-01",
            Description = "Factura de noviembre",
        });
        Assert.Equal(150m, updated.Amount);
        Assert.Equal("2026-12-01", updated.DueDate);
        Assert.Equal("Factura de noviembre", updated.Description);
        Assert.False(updated.IsPaid);
    }

    [Fact]
    public async Task Remove_Cancels_AndDisappearsFromList()
    {
        var p = await CreatePendingAsync("Luz", 100m);
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PendingPaymentsService>();
        var removed = await service.Remove(p.Id);
        Assert.True(removed.IsCancelled);

        Assert.Empty(await service.List(false));
        Assert.Empty(await service.List(true)); // cancelado no aparece ni en historial
        // Update de un cancelado: no encontrado (404)
        await Assert.ThrowsAsync<NotFoundException>(() => service.Update(p.Id, new UpdatePendingPaymentCommand { Amount = 200m }));
        // Pay de un cancelado: no encontrado (404)
        await Assert.ThrowsAsync<NotFoundException>(() => service.Pay(p.Id, new PayPendingPaymentCommand
        {
            Date = "2026-09-10",
            Time = "10:00",
            Tz = "America/Caracas",
        }));
    }

    // ============ PAGO ============

    [Fact]
    public async Task Pay_CreatesTransaction_AndDeductsFromWallet()
    {
        var walletId = await CreateWalletAsync("Efectivo", 500m);
        var p = await CreatePendingAsync("Luz", 120m, fee: 5m, walletId: walletId);

        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PendingPaymentsService>();
        var result = await service.Pay(p.Id, new PayPendingPaymentCommand
        {
            Date = "2026-09-10",
            Time = "10:00",
            Tz = "America/Caracas",
        });

        var txId = GetProp<int>(result, "transactionId");
        Assert.True(GetProp<bool>(result, "success"));
        Assert.True(txId > 0);

        // Balance: 500 - 120 (monto) - 5 (fee) = 375
        Assert.Equal(375m, await GetBalanceAsync(walletId));

        // El pendiente queda pagado y sale de la lista activa
        var detail = await service.Detail(p.Id);
        Assert.True(detail.IsPaid);
        Assert.NotNull(detail.PaidAt);
        Assert.Equal(txId, detail.TransactionId);
        Assert.Empty(await service.List(false));
    }

    [Fact]
    public async Task Pay_WithoutWallet_Throws_AndStaysPending()
    {
        var p = await CreatePendingAsync("Luz", 120m); // sin billetera

        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PendingPaymentsService>();
        await Assert.ThrowsAsync<BusinessException>(() => service.Pay(p.Id, new PayPendingPaymentCommand
        {
            Date = "2026-09-10",
            Time = "10:00",
            Tz = "America/Caracas",
        }));

        // Sigue pendiente e intacto
        var detail = await service.Detail(p.Id);
        Assert.False(detail.IsPaid);
        Assert.Null(detail.TransactionId);
    }

    [Fact]
    public async Task Pay_WithOverrideWallet_AndOverrideAmount_UsesOverrides()
    {
        var walletA = await CreateWalletAsync("A", 1000m);
        var walletB = await CreateWalletAsync("B", 1000m);
        var p = await CreatePendingAsync("Gimnasio", 50m, walletId: walletA);

        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PendingPaymentsService>();
        await service.Pay(p.Id, new PayPendingPaymentCommand
        {
            Date = "2026-09-10",
            Time = "10:00",
            Tz = "America/Caracas",
            OverrideWalletId = walletB,
            OverrideAmount = 60m,
        });

        // Se descontó de la billetera B y con el monto override
        Assert.Equal(1000m, await GetBalanceAsync(walletA));
        Assert.Equal(940m, await GetBalanceAsync(walletB)); // 1000 - 60
    }

    [Fact]
    public async Task Pay_InsufficientFunds_Fails_AndStaysPending()
    {
        var walletId = await CreateWalletAsync("Pobre", 10m);
        var p = await CreatePendingAsync("Luz", 120m, walletId: walletId);

        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PendingPaymentsService>();
        await Assert.ThrowsAsync<BusinessException>(() => service.Pay(p.Id, new PayPendingPaymentCommand
        {
            Date = "2026-09-10",
            Time = "10:00",
            Tz = "America/Caracas",
        }));

        // Balance intacto y pendiente sigue sin pagar
        Assert.Equal(10m, await GetBalanceAsync(walletId));
        var detail = await service.Detail(p.Id);
        Assert.False(detail.IsPaid);
    }

    // ============ LISTA CON HISTORIAL ============

    [Fact]
    public async Task List_IncludePaid_ReturnsPaidOnes()
    {
        var walletId = await CreateWalletAsync("Efectivo", 1000m);
        var p1 = await CreatePendingAsync("Luz", 100m, walletId: walletId);
        await CreatePendingAsync("Agua", 50m, walletId: walletId);

        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<PendingPaymentsService>();
        await service.Pay(p1.Id, new PayPendingPaymentCommand
        {
            Date = "2026-09-10",
            Time = "10:00",
            Tz = "America/Caracas",
        });

        var activos = await service.List(false);
        Assert.Single(activos);
        Assert.Equal("Agua", activos[0].Name);

        var conHistorial = await service.List(true);
        Assert.Equal(2, conHistorial.Count);
        Assert.Contains(conHistorial, x => x.Name == "Luz" && x.IsPaid);
    }
}
