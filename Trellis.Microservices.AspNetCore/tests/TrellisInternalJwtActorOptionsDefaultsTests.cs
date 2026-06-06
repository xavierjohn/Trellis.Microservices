namespace Trellis.Microservices.AspNetCore.Tests;

using Trellis.Microservices.Abstractions;
using Trellis.Microservices.AspNetCore;

/// <summary>
/// Pin: <see cref="TrellisInternalJwtActorOptions"/> defaults MUST equal the corresponding
/// canonical literals in <see cref="TrellisInternalJwtClaimNames"/>. Catches the regression
/// where a refactor "inlines" the constants as raw string literals — defeating the whole
/// purpose of the shared Trellis.Microservices.Abstractions package (which exists to
/// eliminate gateway/consumer drift on the contract claim names).
/// </summary>
public sealed class TrellisInternalJwtActorOptionsDefaultsTests
{
    [Fact]
    public void ActorIdClaim_default_matches_Subject_constant() =>
        new TrellisInternalJwtActorOptions().ActorIdClaim
            .Should().Be(TrellisInternalJwtClaimNames.Subject);

    [Fact]
    public void PermissionsClaim_default_matches_Permissions_constant() =>
        new TrellisInternalJwtActorOptions().PermissionsClaim
            .Should().Be(TrellisInternalJwtClaimNames.Permissions);

    [Fact]
    public void ForbiddenPermissionsClaim_default_matches_ForbiddenPermissions_constant() =>
        new TrellisInternalJwtActorOptions().ForbiddenPermissionsClaim
            .Should().Be(TrellisInternalJwtClaimNames.ForbiddenPermissions);

    [Fact]
    public void ContractVersionClaim_default_matches_ContractVersion_constant() =>
        new TrellisInternalJwtActorOptions().ContractVersionClaim
            .Should().Be(TrellisInternalJwtClaimNames.ContractVersion);

    [Fact]
    public void PermissionsCountClaim_default_matches_PermissionsCount_constant() =>
        new TrellisInternalJwtActorOptions().PermissionsCountClaim
            .Should().Be(TrellisInternalJwtClaimNames.PermissionsCount);

    [Fact]
    public void ForbiddenPermissionsCountClaim_default_matches_ForbiddenPermissionsCount_constant() =>
        new TrellisInternalJwtActorOptions().ForbiddenPermissionsCountClaim
            .Should().Be(TrellisInternalJwtClaimNames.ForbiddenPermissionsCount);

    [Fact]
    public void ExpectedContractVersion_default_matches_CurrentContractVersion_constant() =>
        new TrellisInternalJwtActorOptions().ExpectedContractVersion
            .Should().Be(TrellisInternalJwtClaimNames.CurrentContractVersion);
}
