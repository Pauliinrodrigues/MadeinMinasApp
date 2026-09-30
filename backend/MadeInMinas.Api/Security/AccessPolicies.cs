namespace MadeInMinas.Api.Security;

public static class AccessPolicies
{
    public const string ManageUsers = "users.manage";
    public const string ManageCatalog = "catalog.manage";
    public const string ManageCustomers = "customers.manage";
    public const string ManageOrders = "orders.manage";
    public const string WorkKitchen = "kitchen.work";
    public const string WorkDispatch = "dispatch.work";

    public static IReadOnlyDictionary<string, string[]> RolesByPermission
    {
        get;
    } =
        new Dictionary<string, string[]>
        {
            [ManageUsers] = ["Administrator"],
            [ManageCatalog] = ["Administrator"],
            [ManageCustomers] = ["Administrator", "Attendant"],
            [ManageOrders] = ["Administrator", "Attendant"],
            [WorkKitchen] = ["Administrator", "Kitchen"],
            [WorkDispatch] = ["Administrator", "Dispatch"]
        };

    public static string[] ForRole(string role) => RolesByPermission
        .Where(policy => policy.Value.Contains(role))
        .Select(policy => policy.Key)
        .ToArray();
}
