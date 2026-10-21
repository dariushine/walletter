using System.Text.Json;
using Walletter.Application.Categories;
using Walletter.Application.Exchanges;
using Walletter.Application.Pending;
using Walletter.Application.Recurring;
using Walletter.Application.Transactions;
using Walletter.Application.Wallets;
using Xunit;

namespace Walletter.UnitTests;

/// <summary>
/// Consistencia del BUG 1: el mismo defecto (campos desconocidos ignorados en
/// silencio → 200 OK que miente) estaba latente en el resto de los commands de
/// update. Todos llevan ahora [JsonUnmappedMemberHandling(Disallow)], así que un
/// body con un campo no soportado falla la deserialización en vez de ser no-op.
/// Opciones equivalentes a las de ASP.NET Core (web defaults, camelCase).
/// </summary>
public class UpdateCommandHardeningTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static void AssertRejectsUnknown<T>(string body) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<T>(body, WebOptions));

    // ---- UpdateWalletCommand ----
    [Fact]
    public void Wallet_UnknownField_Rejected() => AssertRejectsUnknown<UpdateWalletCommand>("""{"name":"x","fooBar":1}""");

    [Fact]
    public void Wallet_KnownFields_Parse()
    {
        var cmd = JsonSerializer.Deserialize<UpdateWalletCommand>(
            """{"name":"x","alias":"a","description":"d","icon":"i","color":"#fff","type":"bank","currency":"USD","balance":10,"excludeFromTotal":true,"hideInDashboard":false}""",
            WebOptions);
        Assert.NotNull(cmd);
        Assert.Equal("x", cmd!.Name);
        Assert.Equal(10m, cmd.Balance);
    }

    // ---- UpdateCategoryCommand ----
    [Fact]
    public void Category_UnknownField_Rejected() => AssertRejectsUnknown<UpdateCategoryCommand>("""{"name":"x","type":"expense"}""");

    [Fact]
    public void Category_KnownFields_Parse()
    {
        var cmd = JsonSerializer.Deserialize<UpdateCategoryCommand>("""{"name":"x","color":"#8b8b8b"}""", WebOptions);
        Assert.NotNull(cmd);
        Assert.Equal("#8b8b8b", cmd!.Color);
    }

    // ---- UpdateExchangeCommand ----
    [Fact]
    public void Exchange_UnknownField_Rejected() => AssertRejectsUnknown<UpdateExchangeCommand>("""{"fromAmount":100,"bogus":true}""");

    [Fact]
    public void Exchange_KnownFields_Parse()
    {
        var cmd = JsonSerializer.Deserialize<UpdateExchangeCommand>(
            """{"fromAmount":100,"toAmount":93000,"fee":0,"creditFee":2,"description":"d","date":"2026-01-01","time":"10:00","tz":"America/Caracas"}""",
            WebOptions);
        Assert.NotNull(cmd);
        Assert.Equal(0m, cmd!.Fee);
        Assert.Equal(2m, cmd.CreditFee);
    }

    // ---- UpdateRecurringCommand ----
    [Fact]
    public void Recurring_UnknownField_Rejected() => AssertRejectsUnknown<UpdateRecurringCommand>("""{"name":"x","nope":1}""");

    [Fact]
    public void Recurring_KnownFields_Parse()
    {
        var cmd = JsonSerializer.Deserialize<UpdateRecurringCommand>(
            """{"name":"x","description":"d","amount":5,"fee":1,"currency":"USD","type":"expense","categoryName":"c","walletId":2,"isSubscription":true,"billingDay":23}""",
            WebOptions);
        Assert.NotNull(cmd);
        Assert.Equal(23, cmd!.BillingDay);
        Assert.True(cmd.IsSubscription);
    }

    // ---- UpdatePendingPaymentCommand ----
    [Fact]
    public void Pending_UnknownField_Rejected() => AssertRejectsUnknown<UpdatePendingPaymentCommand>("""{"name":"x","unknown":"y"}""");

    [Fact]
    public void Pending_KnownFields_Parse()
    {
        var cmd = JsonSerializer.Deserialize<UpdatePendingPaymentCommand>(
            """{"name":"x","description":"d","amount":5,"fee":1,"currency":"USD","type":"expense","categoryName":"c","walletId":2,"dueDate":"2026-02-01"}""",
            WebOptions);
        Assert.NotNull(cmd);
        Assert.Equal("2026-02-01", cmd!.DueDate);
    }

    // ---- UpdateTransactionCommand (BUG 1, regresión) ----
    [Fact]
    public void Transaction_Fee_Rejected() => AssertRejectsUnknown<UpdateTransactionCommand>("""{"amount":10,"fee":1}""");
}
