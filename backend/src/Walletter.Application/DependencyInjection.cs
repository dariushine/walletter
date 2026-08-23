using Walletter.Application.Auth;
using Walletter.Application.Categories;
using Walletter.Application.Exchanges;
using Walletter.Application.Rates;
using Walletter.Application.Recurring;
using Walletter.Application.Settings;
using Walletter.Application.Stats;
using Walletter.Application.Transactions;
using Walletter.Application.Wallets;
using Microsoft.Extensions.DependencyInjection;

namespace Walletter.Application;

/// <summary>
/// Registro de los servicios de aplicación en DI.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CategoriesService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<TransactionsService>();
        services.AddScoped<WalletsService>();
        services.AddScoped<ExchangesService>();
        services.AddScoped<RatesService>();
        services.AddScoped<StatsService>();
        services.AddScoped<RecurringService>();
        services.AddScoped<AuthService>();
        return services;
    }
}
