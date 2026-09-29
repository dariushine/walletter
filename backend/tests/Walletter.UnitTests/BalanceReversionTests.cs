using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Walletter.Application.Categories;
using Walletter.Application.Common;
using Walletter.Application.Exchanges;
using Walletter.Application.Rates;
using Walletter.Application.Transactions;
using Walletter.Domain;
using Walletter.Domain.Entities;
using Walletter.Infrastructure.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace Walletter.UnitTests;

/// <summary>
/// Regresión de los bugs reportados en producción:
///
/// BUG 1: doble reversión de balance al borrar padre (gasto con fee inline) + hijo
///        fee. El fee se devolvía dos veces (+5030 en vez de +5015).
/// BUG 2: poner fee=0 en un exchange no aplicaba el cambio (intermitente).
///
/// Invariante central que comprueban estos tests:
///   Wallet.Balance == BalanceInicial + Σ (income ? +Amount : -Amount) sobre TODAS
///   las transacciones vigentes (usando el Amount crudo, no el Fee denormalizado,
///   porque la comisión ya tiene su propia fila hija).
///   Es decir: cero drift entre el saldo y el ledger.
/// </summary>
public class BalanceReversionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly ITestOutputHelper _out;

    public BalanceReversionTests(ITestOutputHelper output)
    {
        _out = output;
        _dbPath = Path.Combine(Path.GetTempPath(), $"walletter_balbug_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath};Cache=Shared";

        var services = new ServiceCollection();
        services.AddDbContext<WalletterDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<WalletterDbContext>());
        services.AddScoped<CategoriesService>();
        services.AddScoped<TransactionsService>();
        services.AddScoped<ExchangesService>();
        services.AddScoped<RatesService>();

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

    // ---------------- helpers ----------------

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
            Date = "2026-09-29",
            Time = "10:00",
            Description = cat,
            Tz = "America/Caracas",
        });
    }

    private async Task RemoveAsync(int id)
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TransactionsService>();
        await service.Remove(id);
    }

    private async Task<decimal> GetBalanceAsync(int walletId)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();
        var w = await db.Wallets.AsNoTracking().FirstAsync(x => x.Id == walletId);
        return Money.ToNum(w.Balance);
    }

    /// <summary>Suma con signo de las transacciones VIGENTES usando el Amount crudo.</summary>
    private async Task<decimal> LedgerSumAsync(int walletId)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.WalletId == walletId && !t.Deleted)
            .Select(t => new { t.Type, t.Amount })
            .ToListAsync();
        var sum = rows.Sum(r => r.Type == "income" ? r.Amount : -r.Amount);
        return Money.ToNum(sum);
    }

    private async Task<int> GetParentFeeAsync(int parentId)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();
        var p = await db.Transactions.AsNoTracking().FirstAsync(x => x.Id == parentId);
        return p.Fee;
    }

    private async Task<int> ActiveFeeChildrenAsync(int parentId)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();
        return await db.Transactions.AsNoTracking()
            .CountAsync(t => t.ParentId == parentId && t.Category!.Name == "fee" && !t.Deleted);
    }

    private static int ExtractExchangeId(object result)
    {
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("exchange").GetProperty("id").GetInt32();
    }

    private async Task<int> CreateExchangeAsync(int fromId, int toId, decimal from, decimal to,
        decimal fee = 0m, decimal creditFee = 0m)
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ExchangesService>();
        var res = await service.Create(new CreateExchangeCommand
        {
            FromWalletId = fromId,
            ToWalletId = toId,
            FromAmount = from,
            ToAmount = to,
            Fee = fee,
            CreditFee = creditFee,
            Date = "2026-09-29",
            Time = "10:00",
            Description = "Exchange test",
            Tz = "America/Caracas",
        });
        return ExtractExchangeId(res);
    }

    private async Task UpdateExchangeAsync(int id, UpdateExchangeCommand cmd)
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ExchangesService>();
        await service.Update(id, cmd);
    }

    private async Task<(decimal Fee, decimal CreditFee)> GetExchangeFeesAsync(int id)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();
        var ex = await db.Exchanges.AsNoTracking().FirstAsync(e => e.Id == id);
        return (Money.ToNum(ex.Fee), Money.ToNum(ex.CreditFee));
    }

    private async Task AssertNoDriftAsync(int walletId, decimal initial)
    {
        var balance = await GetBalanceAsync(walletId);
        var ledger = initial + await LedgerSumAsync(walletId);
        Assert.Equal(ledger, balance);
    }

    // ================= BUG 1 =================

    // (a) borrar SOLO el hijo fee -> devuelve exactamente el fee una vez.
    [Fact]
    public async Task Bug1_DeleteOnlyFeeChild_RefundsFeeExactlyOnce()
    {
        const decimal initial = 10000m;
        var walletId = await CreateWalletAsync("Bug1a", initial);
        var parent = await CreateTxAsync(walletId, 5000m, fee: 15m);
        // create: 10000 - (5000 + 15) = 4985
        var afterCreate = await GetBalanceAsync(walletId);
        Assert.Equal(4985m, afterCreate);

        await RemoveAsync(parent.FeeTransactionId!.Value);

        // El fee vuelve UNA sola vez: 4985 + 15 = 5000
        var afterDelete = await GetBalanceAsync(walletId);
        _out.WriteLine($"[BUG1a] inicial=10000 create(5000+fee15)->{afterCreate} | borrar hijo fee -> {afterDelete} (esperado 5000, bug sería 5015)");
        Assert.Equal(5000m, afterDelete);
        Assert.Equal(0, await GetParentFeeAsync(parent.Id)); // fee denormalizado sincronizado
        Assert.Equal(0, await ActiveFeeChildrenAsync(parent.Id));
        await AssertNoDriftAsync(walletId, initial);
    }

    // (b) borrar SOLO el padre (con el hijo fee aún vigente) -> revierte monto + fee.
    [Fact]
    public async Task Bug1_DeleteOnlyParent_RefundsAmountPlusFeeOnce()
    {
        const decimal initial = 10000m;
        var walletId = await CreateWalletAsync("Bug1b", initial);
        var parent = await CreateTxAsync(walletId, 5000m, fee: 15m);
        var afterCreate = await GetBalanceAsync(walletId);
        Assert.Equal(4985m, afterCreate);

        await RemoveAsync(parent.Id);

        // 4985 + (5000 + 15) = 10000 (el fee se revierte con el padre, no por separado)
        var afterDelete = await GetBalanceAsync(walletId);
        _out.WriteLine($"[BUG1b] inicial=10000 create(5000+fee15)->{afterCreate} | borrar padre -> {afterDelete} (esperado 10000)");
        Assert.Equal(10000m, afterDelete);
        Assert.Equal(0, await ActiveFeeChildrenAsync(parent.Id));
        await AssertNoDriftAsync(walletId, initial);
    }

    // (c) borrar padre + hijo en CUALQUIER orden -> nunca cuenta el fee dos veces.
    [Fact]
    public async Task Bug1_DeleteChildThenParent_NoDoubleRefund()
    {
        const decimal initial = 10000m;
        var walletId = await CreateWalletAsync("Bug1c1", initial);
        var parent = await CreateTxAsync(walletId, 5000m, fee: 15m);
        var feeId = parent.FeeTransactionId!.Value;
        Assert.Equal(4985m, await GetBalanceAsync(walletId));

        await RemoveAsync(feeId);   // +15 -> 5000, parent.Fee = 0
        Assert.Equal(5000m, await GetBalanceAsync(walletId));
        await RemoveAsync(parent.Id); // +5000 -> 10000
        var final = await GetBalanceAsync(walletId);
        _out.WriteLine($"[BUG1c] hijo->padre: 4985 ->5000 ->{final} (esperado 10000; bug=10015)");
        Assert.Equal(10000m, final);
        await AssertNoDriftAsync(walletId, initial);
    }

    [Fact]
    public async Task Bug1_DeleteParentThenChild_NoDoubleRefund()
    {
        const decimal initial = 10000m;
        var walletId = await CreateWalletAsync("Bug1c2", initial);
        var parent = await CreateTxAsync(walletId, 5000m, fee: 15m);
        var feeId = parent.FeeTransactionId!.Value;
        Assert.Equal(4985m, await GetBalanceAsync(walletId));

        await RemoveAsync(parent.Id); // +5015 -> 10000 (marca hijo deleted)
        Assert.Equal(10000m, await GetBalanceAsync(walletId));
        await RemoveAsync(feeId);     // idempotente: no debe sumar de nuevo
        var final = await GetBalanceAsync(walletId);
        _out.WriteLine($"[BUG1c] padre->hijo: 4985 ->10000 ->{final} (esperado 10000)");
        Assert.Equal(10000m, final);
        await AssertNoDriftAsync(walletId, initial);
    }

    // (d) doble delete del mismo id -> el segundo es no-op.
    [Fact]
    public async Task Bug1_DoubleDelete_SameIdIsNoOp()
    {
        const decimal initial = 10000m;
        var walletId = await CreateWalletAsync("Bug1d", initial);
        var parent = await CreateTxAsync(walletId, 5000m, fee: 15m);
        var feeId = parent.FeeTransactionId!.Value;

        await RemoveAsync(parent.Id);
        Assert.Equal(10000m, await GetBalanceAsync(walletId));
        var afterFirst = await GetBalanceAsync(walletId);

        // Segundo delete del padre: NO debe tocar el balance.
        await RemoveAsync(parent.Id);
        Assert.Equal(afterFirst, await GetBalanceAsync(walletId));

        // Doble delete del hijo tampoco.
        await RemoveAsync(feeId);
        await RemoveAsync(feeId);
        var final = await GetBalanceAsync(walletId);
        _out.WriteLine($"[BUG1d] doble delete padre y doble delete hijo: 10000 ->{final} (no-op, esperado 10000)");
        Assert.Equal(afterFirst, final);
        await AssertNoDriftAsync(walletId, initial);
    }

    // Caso completo (a)-(d) sobre el mismo ledger: combinaciones mezcladas y sin drift.
    [Fact]
    public async Task Bug1_MixedCombinations_NoDrift()
    {
        const decimal initial = 50000m;
        var walletId = await CreateWalletAsync("Bug1mix", initial);

        // 1: fee inline
        var t1 = await CreateTxAsync(walletId, 5000m, fee: 15m);
        // 2: gasto sin fee
        var t2 = await CreateTxAsync(walletId, 1000m);
        // 3: ingreso
        var t3 = await CreateTxAsync(walletId, 2000m, "Inicio", "income");
        // 4: otro fee inline
        var t4 = await CreateTxAsync(walletId, 300m, fee: 5m);

        await RemoveAsync(t1.FeeTransactionId!.Value); // a
        await RemoveAsync(t2.Id);                      // b
        await RemoveAsync(t3.Id);                      // delete income
        await AssertNoDriftAsync(walletId, initial);

        // t4: padre y luego intento de hijo (idempotente)
        await RemoveAsync(t4.Id);
        await RemoveAsync(t4.FeeTransactionId!.Value);
        await AssertNoDriftAsync(walletId, initial);

        // Solo queda vigente el PADRE t1 (gasto 5000) → 50000 - 5000 = 45000.
        // El fee de t1 se revirtió una sola vez (borrado del hijo), sin doble conteo.
        Assert.Equal(45000m, await GetBalanceAsync(walletId));
    }

    // Editar el monto de un hijo fee vía Update mantiene el Fee del padre sin drift.
    [Fact]
    public async Task Bug1_UpdateFeeChild_RecalculatesParentFee()
    {
        const decimal initial = 10000m;
        var walletId = await CreateWalletAsync("Bug1upd", initial);
        var parent = await CreateTxAsync(walletId, 5000m, fee: 15m);
        var feeId = parent.FeeTransactionId!.Value;
        Assert.Equal(4985m, await GetBalanceAsync(walletId));

        // subir el fee de 15 a 20 -> balance 4980, parent.Fee = 20
        using (var scope = _serviceProvider.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<TransactionsService>();
            await svc.Update(feeId, new UpdateTransactionCommand { Amount = 20m, Tz = "America/Caracas" });
        }
        Assert.Equal(4980m, await GetBalanceAsync(walletId));
        Assert.Equal(2000, await GetParentFeeAsync(parent.Id)); // 20.00 en centavos
        await AssertNoDriftAsync(walletId, initial);
    }

    // ================= BUG 2 =================

    // Poner fee=0 debe aplicar de verdad: fee=0, hija soft-deleted, saldo origen sube el fee.
    [Fact]
    public async Task Bug2_UpdateFeeToZero_AppliesAndRefundsOrigin()
    {
        var fromId = await CreateWalletAsync("USD Bug2", 1000m);
        var toId = await CreateWalletAsync("VES Bug2", 0m, "VES");

        var exId = await CreateExchangeAsync(fromId, toId, 100m, 93000m, fee: 3.75m);
        // from: 1000 - (100 + 3.75) = 896.25
        Assert.Equal(896.25m, await GetBalanceAsync(fromId));

        await UpdateExchangeAsync(exId, new UpdateExchangeCommand { Fee = 0m, Tz = "America/Caracas" });

        var (fee, _) = await GetExchangeFeesAsync(exId);
        Assert.Equal(0m, fee);
        Assert.Equal(900m, await GetBalanceAsync(fromId)); // +3.75
        await AssertNoDriftAsync(fromId, 1000m);
    }

    // Repetible 100%: alternar fee 0 / >0 varias veces mantiene fee y balance correctos.
    [Fact]
    public async Task Bug2_ToggleFeeZero_IsRepeatable()
    {
        var fromId = await CreateWalletAsync("USD Toggle", 1000m);
        var toId = await CreateWalletAsync("VES Toggle", 0m, "VES");
        var exId = await CreateExchangeAsync(fromId, toId, 100m, 93000m, fee: 3.75m);
        Assert.Equal(896.25m, await GetBalanceAsync(fromId));

        for (var i = 0; i < 5; i++)
        {
            await UpdateExchangeAsync(exId, new UpdateExchangeCommand { Fee = 0m, Tz = "America/Caracas" });
            Assert.Equal(0m, (await GetExchangeFeesAsync(exId)).Fee);
            Assert.Equal(900m, await GetBalanceAsync(fromId));
            await AssertNoDriftAsync(fromId, 1000m);

            await UpdateExchangeAsync(exId, new UpdateExchangeCommand { Fee = 3.75m, Tz = "America/Caracas" });
            Assert.Equal(3.75m, (await GetExchangeFeesAsync(exId)).Fee);
            Assert.Equal(896.25m, await GetBalanceAsync(fromId));
            await AssertNoDriftAsync(fromId, 1000m);
        }
    }

    // creditFee=0 también debe aplicarse y devolver el saldo al wallet destino.
    [Fact]
    public async Task Bug2_UpdateCreditFeeToZero_AppliesAndRefundsDestination()
    {
        var fromId = await CreateWalletAsync("USD Bug2c", 1000m);
        var toId = await CreateWalletAsync("VES Bug2c", 0m, "VES");

        var exId = await CreateExchangeAsync(fromId, toId, 100m, 93000m, fee: 0m, creditFee: 2m);
        // to (VES, unidades): 0 + (93000 - 2) = 92998
        Assert.Equal(92998m, await GetBalanceAsync(toId));

        await UpdateExchangeAsync(exId, new UpdateExchangeCommand { CreditFee = 0m, Tz = "America/Caracas" });

        var (_, creditFee) = await GetExchangeFeesAsync(exId);
        Assert.Equal(0m, creditFee);
        Assert.Equal(93000m, await GetBalanceAsync(toId)); // +2
        await AssertNoDriftAsync(toId, 0m);
    }

    // Contrato: omitir el campo fee (null) NO debe tocar la comisión existente.
    [Fact]
    public async Task Bug2_OmittedFee_LeavesFeeUntouched()
    {
        var fromId = await CreateWalletAsync("USD Omit", 1000m);
        var toId = await CreateWalletAsync("VES Omit", 0m, "VES");
        var exId = await CreateExchangeAsync(fromId, toId, 100m, 93000m, fee: 3.75m);

        // Update sin fee (p. ej. solo cambiar descripción): fee vigente.
        await UpdateExchangeAsync(exId, new UpdateExchangeCommand { Description = "otra cosa", Tz = "America/Caracas" });

        Assert.Equal(3.75m, (await GetExchangeFeesAsync(exId)).Fee);
        Assert.Equal(896.25m, await GetBalanceAsync(fromId));
        await AssertNoDriftAsync(fromId, 1000m);
    }

    // Borrar el exchange tras haber puesto fee=0 no debe inflar el balance.
    [Fact]
    public async Task Bug2_DeleteExchangeAfterFeeZero_NoOverRefund()
    {
        var fromId = await CreateWalletAsync("USD Del", 1000m);
        var toId = await CreateWalletAsync("VES Del", 0m, "VES");
        var exId = await CreateExchangeAsync(fromId, toId, 100m, 93000m, fee: 3.75m);
        await UpdateExchangeAsync(exId, new UpdateExchangeCommand { Fee = 0m, Tz = "America/Caracas" });
        Assert.Equal(900m, await GetBalanceAsync(fromId));

        using (var scope = _serviceProvider.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<ExchangesService>();
            await svc.Remove(exId);
        }

        Assert.Equal(1000m, await GetBalanceAsync(fromId));
        Assert.Equal(0m, await GetBalanceAsync(toId));
        await AssertNoDriftAsync(fromId, 1000m);
        await AssertNoDriftAsync(toId, 0m);
    }
}