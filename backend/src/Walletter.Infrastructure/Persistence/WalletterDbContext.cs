using Walletter.Application.Common;
using Walletter.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Walletter.Infrastructure.Persistence;

/// <summary>
/// DbContext de EF Core para el sistema financiero. Montos como INTEGER (×100),
/// tasas como INTEGER (×10000), soft-delete y relaciones padre/hijo.
/// La base (provider) se selecciona en AddDbContext usando el driver deseado
/// (SQLite por defecto; cambiar el provider = solo tocar Program.cs / DI).
/// </summary>
public class WalletterDbContext : DbContext, IAppDbContext
{
    /// <summary>
    /// Serializa las transacciones SOLO en SQLite. SQLite no implementa
    /// IsolationLevel.Serializable real (las lecturas no se bloquean), así que
    /// dos transacciones pueden leer el mismo balance antes de escribir y
    /// pisarse (lost update). Con este lock global del proceso, las operaciones
    /// de escritura en SQLite se turnan: el siguiente lee después del commit.
    /// PostgreSQL/MySQL NO usan este lock (su Serializable es real).
    /// </summary>
    private static readonly SemaphoreSlim SqliteTransactionLock = new(1, 1);

    public WalletterDbContext(DbContextOptions<WalletterDbContext> options) : base(options) { }

    private bool IsSqlite => Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Exchange> Exchanges => Set<Exchange>();
    public DbSet<DailyRate> DailyRates => Set<DailyRate>();
    public DbSet<RecurringPayment> RecurringPayments => Set<RecurringPayment>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        if (!IsSqlite)
            return await Database.BeginTransactionAsync(ct);
        await SqliteTransactionLock.WaitAsync(ct);
        try
        {
            return new LockedTransaction(await Database.BeginTransactionAsync(ct), SqliteTransactionLock);
        }
        catch
        {
            SqliteTransactionLock.Release();
            throw;
        }
    }

    public async Task<IDbContextTransaction> BeginTransactionAsync(System.Data.IsolationLevel isolationLevel, CancellationToken ct = default)
    {
        if (!IsSqlite)
            return await Database.BeginTransactionAsync(isolationLevel, ct);
        await SqliteTransactionLock.WaitAsync(ct);
        try
        {
            return new LockedTransaction(await Database.BeginTransactionAsync(isolationLevel, ct), SqliteTransactionLock);
        }
        catch
        {
            SqliteTransactionLock.Release();
            throw;
        }
    }

    /// <summary>
    /// Envuelve la transacción subyacente y libera el lock global al terminar
    /// (commit/rollback/dispose), para que en SQLite las escrituras se serialicen.
    /// </summary>
    private sealed class LockedTransaction : IDbContextTransaction
    {
        private readonly IDbContextTransaction _inner;
        private readonly SemaphoreSlim _lock;
        private bool _released;

        public LockedTransaction(IDbContextTransaction inner, SemaphoreSlim semLock)
        {
            _inner = inner;
            _lock = semLock;
        }

        public Guid TransactionId => _inner.TransactionId;

        public async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            Release();
        }

        public void Dispose()
        {
            _inner.Dispose();
            Release();
        }

        public async Task CommitAsync(CancellationToken ct = default)
        {
            await _inner.CommitAsync(ct);
            Release();
        }

        public void Commit()
        {
            _inner.Commit();
            Release();
        }

        public async Task RollbackAsync(CancellationToken ct = default)
        {
            await _inner.RollbackAsync(ct);
            Release();
        }

        public void Rollback()
        {
            _inner.Rollback();
            Release();
        }

        private void Release()
        {
            if (_released) return;
            _released = true;
            _lock.Release();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Wallet>(e =>
        {
            e.HasKey(w => w.Id);
            e.HasIndex(w => w.Name).IsUnique();
            e.Property(w => w.Name).IsRequired().HasMaxLength(200);
            e.Property(w => w.Type).IsRequired().HasMaxLength(50);
            e.Property(w => w.Currency).IsRequired().HasMaxLength(10);
            e.Property(w => w.Balance).IsRequired();
            e.HasMany(w => w.Transactions).WithOne(t => t.Wallet).HasForeignKey(t => t.WalletId);
            e.HasMany(w => w.ExchangesFrom).WithOne(x => x.From).HasForeignKey(x => x.FromWalletId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(w => w.ExchangesTo).WithOne(x => x.To).HasForeignKey(x => x.ToWalletId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.Name).IsUnique();
            e.Property(c => c.Name).IsRequired().HasMaxLength(200);
            e.Property(c => c.Type).IsRequired().HasMaxLength(20);
            e.HasMany(c => c.Transactions).WithOne(t => t.Category).HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(c => c.Recurring).WithOne(r => r.Category).HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Transaction>(e =>
        {
            e.ToTable("transactions");
            e.HasKey(t => t.Id);
            e.Property(t => t.Type).IsRequired().HasMaxLength(20);
            e.Property(t => t.Amount).IsRequired();
            e.Property(t => t.Fee).IsRequired();
            e.Property(t => t.DatetimeUtc).IsRequired();
            e.Property(t => t.Description).HasMaxLength(500);
            e.HasOne(t => t.Wallet).WithMany(w => w.Transactions).HasForeignKey(t => t.WalletId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.Category).WithMany(c => c.Transactions).HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.Restrict);

            // Autorreferencia padre → hijos (comisiones).
            e.HasOne(t => t.Parent)
                .WithMany(t => t.Children)
                .HasForeignKey(t => t.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(t => t.DebitExchanges).WithOne(x => x.Debit).HasForeignKey(x => x.DebitTransactionId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(t => t.CreditExchanges).WithOne(x => x.Credit).HasForeignKey(x => x.CreditTransactionId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(t => new { t.WalletId });
            e.HasIndex(t => t.DatetimeUtc);
            e.HasIndex(t => t.ParentId);
        });

        modelBuilder.Entity<Exchange>(e =>
        {
            e.ToTable("exchanges");
            e.HasKey(x => x.Id);
            e.Property(x => x.FromAmount).IsRequired();
            e.Property(x => x.ToAmount).IsRequired();
            e.Property(x => x.Rate).IsRequired();
            e.Property(x => x.Fee).IsRequired();
            e.Property(x => x.CreditFee).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasOne(x => x.Debit).WithMany(t => t.DebitExchanges).HasForeignKey(x => x.DebitTransactionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Credit).WithMany(t => t.CreditExchanges).HasForeignKey(x => x.CreditTransactionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.From).WithMany(w => w.ExchangesFrom).HasForeignKey(x => x.FromWalletId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.To).WithMany(w => w.ExchangesTo).HasForeignKey(x => x.ToWalletId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DailyRate>(e =>
        {
            e.ToTable("daily_rates");
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.Date).IsUnique();
            e.Property(r => r.Date).IsRequired().HasMaxLength(10);
            e.Property(r => r.Bcv).IsRequired();
            e.Property(r => r.Paralelo).IsRequired();
            e.Property(r => r.Source).HasMaxLength(50);
        });

        modelBuilder.Entity<RecurringPayment>(e =>
        {
            e.ToTable("recurring_payments");
            e.HasKey(r => r.Id);
            e.Property(r => r.Name).IsRequired().HasMaxLength(200);
            e.Property(r => r.Amount).IsRequired();
            e.Property(r => r.Fee).IsRequired();
            e.Property(r => r.Currency).IsRequired().HasMaxLength(10);
            e.Property(r => r.Type).IsRequired().HasMaxLength(20);
            e.HasOne(r => r.Category).WithMany(c => c.Recurring).HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Setting>(e =>
        {
            e.ToTable("settings");
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).IsRequired().HasMaxLength(100);
            e.Property(s => s.Value).HasMaxLength(500);
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasKey(r => r.Jti);
            e.Property(r => r.Jti).HasMaxLength(64);
            e.Property(r => r.TokenHash).IsRequired().HasMaxLength(64);
            e.HasIndex(r => r.TokenHash).IsUnique();
        });

        modelBuilder.Entity<ApiToken>(e =>
        {
            e.ToTable("api_tokens");
            e.HasKey(t => t.Id);
            e.Property(t => t.Name).IsRequired().HasMaxLength(200);
            e.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);
            e.HasIndex(t => t.TokenHash).IsUnique();
        });
    }
}
