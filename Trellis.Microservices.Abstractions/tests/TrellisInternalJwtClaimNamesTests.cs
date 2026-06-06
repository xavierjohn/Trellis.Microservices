using System.Reflection;
using Trellis.Microservices.Abstractions;

namespace Trellis.Microservices.Abstractions.Tests;

/// <summary>
/// These tests pin the EXACT public claim-name literals shipped by v1 of the
/// Trellis internal JWT contract. Any change to a value here is a contract break
/// and requires a coordinated major-version bump of this package, Trellis.Yarp,
/// and Trellis.Microservices.AspNetCore — do not "fix" a failing test here by
/// editing the expected literal.
/// </summary>
public class TrellisInternalJwtClaimNamesTests
{
    [Fact]
    public void Subject_is_exactly_sub() =>
        TrellisInternalJwtClaimNames.Subject.Should().Be("sub");

    [Fact]
    public void JwtId_is_exactly_jti() =>
        TrellisInternalJwtClaimNames.JwtId.Should().Be("jti");

    [Fact]
    public void Permissions_is_exactly_permissions() =>
        TrellisInternalJwtClaimNames.Permissions.Should().Be("permissions");

    [Fact]
    public void ForbiddenPermissions_is_exactly_forbidden_permissions() =>
        TrellisInternalJwtClaimNames.ForbiddenPermissions.Should().Be("forbidden_permissions");

    [Fact]
    public void ContractVersion_is_exactly_trellis_actor_contract_version() =>
        TrellisInternalJwtClaimNames.ContractVersion.Should().Be("trellis_actor_contract_version");

    [Fact]
    public void PermissionsCount_is_exactly_trellis_permissions_count() =>
        TrellisInternalJwtClaimNames.PermissionsCount.Should().Be("trellis_permissions_count");

    [Fact]
    public void ForbiddenPermissionsCount_is_exactly_trellis_forbidden_permissions_count() =>
        TrellisInternalJwtClaimNames.ForbiddenPermissionsCount.Should().Be("trellis_forbidden_permissions_count");

    [Fact]
    public void CurrentContractVersion_is_exactly_1() =>
        TrellisInternalJwtClaimNames.CurrentContractVersion.Should().Be("1");

    /// <summary>
    /// Snapshot guard: catches silent additions / removals to the public surface. If a
    /// new constant is added to the contract (a v2 evolution), this test fails so the
    /// reviewer is forced to acknowledge the contract surface change explicitly.
    /// </summary>
    [Fact]
    public void Public_const_surface_is_exactly_eight_members()
    {
        var members = typeof(TrellisInternalJwtClaimNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly)
            .Select(f => f.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        members.Should().Equal(
            "ContractVersion",
            "CurrentContractVersion",
            "ForbiddenPermissions",
            "ForbiddenPermissionsCount",
            "JwtId",
            "Permissions",
            "PermissionsCount",
            "Subject");
    }

    /// <summary>
    /// All values must be non-null, non-empty strings — defensive guard against a
    /// future refactor that introduces a typo collapsing one to the empty string.
    /// </summary>
    [Fact]
    public void All_constants_are_non_empty()
    {
        var values = typeof(TrellisInternalJwtClaimNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly)
            .Select(f => (string)f.GetRawConstantValue()!);

        values.Should().OnlyContain(v => !string.IsNullOrEmpty(v));
    }

    /// <summary>
    /// Reserved JWT claim names (sub, jti) and structural Trellis names
    /// (permissions, forbidden_permissions, trellis_*) must not collide with
    /// each other. Catches a copy-paste bug where two consts get the same value.
    /// </summary>
    [Fact]
    public void All_constant_values_are_unique()
    {
        var values = typeof(TrellisInternalJwtClaimNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.Name != nameof(TrellisInternalJwtClaimNames.CurrentContractVersion))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        values.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Type modifier pin: the class MUST be `public static class`. Because
    /// `Directory.Build.props` declares `InternalsVisibleTo` on the test
    /// assembly unconditionally, the const-value pinning tests above would
    /// still pass if the class accidentally got changed to `internal` — but
    /// the published NuGet package would be unusable by external consumers.
    /// This test catches that regression.
    /// </summary>
    [Fact]
    public void Type_modifiers_match_public_static_class_contract()
    {
        var type = typeof(TrellisInternalJwtClaimNames);

        type.IsPublic.Should().BeTrue("the consumer-facing contract requires the class to be public");
        type.IsAbstract.Should().BeTrue("static classes are abstract sealed in CLR metadata");
        type.IsSealed.Should().BeTrue("static classes are abstract sealed in CLR metadata");

        var publicMembers = type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var nonConstMembers = publicMembers
            .Where(m => m is not FieldInfo f || !f.IsLiteral || f.IsInitOnly)
            .Select(m => $"{m.MemberType}:{m.Name}")
            .ToArray();

        nonConstMembers.Should().BeEmpty(
            "the only declared public members are the eight const string fields — adding " +
            "any other member is a contract surface change that must be acknowledged");
    }
}
