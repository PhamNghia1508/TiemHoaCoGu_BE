using HoaSap.API.Storage;
using Xunit;
namespace HoaSap.Tests.DurableMedia;
public sealed class DurableMediaTests
{
    [Theory]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png", true)]
    [InlineData("../secret.png", false)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.svg", false)]
    public void Managed_names_are_strict(string name, bool expected) => Assert.Equal(expected, MediaFileValidator.IsSafeName(name));
    [Fact] public void Storage_defaults_outside_wwwroot() { var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); var path = new MediaStorageOptions().Resolve(root); Assert.EndsWith(Path.Combine("App_Data", "media"), path); Assert.DoesNotContain("wwwroot", path, StringComparison.OrdinalIgnoreCase); Directory.Delete(root, true); }
}
