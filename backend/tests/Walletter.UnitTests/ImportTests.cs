using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Walletter.Application.Categories;
using Walletter.Application.Common;
using Walletter.Application.Exchanges;
using Walletter.Application.Imports;
using Walletter.Application.Rates;
using Walletter.Application.Transactions;
using Walletter.Domain;
using Walletter.Domain.Entities;
using Walletter.Infrastructure.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace Walletter.UnitTests;

/// <summary>
/// Carga por lote (CSV) de transacciones y exchanges:
/// - parser CSV (comillas, comas, saltos, CRLF),
/// - normalización de fecha/hora/tipo/monto (es-VE y en-US),
/// - dryRun no escribe nada,
/// - commit aplica balance correcto (cero drift contra el ledger),
/// - errores por fila no abortan el resto,
/// - cabeceras faltantes → BusinessException.
/// </summary>
public class ImportTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly ITestOutputHelper _out;

    public ImportTests(ITestOutputHelper output)
    {
        _out = output;
        _dbPath = Path.Combine(Path.GetTempPath(), $"walletter_import_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath};Cache=Shared";

        var services = new ServiceCollection();
        services.AddDbContext<WalletterDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<WalletterDbContext>());
        services.AddScoped<CategoriesService>();
        services.AddScoped<TransactionsService>();
        services.AddScoped<ExchangesService>();
        services.AddScoped<RatesService>();
        services.AddScoped<ImportService>();

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

    private async Task<decimal> GetBalanceAsync(int walletId)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();
        var w = await db.Wallets.AsNoTracking().FirstAsync(x => x.Id == walletId);
        return Money.ToNum(w.Balance);
    }

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

    private async Task<ImportResult> ImportTxAsync(string csv, bool dryRun = false)
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ImportService>();
        return await svc.ImportTransactions(new ImportTransactionsCommand { Csv = csv, Tz = "America/Caracas", DryRun = dryRun });
    }

    private async Task<ImportResult> ImportExAsync(string csv, bool dryRun = false)
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ImportService>();
        return await svc.ImportExchanges(new ImportExchangesCommand { Csv = csv, Tz = "America/Caracas", DryRun = dryRun });
    }

    // ================= PARSER =================

    [Fact]
    public void CsvReader_ParsesQuotesCommasAndNewlines()
    {
        var rows = CsvReader.Parse("a,b,c\r\n1,\"x,y\",\"line1\nline2\"\r\n2,\"he said \"\"hi\"\"\",z");
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "a", "b", "c" }, rows[0]);
        Assert.Equal("x,y", rows[1][1]);
        Assert.Equal("line1\nline2", rows[1][2]);
        Assert.Equal("he said \"hi\"", rows[2][1]);
    }

    [Theory]
    [InlineData("1.500,50", 1500.50)]
    [InlineData("1,500.50", 1500.50)]
    [InlineData("1500.50", 1500.50)]
    [InlineData("1,50", 1.50)]
    [InlineData("100", 100)]
    [InlineData("1.500", 1500)]
    [InlineData("$ 2.300,00", 2300)]
    public void ParseMoney_HandlesBothSeparators(string raw, decimal expected)
    {
        Assert.Equal(expected, ImportParsing.ParseMoney(raw));
    }

    [Fact]
    public void ParseMoney_ReturnsNullWithoutDigits()
    {
        Assert.Null(ImportParsing.ParseMoney(""));
        Assert.Null(ImportParsing.ParseMoney("abc"));
        Assert.Null(ImportParsing.ParseMoney(null));
    }

    [Theory]
    [InlineData("2026-01-15", "2026-01-15")]
    [InlineData("15/01/2026", "2026-01-15")]
    [InlineData("15-01-2026", "2026-01-15")]
    [InlineData("2026/01/15", "2026-01-15")]
    public void ParseDate_NormalizesFormats(string raw, string expected)
    {
        Assert.True(ImportParsing.TryParseDate(raw, out var got));
        Assert.Equal(expected, got);
    }

    [Fact]
    public void ParseDate_RejectsGarbage()
    {
        Assert.False(ImportParsing.TryParseDate("nope", out _));
        Assert.False(ImportParsing.TryParseDate("", out _));
    }

    [Theory]
    [InlineData("9:30", "09:30")]
    [InlineData("09:30", "09:30")]
    [InlineData("14:05:30", "14:05")]
    public void ParseTime_Normalizes(string raw, string expected)
    {
        Assert.True(ImportParsing.TryParseTime(raw, out var got));
        Assert.Equal(expected, got);
    }

    [Theory]
    [InlineData("gasto", "expense")]
    [InlineData("Gasto", "expense")]
    [InlineData("ingreso", "income")]
    [InlineData("income", "income")]
    [InlineData("expense", "expense")]
    [InlineData("crédito", "income")]
    [InlineData("débito", "expense")]
    public void ParseType_HandlesAliases(string raw, string expected)
    {
        Assert.True(ImportParsing.TryParseType(raw, out var got));
        Assert.Equal(expected, got);
    }

    // ================= DRYRUN =================

    [Fact]
    public async Task DryRun_DoesNotWriteAnything()
    {
        var walletId = await CreateWalletAsync("DR", 1000m);
        var csv = "fecha,hora,tipo,billetera,categoria,monto\n2026-01-15,09:30,gasto,DR,comida,25.50";
        var res = await ImportTxAsync(csv, dryRun: true);

        Assert.True(res.DryRun);
        Assert.Equal(1, res.Total);
        Assert.Equal(1, res.Imported);
        Assert.Equal(0, res.Failed);
        Assert.Equal(1000m, await GetBalanceAsync(walletId));
        Assert.Equal(0m, await LedgerSumAsync(walletId));
    }

    // ================= TRANSACCIONES =================

    [Fact]
    public async Task Transactions_ImportAppliesBalances_NoDrift()
    {
        var walletId = await CreateWalletAsync("Batch", 1000m);
        var csv = string.Join('\n', new[]
        {
            "fecha,hora,tipo,billetera,categoria,monto,fee,descripcion",
            "2026-01-15,09:30,gasto,Batch,comida,25.50,,Almuerzo",
            "15/01/2026,14:00,ingreso,Batch,salario,1500,,Quincena",
            "2026-01-16,08:00,gasto,Batch,transporte,10.25,1.50,Taxi",
        });

        var res = await ImportTxAsync(csv);

        Assert.Equal(3, res.Imported);
        Assert.Equal(0, res.Failed);

        // 1000 - 25.50 + 1500 - (10.25 + 1.50) = 2462.75
        var balance = await GetBalanceAsync(walletId);
        Assert.Equal(2462.75m, balance);
        Assert.Equal(balance, 1000m + await LedgerSumAsync(walletId));
        _out.WriteLine($"[import tx] balance={balance} (esperado 2462.75, cero drift)");
    }

    [Fact]
    public async Task Transactions_ResolvesWalletByAlias()
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();
            db.Wallets.Add(new Wallet
            {
                Name = "Banco Principal", Alias = "banco", Currency = "USD",
                Balance = Money.ToInt(500m), Type = "bank", IsActive = true, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var csv = "fecha,tipo,billetera,categoria,monto\n2026-01-15,gasto,banco,comida,100";
        var res = await ImportTxAsync(csv);

        Assert.Equal(1, res.Imported);
        Assert.Equal(0, res.Failed);
    }

    [Fact]
    public async Task Transactions_BadRowsDoNotAbortTheRest()
    {
        var walletId = await CreateWalletAsync("Mixed", 1000m);
        var csv = string.Join('\n', new[]
        {
            "fecha,tipo,billetera,categoria,monto",
            "2026-01-15,gasto,Mixed,comida,100",   // ok
            "2026-13-40,gasto,Mixed,comida,50",    // fecha inválida
            "2026-01-16,expense,Mixed,comida,abc", // monto inválido
            "2026-01-17,gasto,NoExiste,comida,10", // billetera inexistente
            "2026-01-18,ingreso,Mixed,salario,300",// ok
        });

        var res = await ImportTxAsync(csv);

        Assert.Equal(5, res.Total);
        Assert.Equal(2, res.Imported);
        Assert.Equal(3, res.Failed);
        Assert.All(res.Rows.Where(r => !r.Ok), r => Assert.False(string.IsNullOrWhiteSpace(r.Error)));

        // 1000 - 100 + 300 = 1200; ninguna fila mala tocó el balance.
        Assert.Equal(1200m, await GetBalanceAsync(walletId));
        Assert.Equal(1200m, 1000m + await LedgerSumAsync(walletId));
    }

    [Fact]
    public async Task Transactions_MissingRequiredHeader_Throws()
    {
        await CreateWalletAsync("H", 100m);
        var csv = "fecha,billetera,categoria,monto\n2026-01-15,H,comida,10";
        await Assert.ThrowsAsync<BusinessException>(() => ImportTxAsync(csv));
    }

    [Fact]
    public async Task Transactions_InsufficientFunds_ReportedPerRow()
    {
        var walletId = await CreateWalletAsync("Pobre", 10m);
        var csv = "fecha,tipo,billetera,categoria,monto\n2026-01-15,gasto,Pobre,comida,500";
        var res = await ImportTxAsync(csv);

        Assert.Equal(0, res.Imported);
        Assert.Equal(1, res.Failed);
        Assert.Equal(10m, await GetBalanceAsync(walletId));
    }

    // ================= EXCHANGES =================

    [Fact]
    public async Task Exchanges_ImportAppliesBalances_NoDrift()
    {
        var usd = await CreateWalletAsync("USD X", 1000m);
        var ves = await CreateWalletAsync("VES X", 0m, "VES");
        var csv = string.Join('\n', new[]
        {
            "fecha,hora,origen,destino,monto_enviado,monto_recibido,fee,credit_fee,descripcion",
            "2026-01-15,09:30,USD X,VES X,100,93000,3.75,0,Cambio",
            "2026-01-16,10:00,USD X,VES X,50,46500,,2,Otro",
        });

        var res = await ImportExAsync(csv);

        Assert.Equal(2, res.Imported);
        Assert.Equal(0, res.Failed);

        // USD: 1000 - (100+3.75) - (50+0) = 846.25
        Assert.Equal(846.25m, await GetBalanceAsync(usd));
        Assert.Equal(846.25m, 1000m + await LedgerSumAsync(usd));

        // VES: (93000-0) + (46500-2) = 139498
        Assert.Equal(139498m, await GetBalanceAsync(ves));
        Assert.Equal(139498m, 0m + await LedgerSumAsync(ves));
        _out.WriteLine($"[import ex] USD={await GetBalanceAsync(usd)} VES={await GetBalanceAsync(ves)} (cero drift)");
    }

    [Fact]
    public async Task Exchanges_SameWallet_ReportedPerRow()
    {
        var usd = await CreateWalletAsync("USD S", 1000m);
        await CreateWalletAsync("VES S", 0m, "VES");
        var csv = "fecha,origen,destino,monto_enviado,monto_recibido\n2026-01-15,USD S,USD S,100,100";
        var res = await ImportExAsync(csv);

        Assert.Equal(0, res.Imported);
        Assert.Equal(1, res.Failed);
        Assert.Equal(1000m, await GetBalanceAsync(usd));
    }

    [Fact]
    public async Task Exchanges_DryRun_DoesNotWrite()
    {
        var usd = await CreateWalletAsync("USD DR", 1000m);
        var ves = await CreateWalletAsync("VES DR", 0m, "VES");
        var csv = "fecha,origen,destino,monto_enviado,monto_recibido\n2026-01-15,USD DR,VES DR,100,93000";
        var res = await ImportExAsync(csv, dryRun: true);

        Assert.True(res.DryRun);
        Assert.Equal(1, res.Imported);
        Assert.Equal(1000m, await GetBalanceAsync(usd));
        Assert.Equal(0m, await GetBalanceAsync(ves));
    }
}
