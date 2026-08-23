using Walletter.Application.Common;
using Walletter.Domain;
using Walletter.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Walletter.Infrastructure.Persistence;

/// <summary>
/// Aplica las migraciones al arrancar (idempotente) y siembra las categorías
/// de sistema y la zona horaria por defecto, si no existen.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletterDbContext>();

        // Asegura el directorio para la base SQLite (EF no crea carpetas padre).
        EnsureSqliteDirectory(db);

        await db.Database.MigrateAsync(ct);

        // Seed: categorías de sistema
        var systemCategories = new[]
        {
            new Category { Name = "fee", Type = "expense", Color = "#e67e22", IsActive = true, CreatedAt = DateTime.UtcNow },
            new Category { Name = "exchange_out", Type = "expense", Color = "#9c27b0", IsActive = true, CreatedAt = DateTime.UtcNow },
            new Category { Name = "exchange_in", Type = "income", Color = "#673ab7", IsActive = true, CreatedAt = DateTime.UtcNow },
        };
        foreach (var cat in systemCategories)
        {
            if (!await db.Categories.AnyAsync(c => c.Name == cat.Name && c.Type == cat.Type, ct))
                db.Categories.Add(cat);
        }

        // Seed: zona horaria por defecto
        if (!await db.Settings.AnyAsync(s => s.Key == "user_timezone", ct))
            db.Settings.Add(new Setting { Key = "user_timezone", Value = TimeZoneHelper.DefaultTimeZone, UpdatedAt = DateTime.UtcNow });

        await db.SaveChangesAsync(ct);
    }

    private static void EnsureSqliteDirectory(WalletterDbContext db)
    {
        var cs = db.Database.GetConnectionString();
        if (string.IsNullOrEmpty(cs) || !cs.Contains("Data Source=")) return;
        var path = cs.Substring(cs.IndexOf("Data Source=") + "Data Source=".Length).Trim();
        // Ignora parámetros tipo `=true` / `Mode=RW` separados por punto y coma.
        var file = path.Split(';')[0].Trim();
        if (string.IsNullOrEmpty(file) || file == ":memory:") return;
        var dir = Path.GetDirectoryName(Path.GetFullPath(file));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }
}
