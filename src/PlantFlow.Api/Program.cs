using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Identity;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

var firebaseProjectId = builder.Configuration["Firebase:ProjectId"]
    ?? throw new InvalidOperationException(
        "Firebase:ProjectId is not configured. Set it with: dotnet user-secrets set \"Firebase:ProjectId\" \"<id>\"");

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<RoleSeeder>();
builder.Services.AddScoped<AuditWriter>();
builder.Services.AddScoped<FloorPriceCalculator>();
builder.Services.AddScoped<PriceFinder>();
builder.Services.AddScoped<OrderApprovalFlow>();
builder.Services.AddScoped<OrderChargeCalculator>();
builder.Services.AddScoped<OrderInvoiceCalculator>();
builder.Services.AddDbContext<PlantFlowDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHealthChecks().AddDbContextCheck<PlantFlowDbContext>("database");

// Validate Firebase ID tokens: Google signs them, we check issuer, audience and expiry.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"https://securetoken.google.com/{firebaseProjectId}";
        options.MapInboundClaims = false; // keep the token's own claim names (sub, email, ...)
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"https://securetoken.google.com/{firebaseProjectId}",
            ValidateAudience = true,
            ValidAudience = firebaseProjectId,
            ValidateLifetime = true
        };
    });

builder.Services.AddTransient<IClaimsTransformation, PlantFlowClaimsTransformation>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.AuthenticatedUser, policy => policy.RequireAuthenticatedUser());

    options.AddPolicy(Policies.PlatformAdmin, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(PlantFlowClaims.Status, nameof(UserStatus.Active))
        .RequireClaim(PlantFlowClaims.PlatformAdmin, "true"));

    // Everything else needs an Active user, unless an endpoint says otherwise.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim(PlantFlowClaims.Status, nameof(UserStatus.Active))
        .Build();
});

var app = builder.Build();

// Tests set this so a fresh container gets the schema before seeding. Off by default.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var migrationScope = app.Services.CreateScope();
    await migrationScope.ServiceProvider.GetRequiredService<PlantFlowDbContext>().Database.MigrateAsync();
}

await DbSeeder.EnsurePlatformTenantAsync(app.Services);
if (app.Environment.IsDevelopment())
{
    await DbSeeder.EnsureDemoTenantAsync(app.Services);
    await DemoDataSeeder.EnsureDemoDataAsync(app.Services);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}
else
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

// Lets integration tests start the app through WebApplicationFactory.
public partial class Program { }