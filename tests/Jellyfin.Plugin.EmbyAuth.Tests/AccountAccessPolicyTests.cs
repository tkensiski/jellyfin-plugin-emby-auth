using System;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public class AccountAccessPolicyTests
{
    private static User JellyfinDefaultUser()
    {
        var user = new User("alice", "provider", "reset-provider");
        user.AddDefaultPermissions();
        user.AddDefaultPreferences();
        return user;
    }

    [Theory]
    [InlineData(AccountAccess.CopyEmbyRemoteAccess, true)]
    [InlineData(AccountAccess.CopyEmbyRemoteAccess, false)]
    [InlineData(AccountAccess.NoLibraries, true)]
    [InlineData(AccountAccess.NoLibraries, false)]
    public void NewAccount_CopiesEmbyRemoteAccess(AccountAccess access, bool embyAllowsRemoteAccess)
    {
        var user = JellyfinDefaultUser();

        AccountAccessPolicy.ApplyToNewAccount(user, access, embyAllowsRemoteAccess);

        Assert.Equal(embyAllowsRemoteAccess, user.HasPermission(PermissionKind.EnableRemoteAccess));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NewAccount_JellyfinDefaults_ChangesNothing(bool embyAllowsRemoteAccess)
    {
        var expected = JellyfinDefaultUser();
        var user = JellyfinDefaultUser();

        AccountAccessPolicy.ApplyToNewAccount(user, AccountAccess.JellyfinDefaults, embyAllowsRemoteAccess);

        Assert.Equal(expected.HasPermission(PermissionKind.EnableRemoteAccess), user.HasPermission(PermissionKind.EnableRemoteAccess));
        Assert.Equal(expected.HasPermission(PermissionKind.EnableAllFolders), user.HasPermission(PermissionKind.EnableAllFolders));
    }

    [Fact]
    public void NewAccount_NoLibraries_RemovesLibraryAccess()
    {
        var user = JellyfinDefaultUser();

        AccountAccessPolicy.ApplyToNewAccount(user, AccountAccess.NoLibraries, embyAllowsRemoteAccess: true);

        Assert.False(user.HasPermission(PermissionKind.EnableAllFolders));
        Assert.Empty(user.GetPreferenceValues<Guid>(PreferenceKind.EnabledFolders));
    }

    [Fact]
    public void NewAccount_CopyEmbyRemoteAccess_KeepsLibraryAccess()
    {
        var expected = JellyfinDefaultUser();
        var user = JellyfinDefaultUser();

        AccountAccessPolicy.ApplyToNewAccount(user, AccountAccess.CopyEmbyRemoteAccess, embyAllowsRemoteAccess: false);

        Assert.Equal(expected.HasPermission(PermissionKind.EnableAllFolders), user.HasPermission(PermissionKind.EnableAllFolders));
    }

    [Theory]
    [InlineData(AccountAccess.CopyEmbyRemoteAccess)]
    [InlineData(AccountAccess.NoLibraries)]
    public void ExistingAccount_LosesRemoteAccess_WhenEmbyDoesNotAllowIt(AccountAccess access)
    {
        var user = JellyfinDefaultUser();
        user.SetPermission(PermissionKind.EnableRemoteAccess, true);
        user.SetPermission(PermissionKind.EnableAllFolders, true);

        AccountAccessPolicy.ApplyToExistingAccount(user, access, embyAllowsRemoteAccess: false);

        Assert.False(user.HasPermission(PermissionKind.EnableRemoteAccess));
        Assert.True(user.HasPermission(PermissionKind.EnableAllFolders));
    }

    [Theory]
    [InlineData(AccountAccess.CopyEmbyRemoteAccess)]
    [InlineData(AccountAccess.NoLibraries)]
    [InlineData(AccountAccess.JellyfinDefaults)]
    public void ExistingAccount_NeverGainsRemoteAccess(AccountAccess access)
    {
        var user = JellyfinDefaultUser();
        user.SetPermission(PermissionKind.EnableRemoteAccess, false);

        AccountAccessPolicy.ApplyToExistingAccount(user, access, embyAllowsRemoteAccess: true);

        Assert.False(user.HasPermission(PermissionKind.EnableRemoteAccess));
    }

    [Fact]
    public void ExistingAccount_JellyfinDefaults_KeepsRemoteAccess()
    {
        var user = JellyfinDefaultUser();
        user.SetPermission(PermissionKind.EnableRemoteAccess, true);

        AccountAccessPolicy.ApplyToExistingAccount(user, AccountAccess.JellyfinDefaults, embyAllowsRemoteAccess: false);

        Assert.True(user.HasPermission(PermissionKind.EnableRemoteAccess));
    }
}
