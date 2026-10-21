using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var authority = builder.Configuration["Authentication:Authority"]
    ?? throw new InvalidOperationException("Authentication:Authority is required.");
var issuer = builder.Configuration["Authentication:Issuer"] ?? authority;
var audience = builder.Configuration["Authentication:Audience"] ?? "service-b-api";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.Audience = audience;
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Authentication:RequireHttpsMetadata", false);
        options.MapInboundClaims = false;
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

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "service-b-api" }));

app.MapGet("/api/transactions", (HttpContext context) =>
{
    var customer = context.User.FindFirst("preferred_username")?.Value ?? "demo";
    return Results.Ok(new[]
    {
        new { transactionId = "T-8001", owner = customer, description = "Coffee shop", amount = -6.45m, currency = "USD", date = "2026-10-19" },
        new { transactionId = "T-8002", owner = customer, description = "Salary sample", amount = 2400.00m, currency = "USD", date = "2026-10-15" }
    });
}).RequireAuthorization(policy => policy.RequireRole("bank-b-user"));

IResult CreateTransferPreview(TransferRequest request)
{
    if (string.IsNullOrWhiteSpace(request.FromAccount) ||
        string.IsNullOrWhiteSpace(request.ToAccount) ||
        request.Amount <= 0 ||
        request.Amount > 10000)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["transfer"] = ["Provide both account IDs and an amount greater than 0 and at most 10,000."]
        });
    }

    return Results.Ok(new
    {
        previewId = Guid.NewGuid(),
        fromAccount = request.FromAccount,
        toAccount = request.ToAccount,
        amount = request.Amount,
        currency = "USD",
        status = "preview-only",
        message = "No money was moved by this sample endpoint."
    });
}

app.MapPost("/api/transfer-preview", CreateTransferPreview)
    .RequireAuthorization(policy => policy.RequireRole("bank-b-user"));

app.Run();

public sealed record TransferRequest(string FromAccount, string ToAccount, decimal Amount);
