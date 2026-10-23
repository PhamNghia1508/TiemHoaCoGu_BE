using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HoaSap.API.Security;
using HoaSap.DAL;
using HoaSap.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HoaSap.Tests;

[Collection("api")]
public sealed class CmsAccountTests(ApiFixture fixture) : ApiTestBase(fixture)
{
    private readonly ApiFixture _fixture = fixture;

    [Fact]
    public async Task AdminCanCreateEditAndListAccounts_WithSafeDtosOnly()
    {
        var admin = await AuthenticatedAsync(await AdminTokenAsync());
        var email = UniqueEmail("cms-create");
        var phone = UniquePhone();
        var created = await admin.PostAsync("/api/management/accounts", Json(new
        {
            fullName = "CMS Created Customer",
            email,
            phoneNumber = phone,
            password = "CMS-created-password-123!",
            role = "Customer"
        }));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await created.Content.ReadAsStringAsync();
        AssertSafeAccountBody(createdBody);
        using var createdJson = JsonDocument.Parse(createdBody);
        var id = createdJson.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(email, createdJson.RootElement.GetProperty("email").GetString());
        Assert.Equal("Customer", createdJson.RootElement.GetProperty("role").GetString());

        var edited = await admin.PutAsync($"/api/management/accounts/{id}", Json(new
        {
            fullName = "CMS Edited Customer",
            email,
            phoneNumber = phone,
            role = "Staff",
            isActive = true
        }));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        AssertSafeAccountBody(await edited.Content.ReadAsStringAsync());

        var listed = await admin.GetAsync($"/api/management/accounts?search={Uri.EscapeDataString("CMS Edited Customer")}&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var listBody = await listed.Content.ReadAsStringAsync();
        AssertSafeAccountBody(listBody);
        using var listJson = JsonDocument.Parse(listBody);
        var item = Assert.Single(listJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(id, item.GetProperty("id").GetGuid());
        Assert.Equal("Staff", item.GetProperty("role").GetString());
        Assert.False(item.GetProperty("isGuest").GetBoolean());
    }

    [Fact]
    public async Task CustomerAndStaffCannotAccessCmsAccounts()
    {
        var customer = await AuthenticatedAsync(await RegisterAsync(UniqueEmail("cms-customer"), UniquePhone()));
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/management/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/management/permissions")).StatusCode);

        var admin = await AuthenticatedAsync(await AdminTokenAsync());
        var staffEmail = UniqueEmail("cms-staff");
        const string staffPassword = "CMS-staff-password-123!";
        var staffAccount = await CreateAccountAsync(admin, "CMS Staff", staffEmail, UniquePhone(), staffPassword, "Staff");
        Assert.NotEqual(Guid.Empty, staffAccount);
        var staffLogin = await Client.PostAsync("/api/auth/login", Json(new { email = staffEmail, password = staffPassword }));
        staffLogin.EnsureSuccessStatusCode();
        using var loginJson = JsonDocument.Parse(await staffLogin.Content.ReadAsStringAsync());
        var staff = await AuthenticatedAsync(loginJson.RootElement.GetProperty("accessToken").GetString()!);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/management/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/management/permissions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/management/accounts")).StatusCode);
    }

    [Fact]
    public async Task LockingAccountBlocksLoginAndInvalidatesExistingToken()
    {
        var admin = await AuthenticatedAsync(await AdminTokenAsync());
        var email = UniqueEmail("cms-lock");
        const string password = "CMS-lock-password-123!";
        var id = await CreateAccountAsync(admin, "Lockable Customer", email, UniquePhone(), password, "Customer");
        var token = await LoginTokenAsync(email, password);
        var account = await AuthenticatedAsync(token);

        var locked = await admin.PutAsync($"/api/management/accounts/{id}", Json(new
        {
            fullName = "Lockable Customer",
            email,
            phoneNumber = UniquePhone(),
            role = "Customer",
            isActive = false
        }));
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await account.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsync("/api/auth/login", Json(new { email, password }))).StatusCode);
    }

    [Fact]
    public async Task RoleChangeInvalidatesOldToken()
    {
        var admin = await AuthenticatedAsync(await AdminTokenAsync());
        var email = UniqueEmail("cms-role");
        const string password = "CMS-role-password-123!";
        var phone = UniquePhone();
        var id = await CreateAccountAsync(admin, "Role Customer", email, phone, password, "Customer");
        var oldToken = await LoginTokenAsync(email, password);

        var changed = await admin.PutAsync($"/api/management/accounts/{id}", Json(new
        {
            fullName = "Role Customer",
            email,
            phoneNumber = phone,
            role = "Staff",
            isActive = true
        }));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var oldClient = await AuthenticatedAsync(oldToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldClient.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ResetPasswordInvalidatesOldTokenAndNewLoginWorks()
    {
        var admin = await AuthenticatedAsync(await AdminTokenAsync());
        var email = UniqueEmail("cms-reset");
        const string oldPassword = "CMS-old-password-123!";
        const string newPassword = "CMS-new-password-456!";
        var id = await CreateAccountAsync(admin, "Reset Customer", email, UniquePhone(), oldPassword, "Customer");
        var oldToken = await LoginTokenAsync(email, oldPassword);

        var reset = await admin.PostAsync($"/api/management/accounts/{id}/reset-password", Json(new { newPassword }));
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        var oldClient = await AuthenticatedAsync(oldToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldClient.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsync("/api/auth/login", Json(new { email, password = oldPassword }))).StatusCode);

        var newToken = await LoginTokenAsync(email, newPassword);
        var newClient = await AuthenticatedAsync(newToken);
        Assert.Equal(HttpStatusCode.OK, (await newClient.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task AdminCannotLockOrDemoteItself()
    {
        var sharedAdmin = await AuthenticatedAsync(await AdminTokenAsync());
        var ownEmail = UniqueEmail("cms-own-admin");
        const string password = "CMS-own-admin-password-123!";
        var ownPhone = UniquePhone();
        var id = await CreateAccountAsync(sharedAdmin, "Own CMS Admin", ownEmail, ownPhone, password, "Admin");
        var ownAdmin = await AuthenticatedAsync(await LoginTokenAsync(ownEmail, password));

        var demote = await ownAdmin.PutAsync($"/api/management/accounts/{id}", Json(new
        {
            fullName = "Own CMS Admin", email = ownEmail, phoneNumber = ownPhone, role = "Staff", isActive = true
        }));
        Assert.Equal(HttpStatusCode.BadRequest, demote.StatusCode);
        var lockResponse = await ownAdmin.PutAsync($"/api/management/accounts/{id}", Json(new
        {
            fullName = "Own CMS Admin", email = ownEmail, phoneNumber = ownPhone, role = "Admin", isActive = false
        }));
        Assert.Equal(HttpStatusCode.BadRequest, lockResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ownAdmin.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task GuestCannotBeManagedAndIsExcludedFromList()
    {
        var email = UniqueEmail("cms-guest");
        var phone = UniquePhone();
        Guid guestId;
        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoaSapDbContext>();
            var guest = new Customer
            {
                FullName = "Service Seed Guest", Email = email, PhoneNumber = phone,
                PasswordHash = PasswordService.Hash("guest-password-123!"), Role = "Customer", IsGuest = true
            };
            db.Customers.Add(guest);
            await db.SaveChangesAsync();
            guestId = guest.Id;
        }

        var admin = await AuthenticatedAsync(await AdminTokenAsync());
        var list = await admin.GetAsync($"/api/management/accounts?search={Uri.EscapeDataString(email)}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Empty(listJson.RootElement.GetProperty("items").EnumerateArray());
        var edit = await admin.PutAsync($"/api/management/accounts/{guestId}", Json(new
        {
            fullName = "Guest", email, phoneNumber = phone, role = "Customer", isActive = true
        }));
        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/management/accounts/{guestId}/reset-password", Json(new { newPassword = "Guest-new-password-123!" }))).StatusCode);
    }

    [Fact]
    public async Task AccountEndpointsRejectBadPhonePagingOverflowAndDuplicateIdentity()
    {
        var admin = await AuthenticatedAsync(await AdminTokenAsync());
        var badPhone = await admin.PostAsync("/api/management/accounts", Json(new
        {
            fullName = "Bad Phone", email = UniqueEmail("cms-bad-phone"), phoneNumber = "not-a-phone",
            password = "CMS-bad-phone-password-123!", role = "Customer"
        }));
        Assert.Equal(HttpStatusCode.BadRequest, badPhone.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/management/accounts?page=2147483647&pageSize=100")).StatusCode);

        var email = UniqueEmail("cms-duplicate");
        var phone = UniquePhone();
        await CreateAccountAsync(admin, "Duplicate Source", email, phone, "CMS-duplicate-password-123!", "Customer");
        var duplicateEmail = await admin.PostAsync("/api/management/accounts", Json(new
        {
            fullName = "Duplicate Email", email, phoneNumber = UniquePhone(), password = "CMS-duplicate-password-456!", role = "Customer"
        }));
        Assert.Equal(HttpStatusCode.Conflict, duplicateEmail.StatusCode);
        var duplicatePhone = await admin.PostAsync("/api/management/accounts", Json(new
        {
            fullName = "Duplicate Phone", email = UniqueEmail("cms-duplicate-phone"), phoneNumber = phone, password = "CMS-duplicate-password-789!", role = "Customer"
        }));
        Assert.Equal(HttpStatusCode.Conflict, duplicatePhone.StatusCode);
    }

    private async Task<Guid> CreateAccountAsync(HttpClient admin, string fullName, string email, string phone, string password, string role)
    {
        var response = await admin.PostAsync("/api/management/accounts", Json(new { fullName, email, phoneNumber = phone, password, role }));
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<string> LoginTokenAsync(string email, string password)
    {
        var response = await Client.PostAsync("/api/auth/login", Json(new { email, password }));
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static void AssertSafeAccountBody(string body)
    {
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password-", body, StringComparison.Ordinal);
    }

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";
    private static string UniquePhone() => "09" + Random.Shared.Next(100000000, 999999999).ToString();
}
