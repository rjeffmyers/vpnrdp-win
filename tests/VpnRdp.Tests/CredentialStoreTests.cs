using VpnRdp.Services;

namespace VpnRdp.Tests;

/// <summary>Round-trips through the real Windows Credential Manager using a throwaway target.</summary>
public class CredentialStoreTests
{
    [Fact]
    public void WriteReadDelete()
    {
        var target = $"VpnRdp:test-{Guid.NewGuid():N}:vpn";
        try
        {
            Assert.Null(CredentialStore.Read(target));
            CredentialStore.Write(target, "jeff", "pä$$wörd \"quoted\"");
            var c = CredentialStore.Read(target);
            Assert.NotNull(c);
            Assert.Equal("jeff", c!.Value.User);
            Assert.Equal("pä$$wörd \"quoted\"", c.Value.Secret);
        }
        finally
        {
            CredentialStore.Delete(target);
        }
        Assert.Null(CredentialStore.Read(target));
        CredentialStore.Delete(target); // deleting a missing entry is not an error
    }
}
