using Walletter.Application.Common;
using Walletter.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Walletter.Application.Auth;

public record LoginResult(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn, bool Remember);
public record RefreshResult(string AccessToken, string TokenType);

public class AuthService
{
    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IAuthOptions _options;

    public AuthService(IAppDbContext db, ITokenService tokens, IAuthOptions options)
    {
        _db = db;
        _tokens = tokens;
        _options = options;
    }

    public bool AuthEnabled =>
        !string.IsNullOrEmpty(_options.AuthUsername) && !string.IsNullOrEmpty(_options.AuthPassword);

    public async Task<LoginResult> Login(string username, string password, bool remember, CancellationToken ct = default)
    {
        if (!AuthEnabled)
            throw new UnauthorizedAppException("Autenticación deshabilitada");

        if (username != _options.AuthUsername || password != _options.AuthPassword)
            throw new UnauthorizedAppException("Credenciales inválidas");

        var accessToken = await _tokens.SignAccessTokenAsync(username);
        var jti = _tokens.NewJti();
        var refreshSecret = _tokens.RandomSecret();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expiresMs = 2592000000L; // 30 días

        _db.RefreshTokens.Add(new RefreshToken
        {
            Jti = jti,
            TokenHash = _tokens.Sha256(refreshSecret),
            CreatedAt = now,
            ExpiresAt = now + expiresMs,
        });
        await _db.SaveChangesAsync(ct);

        return new LoginResult(accessToken, refreshSecret, "Bearer", 15 * 60, remember);
    }

    public async Task<object> Logout()
    {
        return new { success = true };
    }

    public async Task<RefreshResult> Refresh(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(refreshToken))
            throw new UnauthorizedAppException("Sin refresh token");
        var hash = _tokens.Sha256(refreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash, ct);
        if (stored == null || stored.ExpiresAt < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            throw new UnauthorizedAppException("Refresh token inválido o expirado");

        stored.LastUsedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.SaveChangesAsync(ct);

        var username = _options.AuthUsername ?? "user";
        var accessToken = await _tokens.SignAccessTokenAsync(username);
        return new RefreshResult(accessToken, "Bearer");
    }

    public async Task<bool> ValidateApiToken(string? apiToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(apiToken)) return false;
        var hash = _tokens.Sha256(apiToken);
        var row = await _db.ApiTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.IsActive, ct);
        if (row == null) return false;
        if (row.ExpiresAt is long exp && exp < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return false;
        row.LastUsedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<object>> ListSessions(CancellationToken ct = default)
    {
        var rows = await _db.RefreshTokens.OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        return rows.Select(r => (object)new
        {
            jti = r.Jti,
            createdAt = r.CreatedAt,
            lastUsedAt = r.LastUsedAt,
            deviceName = r.DeviceName,
            current = false,
        }).ToList();
    }

    public async Task<object> RevokeSession(string jti, CancellationToken ct = default)
    {
        var row = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.Jti == jti, ct);
        if (row != null)
        {
            _db.RefreshTokens.Remove(row);
            await _db.SaveChangesAsync(ct);
        }
        return new { success = true };
    }

    public async Task<List<object>> ListTokens(CancellationToken ct = default)
    {
        var rows = await _db.ApiTokens.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
        return rows.Select(t => (object)new
        {
            id = t.Id,
            name = t.Name,
            createdAt = t.CreatedAt,
            lastUsedAt = t.LastUsedAt,
            isActive = t.IsActive,
        }).ToList();
    }

    public async Task<object> CreateToken(string name, CancellationToken ct = default)
    {
        var token = _tokens.RandomSecret();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var row = new ApiToken
        {
            Name = name,
            TokenHash = _tokens.Sha256(token),
            CreatedAt = now,
            IsActive = true,
        };
        _db.ApiTokens.Add(row);
        await _db.SaveChangesAsync(ct);
        return new { id = row.Id, token };
    }

    public async Task<object> RevokeToken(int id, CancellationToken ct = default)
    {
        var row = await _db.ApiTokens.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (row != null)
        {
            row.IsActive = false;
            await _db.SaveChangesAsync(ct);
        }
        return new { success = true };
    }
}
