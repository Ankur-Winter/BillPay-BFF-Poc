using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;

public sealed class DownstreamProxy(
    IHttpClientFactory clients,
    IConfiguration configuration,
    IAntiforgery antiforgery,
    ILogger<DownstreamProxy> logger)
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "OPTIONS"
    };

    public async Task<IResult> ForwardAsync(string serviceKey, string path, HttpContext context)
    {
        if (!SafeMethods.Contains(context.Request.Method))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest(new { error = "Missing or invalid CSRF token." });
            }
        }

        var baseUrl = configuration[$"Bff:Services:{serviceKey}:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            logger.LogWarning("Rejected unknown or invalid BFF service route {ServiceKey}", serviceKey);
            return Results.NotFound();
        }

        if (path.Split('/').Any(segment => segment is "." or ".."))
        {
            return Results.BadRequest(new { error = "Invalid downstream path." });
        }

        var relativePath = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
        var target = new Uri(baseUri, relativePath).ToString() + context.Request.QueryString.Value;
        var accessToken = await context.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Results.Unauthorized();
        }

        using var outgoing = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
        outgoing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        outgoing.Headers.TryAddWithoutValidation("X-Bff-Service", serviceKey);
        CopyRequestHeader(context.Request, outgoing, "Accept");
        CopyRequestHeader(context.Request, outgoing, "If-None-Match");
        CopyRequestHeader(context.Request, outgoing, "If-Modified-Since");

        if (context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            outgoing.Content = new StreamContent(context.Request.Body);
            if (MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var mediaType))
            {
                outgoing.Content.Headers.ContentType = mediaType;
            }
        }

        try
        {
            using var response = await clients.CreateClient("downstream").SendAsync(
                outgoing, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            CopyResponseHeader(response, context.Response, "Content-Type");
            CopyResponseHeader(response, context.Response, "Cache-Control");
            CopyResponseHeader(response, context.Response, "ETag");
            CopyResponseHeader(response, context.Response, "Last-Modified");
            CopyResponseHeader(response, context.Response, "Retry-After");
            if (response.Content.Headers.ContentLength is long length)
            {
                context.Response.ContentLength = length;
            }

            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
            return Results.Empty;
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Downstream request failed for {ServiceKey}", serviceKey);
            return Results.Problem("A downstream service is unavailable.", statusCode: (int)HttpStatusCode.BadGateway);
        }
    }

    private static void CopyRequestHeader(HttpRequest request, HttpRequestMessage outgoing, string name)
    {
        if (request.Headers.TryGetValue(name, out var values))
        {
            outgoing.Headers.TryAddWithoutValidation(name, values.ToArray());
        }
    }

    private static void CopyResponseHeader(HttpResponseMessage source, HttpResponse destination, string name)
    {
        if (source.Headers.TryGetValues(name, out var headers) ||
            source.Content.Headers.TryGetValues(name, out headers))
        {
            destination.Headers[name] = headers.ToArray();
        }
    }
}
