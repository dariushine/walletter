using Walletter.Application.Auth;
using Walletter.Application.Common;
using Walletter.Infrastructure.Auth;
using Walletter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Walletter.Infrastructure;

/// <summary>
/// Registro de dependencias de la capa de Infraestructura.
/// El provider de la base se selecciona aquí: SQLite por defecto.
/// Para usar PostgreSQL/MySQL solo cambia UseSqlite → UseNpgsql/UseMySql y el
/// connection string — la capa de Aplicación no cambia (DB-agnóstico).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString =
            config.GetConnectionString("WalletterDb")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? "Data Source=data/walletter.db";

        // SQLite: la concurrencia real de escritura requiere un solo cache
        // compartido + busy timeout para que las transacciones concurrentes
        // esperen el lock del archivo en vez de fallar/pisarse (lost update).
        // Solo aplica a SQLite; PostgreSQL/MySQL usan su control de concurrencia.
        var isSqlite = connectionString.Contains("Data Source", StringComparison.OrdinalIgnoreCase)
            || connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase);
        if (isSqlite && !connectionString.Contains("Cache=", StringComparison.OrdinalIgnoreCase))
        {
            var sep = connectionString.Contains(';') ? ";" : "";
            connectionString += $"{sep}Cache=Shared;Default Timeout=30";
        }

        services.AddDbContext<WalletterDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<WalletterDbContext>());
        services.AddScoped<WalletterDbContext>();

        // Auth
        services.AddScoped<IAuthOptions>(sp => new AuthOptions(config));
        services.AddScoped<ITokenService, TokenService>();

        return services;
    }
}
