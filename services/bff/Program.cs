using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var frontendOrigins = builder.Configuration.GetSection("Bff:FrontendOrigins").Get<Dictionary<string, string>>() ?? [];
var downstreamServices = builder.Configuration.GetSection("Bff:Services").GetChildren().ToArray();
var production = !builder.Environment.IsDevelopment();

builder.Services.AddSingleton<ITicketStore, InMemoryTicketStore>();
builder.Services.AddSingleton<DownstreamProxy>();
builder.Services.AddHttpClient("downstream", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddCors(options => options.AddPolicy("frontends", policy =>
{
    policy.WithOrigins(allowedOrigins)
        .WithHeaders("Accept", "Content-Type", "If-None-Match", "If-Modified-Since", "X-CSRF-TOKEN")
        .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
        .AllowCredentials();
}));
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = production ? "__Host-BffCsrf" : "BffCsrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = production ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.Cookie.Path = "/";
});
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.Cookie.Name = production ? "__Host-BffSession" : "BffSession";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = production ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.Cookie.Path = "/";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
})
.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    options.Authority = builder.Configuration["Authentication:Authority"];
    options.ClientId = builder.Configuration["Authentication:ClientId"];
    options.ClientSecret = builder.Configuration["Authentication:ClientSecret"];
    options.CallbackPath = builder.Configuration["Authentication:CallbackPath"] ?? "/signin-oidc";
    options.RequireHttpsMetadata = builder.Configuration.GetValue("Authentication:RequireHttpsMetadata", production);
    options.CorrelationCookie.SecurePolicy = production ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.NonceCookie.SecurePolicy = production ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.ResponseType = OpenIdConnectResponseType.Code;
    options.UsePkce = true;
    options.SaveTokens = true;
    options.GetClaimsFromUserInfoEndpoint = true;
    options.MapInboundClaims = false;
    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
    foreach (var service in downstreamServices)
    {
        var scope = service["Scope"];
        if (!string.IsNullOrWhiteSpace(scope))
        {
            options.Scope.Add(scope);
        }
    }
    options.TokenValidationParameters.NameClaimType = "name";
    options.TokenValidationParameters.RoleClaimType = "roles";
});
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<ITicketStore>((options, ticketStore) => options.SessionStore = ticketStore);
builder.Services.AddAuthorization();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    var forwardedOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1
    };
    forwardedOptions.KnownProxies.Clear();
    forwardedOptions.KnownNetworks.Clear();
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(proxy, out var address))
        {
            forwardedOptions.KnownProxies.Add(address);
        }
    }
    app.UseForwardedHeaders(forwardedOptions);
    app.UseHsts();
}

app.UseCors("frontends");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "bff" }));

IResult StartLogin(string? appId)
{
    if (appId is null || !frontendOrigins.TryGetValue(appId, out var destination))
    {
        return Results.BadRequest(new { error = "Set app to service-a or service-b." });
    }

    var properties = new AuthenticationProperties { RedirectUri = destination + "/" };
    return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
}

app.MapGet("/auth/login", StartLogin).AllowAnonymous();

app.MapGet("/auth/me", (HttpContext context) =>
{
    var user = context.User;
    return Results.Ok(new
    {
        authenticated = true,
        subject = user.FindFirst("sub")?.Value,
        name = user.FindFirst("name")?.Value ?? user.Identity?.Name,
        roles = user.FindAll("roles").Select(c => c.Value).ToArray()
    });
}).RequireAuthorization();

app.MapGet("/auth/csrf", (IAntiforgery antiforgery, HttpContext context) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { requestToken = tokens.RequestToken });
}).RequireAuthorization();

async Task<IResult> Logout(HttpContext context, IAntiforgery antiforgery)
{
    try
    {
        await antiforgery.ValidateRequestAsync(context);
    }
    catch (AntiforgeryValidationException)
    {
        return Results.BadRequest(new { error = "Missing or invalid CSRF token." });
    }

    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
}

app.MapPost("/auth/logout", Logout).RequireAuthorization();

var supportedMethods = new[] { "GET", "POST", "PUT", "PATCH", "DELETE" };
app.MapMethods("/api/{serviceKey}/{**path}", supportedMethods,
    async (string serviceKey, string? path, HttpContext context, DownstreamProxy proxy) =>
        await proxy.ForwardAsync(serviceKey, path ?? string.Empty, context))
    .RequireAuthorization();

app.Run();
