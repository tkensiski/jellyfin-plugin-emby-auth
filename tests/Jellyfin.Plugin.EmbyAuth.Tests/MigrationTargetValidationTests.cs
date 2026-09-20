using System;
using System.Collections.Generic;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using MediaBrowser.Model.Dto;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public sealed class MigrationTargetValidationTests
{
    private static readonly IReadOnlyList<NameIdPair> EnabledMethods =
    [
        new NameIdPair { Name = "Default", Id = LoginMethodMove.DefaultProviderId },
        new NameIdPair { Name = "Emby", Id = EmbyAuthenticationProvider.ProviderId },
        new NameIdPair { Name = "JellyfinSecurity", Id = "jf-security-id" },
    ];

    private static PluginConfiguration Config(string? migrationTarget = LoginMethodMove.DefaultProviderId, string? passwordSetTarget = "") => new()
    {
        EmbyServerUrl = "http://emby:8096",
        EmbyApiKey = "key-1",
        MigrationTarget = migrationTarget!,
        PasswordSetTarget = passwordSetTarget!,
    };

    [Fact]
    public void ReturnsNull_ForAMigrationTarget_WhoseIdIsInTheEnabledList()
    {
        var problem = MigrationTargetValidation.FindProblem(Config(migrationTarget: "jf-security-id"), EnabledMethods);

        Assert.Null(problem);
    }

    [Fact(Skip = "Task 1 GREEN: FindProblem does not accept the no-move sentinel yet.")]
    public void ReturnsNull_ForAMigrationTarget_EqualToTheNoMoveSentinel_EvenWhenTheEnabledListIsEmpty()
    {
        var problem = MigrationTargetValidation.FindProblem(Config(migrationTarget: PluginConfiguration.RemainOnEmbyLoginMethod), []);

        Assert.Null(problem);
    }

    [Theory(Skip = "Task 1 GREEN: FindProblem does not refuse a blank migration target yet.")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnsAProblem_ForAMigrationTarget_ThatIsNullEmptyOrWhitespace(string? blankTarget)
    {
        var problem = MigrationTargetValidation.FindProblem(Config(migrationTarget: blankTarget), EnabledMethods);

        Assert.NotNull(problem);
    }

    [Fact(Skip = "Task 1 GREEN: FindProblem does not refuse an absent target yet.")]
    public void ReturnsAProblem_ForAMigrationTarget_NotInTheEnabledList()
    {
        var problem = MigrationTargetValidation.FindProblem(Config(migrationTarget: "unknown-id"), EnabledMethods);

        Assert.NotNull(problem);
    }

    [Fact(Skip = "Task 1 GREEN: FindProblem does not refuse this plugin's own provider id yet.")]
    public void ReturnsAProblem_ForAMigrationTarget_EqualToThisPluginsOwnProviderId_EvenWhenItIsInTheEnabledList()
    {
        var problem = MigrationTargetValidation.FindProblem(Config(migrationTarget: EmbyAuthenticationProvider.ProviderId), EnabledMethods);

        Assert.NotNull(problem);
    }

    [Fact]
    public void ReturnsNull_ForAnEmptyPasswordSetTarget()
    {
        var problem = MigrationTargetValidation.FindProblem(Config(passwordSetTarget: string.Empty), EnabledMethods);

        Assert.Null(problem);
    }

    [Fact(Skip = "Task 1 GREEN: FindProblem does not check a non-empty password-set target yet.")]
    public void ReturnsAProblem_ForANonEmptyPasswordSetTarget_NotInTheEnabledList()
    {
        var problem = MigrationTargetValidation.FindProblem(Config(passwordSetTarget: "unknown-id"), EnabledMethods);

        Assert.NotNull(problem);
    }

    [Fact]
    public void ReturnsNull_ForANonEmptyPasswordSetTarget_InTheEnabledList()
    {
        var problem = MigrationTargetValidation.FindProblem(Config(passwordSetTarget: "jf-security-id"), EnabledMethods);

        Assert.Null(problem);
    }

    [Fact(Skip = "Task 1 GREEN: FindProblem does not refuse a target when the enabled list is empty yet.")]
    public void ReturnsAProblem_WhenTheEnabledListIsEmpty_AndTheTargetIsNotTheSentinel()
    {
        var problem = MigrationTargetValidation.FindProblem(Config(), []);

        Assert.NotNull(problem);
    }

    [Fact(Skip = "Task 1 GREEN: FindProblem does not refuse a missing configuration yet.")]
    public void ReturnsAProblem_ForANullConfiguration()
    {
        var problem = MigrationTargetValidation.FindProblem(null, EnabledMethods);

        Assert.NotNull(problem);
    }

    [Fact(Skip = "Task 1 GREEN: no problem messages exist yet to check for leaked values.")]
    public void NoProblemMessage_ContainsTheOffendingValueOrAnyProviderId()
    {
        var offendingValue = "unknown-id";
        var cases = new (PluginConfiguration Configuration, IReadOnlyList<NameIdPair> EnabledMethods)[]
        {
            (Config(migrationTarget: offendingValue), EnabledMethods),
            (Config(passwordSetTarget: offendingValue), EnabledMethods),
            (Config(migrationTarget: EmbyAuthenticationProvider.ProviderId), EnabledMethods),
            (Config(), []),
        };

        foreach (var (configuration, enabledMethods) in cases)
        {
            var problem = MigrationTargetValidation.FindProblem(configuration, enabledMethods);

            Assert.NotNull(problem);
            Assert.DoesNotContain(offendingValue, problem, StringComparison.Ordinal);
            Assert.DoesNotContain("AuthenticationProvider", problem, StringComparison.Ordinal);
        }
    }
}
