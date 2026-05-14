using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Distributed;

namespace AlteredOwnership.Server.Auth;

// Cookie holds an opaque session key. The full AuthenticationTicket (claims +
// access_token + refresh_token saved via SaveTokens=true on OIDC) lives in
// Redis under that key. Logout clears the entry server-side, which is the main
// reason to use this over storing tokens inside an encrypted cookie.
public class RedisTicketStore(IDistributedCache cache) : ITicketStore
{
    private const string KeyPrefix = "auth:session:";

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = KeyPrefix + Guid.NewGuid().ToString("N");
        await RenewAsync(key, ticket);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var bytes = TicketSerializer.Default.Serialize(ticket);
        var options = new DistributedCacheEntryOptions();
        if (ticket.Properties.ExpiresUtc is { } expires)
            options.SetAbsoluteExpiration(expires);
        return cache.SetAsync(key, bytes, options);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var bytes = await cache.GetAsync(key);
        return bytes is null ? null : TicketSerializer.Default.Deserialize(bytes);
    }

    public Task RemoveAsync(string key) => cache.RemoveAsync(key);
}
