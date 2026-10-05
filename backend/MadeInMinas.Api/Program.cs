using MadeInMinas.Api.Data;
using MadeInMinas.Api.Infrastructure;
using MadeInMinas.Api.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--create-admin").ToArray());
builder.Services.AddControllers(options => options.Filters.Add(new AuthorizeFilter()));
builder.Services.AddStaffAuthentication();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UserManagementExceptionHandler>();
builder.Services.AddExceptionHandler<CategoryExceptionHandler>();
builder.Services.AddExceptionHandler<ProductExceptionHandler>();
builder.Services.AddExceptionHandler<IngredientExceptionHandler>();
builder.Services.AddExceptionHandler<StockExceptionHandler>();
builder.Services.AddScoped<MadeInMinas.Api.Services.StockService>();
builder.Services.AddExceptionHandler<RecipeExceptionHandler>();
builder.Services.AddExceptionHandler<CustomerExceptionHandler>();
builder.Services.AddExceptionHandler<CartExceptionHandler>();
builder.Services.AddExceptionHandler<OrderExceptionHandler>();
builder.Services.AddExceptionHandler<ChatExceptionHandler>();
builder.Services.AddScoped<MadeInMinas.Api.Services.HumanChatService>();
builder.Services.AddSingleton<PublicChatAccess>();
builder.Services.AddRateLimiter(options =>
{
    foreach (var (policy, limit) in new[] { ("chat-start", 5), ("chat-send", 20), ("chat-read", 120) })
        options.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});
builder.Services.AddExceptionHandler<PaymentExceptionHandler>();
builder.Services.AddScoped<MadeInMinas.Api.Services.PaymentService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.KitchenService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.DispatchService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.OrderService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.OrderStockService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.CartService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.CustomerService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.RecipeService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.ProductCostService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.DashboardService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.SalesReportService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.PublicMenuService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.PublicCartService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.PublicCheckoutService>();
builder.Services.AddDataProtection().SetApplicationName("MadeInMinas.Api");
builder.Services.AddSingleton<PublicOrderAccess>();
builder.Services.AddScoped<MadeInMinas.Api.Services.PublicOrderTrackingService>();
builder.Services.AddRateLimiter(options => options.AddPolicy("public-tracking", context =>
    RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        })));
builder.Services.AddRateLimiter(options => options.AddPolicy("public-checkout", context =>
    RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        })));
builder.Services.AddScoped<MadeInMinas.Api.Services.IngredientService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.ProductService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.CategoryService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.UserService>();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"], timeout: TimeSpan.FromSeconds(5));

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins).WithMethods("GET", "POST", "PUT").AllowAnyHeader();
    }
}));

var app = builder.Build();
if (args.Contains("--create-admin"))
{
    await AdministratorCommand.RunAsync(app.Services);
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
});
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.Run();

public partial class Program;
