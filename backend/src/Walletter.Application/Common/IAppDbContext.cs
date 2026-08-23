using Walletter.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Walletter.Application.Common;

/// <summary>
/// Contrato de acceso a datos expuesto a la capa de Aplicación.
/// La implementación concreta (EF Core + provider SQLite/Postgres/MySQL)
/// vive en Infrastructure. Esto mantiene la capa de Aplicación agnóstica
/// a la base de datos: cambiar de provider solo toca Infrastructure.
/// </summary>
public interface IAppDbContext
{
    DbSet<Wallet> Wallets { get; }
    DbSet<Category> Categories { get; }
    DbSet<Transaction> Transactions { get; }
    DbSet<Exchange> Exchanges { get; }
    DbSet<DailyRate> DailyRates { get; }
    DbSet<RecurringPayment> RecurringPayments { get; }
    DbSet<Setting> Settings { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<ApiToken> ApiTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
