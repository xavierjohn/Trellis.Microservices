namespace Trellis.Microservices.AspNetCore.Tests;

using System.Collections.ObjectModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Trellis.Microservices.AspNetCore;

public sealed class AllowMissingActorAttributesAttributeTests
{
    [Fact]
    public void Constructor_NoNames_ThrowsBlanketNotSupported()
    {
        var act = () => new AllowMissingActorAttributesAttribute();

        act.Should().Throw<ArgumentException>().WithMessage("*at least one*");
    }

    [Fact]
    public void Constructor_NullArray_Throws()
    {
        var act = () => new AllowMissingActorAttributesAttribute(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyOrWhitespaceName_Throws(string blank)
    {
        var act = () => new AllowMissingActorAttributesAttribute("tenant_id", blank);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_DuplicateNames_AreDeduplicatedPreservingOrder()
    {
        var attribute = new AllowMissingActorAttributesAttribute("tenant_id", "tenant_id", "mfa");

        attribute.AttributeNames.Should().Equal("tenant_id", "mfa");
    }

    [Fact]
    public void Constructor_CaseVariantNames_AreKept_OrdinalDedup()
    {
        var attribute = new AllowMissingActorAttributesAttribute("tenant_id", "TENANT_ID");

        attribute.AttributeNames.Should().Equal("tenant_id", "TENANT_ID");
    }

    [Fact]
    public void AttributeNames_DoesNotExposeMutableBackingList()
    {
        var attribute = new AllowMissingActorAttributesAttribute("tenant_id");

        attribute.AttributeNames.Should().BeOfType<ReadOnlyCollection<string>>(
            "endpoint metadata must expose a truly immutable collection (not a mutable List or array a caller could cast and mutate)");
    }

    [Fact]
    public void EndpointExtension_AddsMetadataCarryingTheNames()
    {
        var builder = new CapturingConventionBuilder();

        builder.AllowMissingActorAttributes("tenant_id", "mfa");

        var endpointBuilder = new TestEndpointBuilder();
        foreach (var convention in builder.Conventions)
            convention(endpointBuilder);

        endpointBuilder.Metadata.OfType<AllowMissingActorAttributesAttribute>().Single()
            .AttributeNames.Should().Equal("tenant_id", "mfa");
    }

    [Fact]
    public void EndpointExtension_NullBuilder_Throws()
    {
        var act = () => ((IEndpointConventionBuilder)null!).AllowMissingActorAttributes("tenant_id");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EndpointExtension_NoNames_Throws()
    {
        var builder = new CapturingConventionBuilder();

        var act = () => builder.AllowMissingActorAttributes();

        act.Should().Throw<ArgumentException>();
    }

    private sealed class CapturingConventionBuilder : IEndpointConventionBuilder
    {
        public List<Action<EndpointBuilder>> Conventions { get; } = [];

        public void Add(Action<EndpointBuilder> convention) => Conventions.Add(convention);
    }

    private sealed class TestEndpointBuilder : EndpointBuilder
    {
        public override Endpoint Build() => throw new NotSupportedException();
    }
}
