using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;

public sealed class InMemoryTicketStore : ITicketStore
{
    private readonly ConcurrentDictionary<string, AuthenticationTicket> _tickets = new();

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        _tickets[key] = ticket;
        return Task.FromResult(key);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        _tickets[key] = ticket;
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (!_tickets.TryGetValue(key, out var ticket))
        {
            return Task.FromResult<AuthenticationTicket?>(null);
        }

        if (ticket.Properties.ExpiresUtc is DateTimeOffset expires && expires <= DateTimeOffset.UtcNow)
        {
            _tickets.TryRemove(key, out _);
            return Task.FromResult<AuthenticationTicket?>(null);
        }

        return Task.FromResult<AuthenticationTicket?>(ticket);
    }

    public Task RemoveAsync(string key)
    {
        _tickets.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
