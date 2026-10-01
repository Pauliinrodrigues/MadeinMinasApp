using MadeInMinas.Api.Data;
using MadeInMinas.Api.Infrastructure;
using MadeInMinas.Api.Security;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--create-admin").ToArray());
builder.Services.AddControllers(options => options.Filters.Add(new AuthorizeFilter()));
builder.Services.AddStaffAuthentication();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UserManagementExceptionHandler>();
builder.Services.AddExceptionHandler<CategoryExceptionHandler>();
builder.Services.AddExceptionHandler<ProductExceptionHandler>();
builder.Services.AddExceptionHandler<IngredientExceptionHandler>();
builder.Services.AddExceptionHandler<RecipeExceptionHandler>();
builder.Services.AddExceptionHandler<CustomerExceptionHandler>();
builder.Services.AddExceptionHandler<CartExceptionHandler>();
builder.Services.AddExceptionHandler<OrderExceptionHandler>();
builder.Services.AddScoped<MadeInMinas.Api.Services.OrderService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.CartService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.CustomerService>();
builder.Services.AddScoped<MadeInMinas.Api.Services.RecipeService>();
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
