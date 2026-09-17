using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public class LoginDecisionTests
{
    private const string Bridge = "Jellyfin.Plugin.EmbyAuth.EmbyAuthenticationProvider";
    private const string DefaultProvider = "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider";

    [Fact]
    public void CreatesAccount_WhenNoJellyfinAccountExists()
    {
        var action = LoginDecision.Decide("alice", typedAccount: null, embyUserName: "alice", Bridge);

        Assert.Equal(LoginAction.CreateAccount, action);
    }

    [Fact]
    public void UsesAccount_WhenAccountUsesEmbyLoginMethod()
    {
        var alice = new JellyfinAccount("alice", Bridge, IsAdministrator: false);

        var action = LoginDecision.Decide("alice", alice, "alice", Bridge);

        Assert.Equal(LoginAction.UseAccount, action);
    }

    [Fact]
    public void UsesAccount_WhenNamesDifferOnlyInCase()
    {
        var alice = new JellyfinAccount("Alice", Bridge, IsAdministrator: false);

        var action = LoginDecision.Decide("ALICE", alice, "alice", Bridge);

        Assert.Equal(LoginAction.UseAccount, action);
    }

    [Theory]
    [InlineData("alice ")]
    [InlineData(" alice")]
    [InlineData("alice@example.com")]
    public void Denies_WhenTypedNameIsNotExactlyTheEmbyName(string typedName)
    {
        var action = LoginDecision.Decide(typedName, typedAccount: null, "alice", Bridge);

        Assert.Equal(LoginAction.Deny, action);
    }

    [Fact]
    public void Denies_WhenEmbyAuthenticatesADifferentUser()
    {
        var bob = new JellyfinAccount("bob", Bridge, IsAdministrator: false);

        var action = LoginDecision.Decide("bob", bob, "robert", Bridge);

        Assert.Equal(LoginAction.Deny, action);
    }

    [Fact]
    public void Denies_WhenAccountUsesAnotherLoginMethod()
    {
        var carol = new JellyfinAccount("carol", DefaultProvider, IsAdministrator: false);

        var action = LoginDecision.Decide("carol", carol, "carol", Bridge);

        Assert.Equal(LoginAction.Deny, action);
    }

    [Fact]
    public void Denies_WhenAccountIsAnAdministrator()
    {
        var jack = new JellyfinAccount("jack", Bridge, IsAdministrator: true);

        var action = LoginDecision.Decide("jack", jack, "jack", Bridge);

        Assert.Equal(LoginAction.Deny, action);
    }
}
