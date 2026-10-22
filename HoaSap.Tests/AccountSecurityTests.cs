using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Xunit;

namespace HoaSap.Tests;

[Collection("api")]
public sealed class AccountSecurityTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task ChangePassword_RejectsWrongCurrentPasswordAndConfirmationMismatch()
    {
        var email = $"security-invalid-{Guid.NewGuid():N}@example.test";
        const string oldPassword = "Old-password-123!";
        using var client = await AuthenticatedAsync(await RegisterAsync(email, password: oldPassword));

        var wrongCurrent = await client.PostAsync("/api/auth/change-password", Json(new
        {
            currentPassword = "Wrong-password-123!",
            newPassword = "New-password-456!",
            confirmNewPassword = "New-password-456!"
        }));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongCurrent.StatusCode);

        var mismatch = await client.PostAsync("/api/auth/change-password", Json(new
        {
            currentPassword = oldPassword,
            newPassword = "New-password-456!",
            confirmNewPassword = "Different-password-789!"
        }));
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_InvalidatesOldTokenAndOldLogin_ButNewLoginWorks()
    {
        var email = $"security-rotate-{Guid.NewGuid():N}@example.test";
        const string oldPassword = "Old-password-123!";
        const string newPassword = "New-password-456!";
        var oldToken = await RegisterAsync(email, password: oldPassword);
        using var client = await AuthenticatedAsync(oldToken);

        var changed = await client.PostAsync("/api/auth/change-password", Json(new
        {
            currentPassword = oldPassword,
            newPassword,
            confirmNewPassword = newPassword
        }));
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        var oldTokenResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, oldTokenResponse.StatusCode);

        var oldLogin = await Client.PostAsync("/api/auth/login", Json(new { email, password = oldPassword }));
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        var newLogin = await Client.PostAsync("/api/auth/login", Json(new { email, password = newPassword }));
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
        using var json = JsonDocument.Parse(await newLogin.Content.ReadAsStringAsync());
        var newToken = json.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(newToken));
        using var newClient = await AuthenticatedAsync(newToken!);
        Assert.Equal(HttpStatusCode.OK, (await newClient.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_CannotUseAnotherAccountsPassword()
    {
        var firstEmail = $"security-boundary-a-{Guid.NewGuid():N}@example.test";
        var secondEmail = $"security-boundary-b-{Guid.NewGuid():N}@example.test";
        const string firstPassword = "First-password-123!";
        const string secondPassword = "Second-password-456!";
        using var first = await AuthenticatedAsync(await RegisterAsync(firstEmail, password: firstPassword));
        await RegisterAsync(secondEmail, password: secondPassword);

        var response = await first.PostAsync("/api/auth/change-password", Json(new
        {
            currentPassword = secondPassword,
            newPassword = "Attacker-password-789!",
            confirmNewPassword = "Attacker-password-789!"
        }));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var firstLogin = await Client.PostAsync("/api/auth/login", Json(new { email = firstEmail, password = firstPassword }));
        Assert.Equal(HttpStatusCode.OK, firstLogin.StatusCode);
        var secondLogin = await Client.PostAsync("/api/auth/login", Json(new { email = secondEmail, password = secondPassword }));
        Assert.Equal(HttpStatusCode.OK, secondLogin.StatusCode);
    }
}
