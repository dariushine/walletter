using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Walletter.Application.Categories;
using Walletter.Application.Common;
using Walletter.Domain.Entities;
using Walletter.Infrastructure.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace Walletter.UnitTests;

/// <summary>
/// BUG 4 (bajo): POST /categories ignoraba `color` y creaba con el default.
/// Causa: CategoriesService.Create llamaba a GetOrCreateCategory(name,type) sin
/// propagar el color, y esa función forzaba siempre el default por tipo.
/// </summary>
public class CategoryColorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly ITestOutputHelper _out;

    public CategoryColorTests(ITestOutputHelper output)
    {
        _out = output;
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"walletter_cat_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath};Cache=Shared";

        var services = new ServiceCollection();
        services.AddDbContext<WalletterDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<WalletterDbContext>());
        services.AddScoped<CategoriesService>();

        _serviceProvider = services.BuildServiceProvider();
        using var scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<WalletterDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        SqliteConnection.ClearAllPools();
        try { if (System.IO.File.Exists(_dbPath)) System.IO.File.Delete(_dbPath); } catch { }
    }

    private async Task<Category> CreateAsync(string name, string type, string? color)
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<CategoriesService>();
        await svc.Create(new CreateCategoryCommand { Name = name, Type = type, Color = color });
        var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();
        return await db.Categories.AsNoTracking().FirstAsync(c => c.Name == name && c.Type == type);
    }

    [Fact]
    public async Task ExplicitColor_IsPersisted()
    {
        var cat = await CreateAsync("ajuste", "expense", "#8b8b8b");
        _out.WriteLine($"[BUG4] color explícito persistido: {cat.Color}");
        Assert.Equal("#8b8b8b", cat.Color);
    }

    [Fact]
    public async Task NoColor_FallsBackToExpenseDefault()
    {
        var cat = await CreateAsync("sinColorGasto", "expense", null);
        _out.WriteLine($"[BUG4] default expense: {cat.Color}");
        Assert.Equal("#e74c3c", cat.Color);
    }

    [Fact]
    public async Task NoColor_FallsBackToIncomeDefault()
    {
        var cat = await CreateAsync("sinColorIngreso", "income", null);
        _out.WriteLine($"[BUG4] default income: {cat.Color}");
        Assert.Equal("#2ecc71", cat.Color);
    }

    [Fact]
    public async Task EmptyColor_UsesDefault_NotEmptyString()
    {
        var cat = await CreateAsync("colorVacio", "expense", "   ");
        _out.WriteLine($"[BUG4] color vacío -> {cat.Color}");
        Assert.Equal("#e74c3c", cat.Color);
    }
}
