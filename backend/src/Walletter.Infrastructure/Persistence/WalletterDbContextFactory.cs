using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Walletter.Infrastructure.Persistence;

/// <summary>
/// Fábrica en tiempo de diseño para `dotnet ef migrations add` / `database update`.
/// Usa la URL de la base de datos desde la variable de entorno (igual que runtime).
/// </summary>
public class WalletterDbContextFactory : IDesignTimeDbContextFactory<WalletterDbContext>
{
    public WalletterDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__WalletterDb")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? "Data Source=data/walletter.db";

        var options = new DbContextOptionsBuilder<WalletterDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new WalletterDbContext(options);
    }
}
