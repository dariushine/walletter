using Walletter.Application.Common;
using Walletter.Domain;
using Walletter.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Walletter.Application.Categories;

/// <summary>
/// Lógica de categorías. `GetOrCreateCategory` es idempotente y lo reutilizan
/// las transacciones, exchanges y pagos recurrentes.
/// </summary>
public class CategoriesService
{
    private readonly IAppDbContext _db;

    public CategoriesService(IAppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Busca una categoría activa por nombre+tipo; si no existe la crea.
    /// No permite crear categorías de sistema.
    /// </summary>
    public async Task<Category> GetOrCreateCategory(string categoryName, string type, CancellationToken ct = default)
    {
        var name = (categoryName ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new BusinessException("Nombre de categoría vacío");
        if (!TransactionTypes.IsValid(type))
            throw new BusinessException("type debe ser income o expense");

        var row = await _db.Categories.FirstOrDefaultAsync(c => c.Name == name && c.Type == type, ct);
        if (row != null)
        {
            if (!row.IsActive && !SystemCategories.IsSystem(row.Name))
            {
                row.IsActive = true;
                await _db.SaveChangesAsync(ct);
            }
            return row;
        }

        if (SystemCategories.IsSystem(name))
            throw new BusinessException($"No puedes crear la categoría de sistema '{name}'");

        var color = type == TransactionTypes.Income ? "#2ecc71" : "#e74c3c";
        var created = new Category { Name = name, Type = type, Color = color, IsActive = true, CreatedAt = DateTime.UtcNow };
        _db.Categories.Add(created);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Carrera concurrente: otro request creó la categoría. Reintentar lectura.
            var existing = await _db.Categories.FirstOrDefaultAsync(c => c.Name == name && c.Type == type, ct);
            if (existing != null) return existing;
            throw;
        }
        return created;
    }

    public async Task<List<object>> List(CancellationToken ct = default)
    {
        return (await _db.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(ct))
            .Select(Map).ToList();
    }

    private static object Map(Category c) => new
    {
        id = c.Id,
        name = c.Name,
        type = c.Type,
        color = c.Color,
        icon = c.Icon,
        isActive = c.IsActive,
        createdAt = c.CreatedAt,
    };

    public async Task<object> Create(CreateCategoryCommand cmd, CancellationToken ct = default)
    {
        if (SystemCategories.IsSystem(cmd.Name))
            throw new BusinessException($"No puedes crear la categoría de sistema '{cmd.Name}'");
        return Map(await GetOrCreateCategory(cmd.Name, cmd.Type, ct));
    }

    public async Task<object> Update(int id, UpdateCategoryCommand cmd, CancellationToken ct = default)
    {
        var existing = await _db.Categories.FindAsync(new object?[] { id }, ct);
        if (existing == null) throw new NotFoundException("Categoría no encontrada");
        if (SystemCategories.IsSystem(existing.Name) && !string.IsNullOrEmpty(cmd.Name))
            throw new BusinessException("La categoría de sistema no se puede renombrar");

        if (!string.IsNullOrEmpty(cmd.Name)) existing.Name = cmd.Name;
        if (cmd.Color != null) existing.Color = cmd.Color;
        await _db.SaveChangesAsync(ct);
        return Map(existing);
    }

    public async Task<object> Remove(int id, CancellationToken ct = default)
    {
        var existing = await _db.Categories.FindAsync(new object?[] { id }, ct);
        if (existing == null) throw new NotFoundException("Categoría no encontrada");
        if (SystemCategories.IsSystem(existing.Name))
            throw new BusinessException("La categoría de sistema no se puede eliminar");
        existing.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return Map(existing);
    }

    public async Task<object> Reactivate(int id, CancellationToken ct = default)
    {
        var existing = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id && !c.IsActive, ct);
        if (existing == null) throw new NotFoundException("Categoría no encontrada o ya activa");
        existing.IsActive = true;
        await _db.SaveChangesAsync(ct);
        return Map(existing);
    }
}
