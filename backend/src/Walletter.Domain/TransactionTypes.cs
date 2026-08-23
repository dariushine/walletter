namespace Walletter.Domain;

/// <summary>
/// tipos de transacción válidos.
/// </summary>
public static class TransactionTypes
{
    public const string Income = "income";
    public const string Expense = "expense";

    public static bool IsValid(string? type) =>
        type == Income || type == Expense;
}
