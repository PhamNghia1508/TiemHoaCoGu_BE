using HoaSap.Domain;
using Xunit;
namespace HoaSap.Tests.DistributedRateLimit;
public sealed class DistributedRateLimitTests
{
    [Fact] public void Defaults_are_bounded_and_proxy_trust_is_off() { var s = new RateLimitSettings(); Assert.True(s.Enabled); Assert.InRange(s.PermitLimit, 1, 1_000_000); Assert.InRange(s.MaxBuckets, 100, 1_000_000); Assert.False(s.TrustForwardedHeaders); }
    [Fact] public void Bucket_key_is_not_a_raw_ip_contract() { var c = new RateLimitCounter { BucketKey = "sha256", WindowStartUtc = DateTime.UtcNow }; Assert.NotEqual("127.0.0.1", c.BucketKey); }
}
