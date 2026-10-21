using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var authority = builder.Configuration["Authentication:Authority"]
    ?? throw new InvalidOperationException("Authentication:Authority is required.");
var issuer = builder.Configuration["Authentication:Issuer"] ?? authority;
var audience = builder.Configuration["Authentication:Audience"] ?? "service-a-api";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.Audience = audience;
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Authentication:RequireHttpsMetadata", false);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidateIssuer = true,
            ValidateAudience = true,
            RoleClaimType = "roles",
            NameClaimType = "preferred_username"
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "service-a-api" }));

app.MapGet("/api/accounts", (HttpContext context) =>
{
    var customer = context.User.FindFirst("preferred_username")?.Value ?? "demo";
    return Results.Ok(new[]
    {
        new { accountId = "A-1001", owner = customer, accountType = "Everyday", currency = "USD", availableBalance = 1842.61m },
        new { accountId = "A-1002", owner = customer, accountType = "Savings", currency = "USD", availableBalance = 9250.00m }
    });
}).RequireAuthorization(policy => policy.RequireRole("service-a-user"));

app.MapGet("/api/profile", (HttpContext context) => Results.Ok(new
{
    user = context.User.FindFirst("preferred_username")?.Value ?? "demo",
    service = "service-a",
    note = "Sample profile response from Service A."
})).RequireAuthorization(policy => policy.RequireRole("service-a-user"));

app.Run();
