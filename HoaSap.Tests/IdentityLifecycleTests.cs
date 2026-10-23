using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HoaSap.API.Security;
using HoaSap.DAL;
using HoaSap.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HoaSap.Tests;

[Collection("api")]
public sealed class IdentityLifecycleTests : ApiTestBase
{
    private readonly ApiFixture fixture;
    public IdentityLifecycleTests(ApiFixture fixture) : base(fixture) => this.fixture = fixture;
    [Fact]
    public async Task Refresh_RotatesToken_AndReplayRevokesTheWholeFamily()
    {
        var email = $"refresh-{Guid.NewGuid():N}@example.test";
        var first = await RegisterSessionAsync(email);
        var rotated = await RefreshAsync(first.RefreshToken);
        Assert.NotEqual(first.RefreshToken, rotated.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshFailureAsync(first.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshFailureAsync(rotated.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Refresh_ConcurrentClaim_AllowsOnlyOneWinner()
    {
        var session = await RegisterSessionAsync($"refresh-race-{Guid.NewGuid():N}@example.test");
        var responses = await Task.WhenAll(
            Client.PostAsync("/api/auth/refresh", Json(new { refreshToken = session.RefreshToken })),
            Client.PostAsync("/api/auth/refresh", Json(new { refreshToken = session.RefreshToken })));

        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Unauthorized));
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task Logout_RevokesRefresh_ButExistingAccessTokenRemainsValidUntilExpiry()
    {
        var session = await RegisterSessionAsync($"logout-{Guid.NewGuid():N}@example.test");
        var logout = await Client.PostAsync("/api/auth/logout", Json(new { refreshToken = session.RefreshToken }));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshFailureAsync(session.RefreshToken)).StatusCode);

        using var accessClient = fixture.CreateClient();
        accessClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await accessClient.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task PasswordChange_RevokesRefreshAndExistingAccess()
    {
        const string oldPassword = "Old-password-123!";
        const string newPassword = "New-password-456!";
        var session = await RegisterSessionAsync($"change-{Guid.NewGuid():N}@example.test", oldPassword);
        using var authenticated = fixture.CreateClient();
        authenticated.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, (await authenticated.PostAsync("/api/auth/change-password", Json(new { currentPassword = oldPassword, newPassword, confirmNewPassword = newPassword }))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await authenticated.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshFailureAsync(session.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsync("/api/auth/login", Json(new { email = session.Email, password = oldPassword }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsync("/api/auth/login", Json(new { email = session.Email, password = newPassword }))).StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_UsesSameResponseForExistingAndUnknownEmail()
    {
        var existing = $"forgot-{Guid.NewGuid():N}@example.test";
        await RegisterSessionAsync(existing);
        var known = await Client.PostAsync("/api/auth/forgot-password", Json(new { email = existing }));
        var unknown = await Client.PostAsync("/api/auth/forgot-password", Json(new { email = $"missing-{Guid.NewGuid():N}@example.test" }));
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PasswordReset_ExtractsProtectedOutboxLink_IsOneUse_AndRevokesAccess()
    {
        const string oldPassword = "Reset-old-password-123!";
        const string newPassword = "Reset-new-password-456!";
        var session = await RegisterSessionAsync($"reset-{Guid.NewGuid():N}@example.test", oldPassword);
        var protectedToken = await RequestAndExtractProtectedResetTokenAsync(session.Email);

        var reset = await Client.PostAsync("/api/auth/reset-password", Json(new { token = protectedToken, newPassword }));
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsync("/api/auth/reset-password", Json(new { token = protectedToken, newPassword }))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshFailureAsync(session.RefreshToken)).StatusCode);
        using var oldClient = fixture.CreateClient();
        oldClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldClient.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsync("/api/auth/login", Json(new { email = session.Email, password = oldPassword }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsync("/api/auth/login", Json(new { email = session.Email, password = newPassword }))).StatusCode);
    }

    [Fact]
    public async Task PasswordReset_ProtectedTokenCannotBeUsedAcrossAccounts_AndConcurrentUseIsCompareAndSwap()
    {
        const string password = "Cross-account-password-123!";
        var first = await RegisterSessionAsync($"reset-a-{Guid.NewGuid():N}@example.test", password);
        var second = await RegisterSessionAsync($"reset-b-{Guid.NewGuid():N}@example.test", password);
        var token = await RequestAndExtractProtectedResetTokenAsync(first.Email);
        var attackerAttempt = await Client.PostAsync("/api/auth/reset-password", Json(new { token, newPassword = "Attacker-new-password-456!" }));
        Assert.Equal(HttpStatusCode.NoContent, attackerAttempt.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsync("/api/auth/login", Json(new { email = first.Email, password = "Attacker-new-password-456!" }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsync("/api/auth/login", Json(new { email = second.Email, password }))).StatusCode);

        var third = await RegisterSessionAsync($"reset-race-{Guid.NewGuid():N}@example.test", password);
        var raceToken = await RequestAndExtractProtectedResetTokenAsync(third.Email);
        var results = await Task.WhenAll(
            Client.PostAsync("/api/auth/reset-password", Json(new { token = raceToken, newPassword = "Race-new-password-456!" })),
            Client.PostAsync("/api/auth/reset-password", Json(new { token = raceToken, newPassword = "Race-new-password-456!" })));
        Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.BadRequest));
        foreach (var response in results) response.Dispose();
    }

    [Fact]
    public async Task PasswordReset_ExpiredTokenIsRejected()
    {
        var email = $"reset-expired-{Guid.NewGuid():N}@example.test";
        await RegisterSessionAsync(email);
        var protectedToken = await RequestAndExtractProtectedResetTokenAsync(email);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoaSapDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("HoaSap.Auth.PasswordReset.v1");
        var raw = protector.Unprotect(protectedToken);
        var row = await db.Set<PasswordResetToken>().SingleAsync(x => x.TokenHash == TokenService.Hash(raw));
        row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsync("/api/auth/reset-password", Json(new { token = protectedToken, newPassword = "Expired-new-password-456!" }))).StatusCode);
    }

    private async Task<(string Email, string AccessToken, string RefreshToken)> RegisterSessionAsync(string email, string password = "Password-123!")
    {
        var response = await Client.PostAsync("/api/auth/register", Json(new { fullName = "Lifecycle Customer", phoneNumber = "09" + Random.Shared.Next(100000000, 999999999), email, password }));
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (email, json.RootElement.GetProperty("accessToken").GetString()!, json.RootElement.GetProperty("refreshToken").GetString()!);
    }

    private async Task<(string AccessToken, string RefreshToken)> RefreshAsync(string refreshToken)
    {
        var response = await Client.PostAsync("/api/auth/refresh", Json(new { refreshToken }));
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (json.RootElement.GetProperty("accessToken").GetString()!, json.RootElement.GetProperty("refreshToken").GetString()!);
    }

    private Task<HttpResponseMessage> RefreshFailureAsync(string refreshToken) =>
        Client.PostAsync("/api/auth/refresh", Json(new { refreshToken }));

    private async Task<string> RequestAndExtractProtectedResetTokenAsync(string email)
    {
        var response = await Client.PostAsync("/api/auth/forgot-password", Json(new { email }));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoaSapDbContext>();
        var message = await db.Set<NotificationOutbox>().Where(x => x.EventType == "PasswordReset" && x.Destination == email).OrderByDescending(x => x.CreatedAtUtc).FirstAsync();
        using var payload = JsonDocument.Parse(message.PayloadJson);
        var protectedToken = payload.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(protectedToken));
        return protectedToken!;
    }
}
