using System;
using System.Collections.Generic;
using System.Linq;
using ClientAvalonia.Core;
using ClientAvalonia.Services;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.Core;

public sealed class ModDiscoveryCatalogTests
{
    [Fact]
    public void BuildModNamesToProbe_ExplicitKeys_AppendedAfterRegistry()
    {
        var names = ModDiscoveryCatalog.BuildModNamesToProbe(
            explicitCandidateKeys: ["ExplicitOnly"],
            includeLegacyDxHints: true);

        names.Should().Contain("ExplicitOnly");

        foreach (string registered in ModWorkspaceRegistry.ListRegisteredModNames())
        {
            int regIndex = names.ToList().FindIndex(n => n.Equals(registered, StringComparison.OrdinalIgnoreCase));
            int explicitIndex = names.ToList().FindIndex(n => n.Equals("ExplicitOnly", StringComparison.OrdinalIgnoreCase));
            if (regIndex >= 0 && explicitIndex >= 0)
                regIndex.Should().BeLessThan(explicitIndex);
        }
    }

    [Fact]
    public void BuildModNamesToProbe_WithoutExplicitKeys_UsesDxHintsOnlyWhenEnabled()
    {
        var withHints = ModDiscoveryCatalog.BuildModNamesToProbe(includeLegacyDxHints: true);
        var withoutHints = ModDiscoveryCatalog.BuildModNamesToProbe(includeLegacyDxHints: false);
        var registered = new HashSet<string>(ModWorkspaceRegistry.ListRegisteredModNames(), StringComparer.OrdinalIgnoreCase);

        foreach (string hint in ModWorkspaceRegistry.KnownDxHintModNames)
        {
            withHints.Should().Contain(hint);
            if (!registered.Contains(hint))
                withoutHints.Should().NotContain(hint, "unregistered DX hints must not appear when includeLegacyDxHints=false");
        }
    }

    [Fact]
    public void BuildModNamesToProbe_DeduplicatesCaseInsensitively()
    {
        var names = ModDiscoveryCatalog.BuildModNamesToProbe(
            explicitCandidateKeys: ["lnod", "LNOD", "Lnod"],
            includeLegacyDxHints: false);

        names.Count(n => n.Equals("lnod", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
    }
}

public sealed class GameLaunchPolicyTests
{
    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(false, true, true, true, false)]
    public void ShouldUseQres_MatchesDxWindowedOnlyPolicy(
        bool isWindows,
        bool qresExists,
        bool rendererUseQres,
        bool windowed,
        bool expected)
    {
        GameLaunchPolicy.ShouldUseQres(isWindows, qresExists, rendererUseQres, windowed)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("syringe.exe", true)]
    [InlineData("Syringe.exe", true)]
    [InlineData("gamemd.exe", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void UsesSyringeLauncher_DetectsSyringeChain(string? launcher, bool expected)
    {
        GameLaunchPolicy.UsesSyringeLauncher(launcher).Should().Be(expected);
    }
}
