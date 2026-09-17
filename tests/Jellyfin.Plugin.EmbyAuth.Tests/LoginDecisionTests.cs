using Jellyfin.Plugin.EmbyAuth;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public class LoginDecisionTests
{
    private const string Bridge = "Jellyfin.Plugin.EmbyAuth.EmbyAuthenticationProvider";
    private const string DefaultProvider = "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider";

    [Fact]
    public void CreatesAccount_WhenNoJellyfinAccountExists()
    {
        var action = LoginDecision.Decide(typedAccount: null, embyUserName: "alice", embyNameAccount: null, Bridge);

        Assert.Equal(LoginAction.CreateAccount, action);
    }

    [Fact]
    public void UsesAccount_WhenAccountSignsInThroughEmby()
    {
        var alice = new JellyfinAccount("alice", Bridge);

        var action = LoginDecision.Decide(alice, "alice", alice, Bridge);

        Assert.Equal(LoginAction.UseAccount, action);
    }

    [Fact]
    public void UsesAccount_WhenNamesDifferOnlyInCase()
    {
        var alice = new JellyfinAccount("Alice", Bridge);

        var action = LoginDecision.Decide(alice, "alice", alice, Bridge);

        Assert.Equal(LoginAction.UseAccount, action);
    }

    [Fact]
    public void UsesAccount_WhenTypedNameIsUnknownButEmbyNameSignsInThroughEmby()
    {
        var alice = new JellyfinAccount("alice", Bridge);

        var action = LoginDecision.Decide(typedAccount: null, "alice", alice, Bridge);

        Assert.Equal(LoginAction.UseAccount, action);
    }

    [Fact]
    public void Denies_WhenTypedAccountUsesAnotherLoginMethod()
    {
        var carol = new JellyfinAccount("carol", DefaultProvider);

        var action = LoginDecision.Decide(carol, "carol", carol, Bridge);

        Assert.Equal(LoginAction.Deny, action);
    }

    [Fact]
    public void Denies_WhenEmbyNameMatchesAccountThatUsesAnotherLoginMethod()
    {
        var admin = new JellyfinAccount("admin", DefaultProvider);

        var action = LoginDecision.Decide(typedAccount: null, "admin", admin, Bridge);

        Assert.Equal(LoginAction.Deny, action);
    }

    [Fact]
    public void Denies_WhenEmbyAuthenticatesADifferentUser()
    {
        var bob = new JellyfinAccount("bob", Bridge);
        var robert = new JellyfinAccount("robert", Bridge);

        var action = LoginDecision.Decide(bob, "robert", robert, Bridge);

        Assert.Equal(LoginAction.Deny, action);
    }

    [Fact]
    public void Denies_WhenEmbyAuthenticatesADifferentUserWithNoAccount()
    {
        var bob = new JellyfinAccount("bob", Bridge);

        var action = LoginDecision.Decide(bob, "robert", embyNameAccount: null, Bridge);

        Assert.Equal(LoginAction.Deny, action);
    }
}
