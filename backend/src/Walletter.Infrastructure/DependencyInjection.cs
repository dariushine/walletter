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
