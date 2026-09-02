namespace FTMS.Api.Controllers;

/// <summary>
/// Route names, which ASP.NET Core also publishes as the OpenAPI operationId.
///
/// design: doc 05 section 9 - OpenAPI is the single client facing contract and both clients
/// generate their API layers from it. That makes these strings part of the public contract:
/// the generated Angular client's method names come straight from here, so renaming one is a
/// breaking change for client code, not a refactor.
///
/// Without them the generator invents names from the path and verb, producing
/// apiTransactionsIdPut and similar, which is unpleasant to read and unstable under any route
/// change at all.
/// </summary>
public static class RouteNames
{
    public const string ListTransactions = "listTransactions";
    public const string GetTransactionById = "getTransactionById";
    public const string CreateTransaction = "createTransaction";
    public const string UpdateTransaction = "updateTransaction";
    public const string DeleteTransaction = "deleteTransaction";
    public const string ListTransactionStatuses = "listTransactionStatuses";

    public const string Login = "login";
    public const string RefreshSession = "refreshSession";
    public const string Logout = "logout";
    public const string GetCurrentUser = "getCurrentUser";
}
