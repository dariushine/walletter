using System.Text.Json;
using Walletter.Application.Transactions;
using Xunit;

namespace Walletter.UnitTests;

/// <summary>
/// BUG 1 (alto): PUT /transactions/:id respondía 200 OK con {success:true}
/// cuando el body traía `fee`, pero el campo no existía en
/// UpdateTransactionCommand y el binder lo descartaba en silencio (no-op).
/// El contrato ahora RECHAZA campos desconocidos (JsonUnmappedMemberHandling.Disallow),
/// así que un body con `fee` produce un error de deserialización en vez de mentir.
/// </summary>
public class UpdateTransactionContractTests
{
    // Opciones equivalentes a las de ASP.NET Core (web defaults, camelCase).
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Body_with_fee_is_rejected_not_silently_ignored()
    {
        const string body = """{"amount":2325,"fee":42,"date":"2026-08-28","time":"10:00"}""";
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<UpdateTransactionCommand>(body, WebOptions));
    }

    [Fact]
    public void Body_with_any_unknown_field_is_rejected()
    {
        const string body = """{"amount":100,"totallyUnknown":"x"}""";
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<UpdateTransactionCommand>(body, WebOptions));
    }

    [Fact]
    public void Body_with_only_known_fields_still_parses()
    {
        const string body = """{"description":"x","amount":100,"date":"2026-08-28","time":"10:00","categoryName":"comida","tz":"America/Caracas"}""";
        var cmd = JsonSerializer.Deserialize<UpdateTransactionCommand>(body, WebOptions);
        Assert.NotNull(cmd);
        Assert.Equal(100m, cmd!.Amount);
        Assert.Equal("comida", cmd.CategoryName);
    }

    [Fact]
    public void Empty_body_parses_to_all_null()
    {
        var cmd = JsonSerializer.Deserialize<UpdateTransactionCommand>("{}", WebOptions);
        Assert.NotNull(cmd);
        Assert.Null(cmd!.Amount);
        Assert.Null(cmd.Description);
        Assert.Null(cmd.Date);
        Assert.Null(cmd.Time);
        Assert.Null(cmd.CategoryName);
    }
}
