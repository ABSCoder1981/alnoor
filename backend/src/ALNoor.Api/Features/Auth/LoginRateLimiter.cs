using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace ALNoor.Api.Features.Auth;

/// <summary>
/// Login rate limits (PRD 17.3): per IP and per phone number, fixed one-minute windows.
/// Implemented here (not as middleware) because the phone number is in the request body.
/// </summary>
public sealed class LoginRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _perIp;
    private readonly PartitionedRateLimiter<string> _perPhone;

    public LoginRateLimiter(IOptions<LoginPolicyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var o = options.Value;
        _perIp = Create(o.LoginRequestsPerMinutePerIp);
        _perPhone = Create(o.LoginRequestsPerMinutePerPhone);
    }

    private static PartitionedRateLimiter<string> Create(int permitsPerMinute) =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));

    /// <summary>Returns false when either limit is exceeded.</summary>
    public bool TryAcquire(string ip, string phoneKey)
    {
        using var ipLease = _perIp.AttemptAcquire(ip);
        if (!ipLease.IsAcquired)
        {
            return false;
        }

        using var phoneLease = _perPhone.AttemptAcquire(phoneKey);
        return phoneLease.IsAcquired;
    }

    public void Dispose()
    {
        _perIp.Dispose();
        _perPhone.Dispose();
    }
}
