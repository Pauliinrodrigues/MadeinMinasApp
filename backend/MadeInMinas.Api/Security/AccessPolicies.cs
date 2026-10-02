namespace MadeInMinas.Api.Security;

public static class AccessPolicies
{
    public const string ManageUsers = "users.manage";
    public const string ViewDashboard = "dashboard.view";
    public const string ViewReports = "reports.view";
    public const string ManageCatalog = "catalog.manage";
    public const string ManageCustomers = "customers.manage";
    public const string ManageOrders = "orders.manage";
    public const string ManagePayments = "payments.manage";
    public const string RefundPayments = "payments.refund";
    public const string WorkKitchen = "kitchen.work";
    public const string WorkDispatch = "dispatch.work";
    public const string PrintKitchen = "printing.kitchen";
    public const string PrintDispatch = "printing.dispatch";

    public static IReadOnlyDictionary<string, string[]> RolesByPermission
    {
        get;
    } =
        new Dictionary<string, string[]>
        {
            [ManageUsers] = ["Administrator"],
            [ViewDashboard] = ["Administrator"],
            [ViewReports] = ["Administrator"],
            [ManageCatalog] = ["Administrator"],
            [ManageCustomers] = ["Administrator", "Attendant"],
            [ManageOrders] = ["Administrator", "Attendant"],
            [ManagePayments] = ["Administrator", "Attendant"],
            [RefundPayments] = ["Administrator"],
            [WorkKitchen] = ["Administrator", "Kitchen"],
            [WorkDispatch] = ["Administrator", "Dispatch"],
            [PrintKitchen] = ["Administrator", "Attendant", "Kitchen"],
            [PrintDispatch] = ["Administrator", "Attendant", "Dispatch"]
        };

    public static string[] ForRole(string role) => RolesByPermission
        .Where(policy => policy.Value.Contains(role))
        .Select(policy => policy.Key)
        .ToArray();
}
