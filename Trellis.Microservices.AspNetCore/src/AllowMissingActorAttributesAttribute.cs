namespace Trellis.Microservices.AspNetCore;

using Microsoft.AspNetCore.Builder;

/// <summary>
/// Endpoint metadata that exempts the decorated endpoint from
/// <see cref="TrellisInternalJwtActorOptions.RequiredAttributes"/> enforcement for the named actor
/// attributes — and ONLY for genuine absence. A scope-creating or pre-tenant bootstrap endpoint
/// (for example "create my first tenant") carries no <c>tenant_id</c> yet, so it cannot run under a
/// provider that requires <c>tenant_id</c> on every request.
/// <para>
/// The exemption is <b>absence-only</b>: a named attribute that is genuinely missing is allowed, but a
/// present-but-empty value, a duplicated claim, or a strict-claim-shape violation is still rejected.
/// Every other required attribute — and all other validation (authentication of the configured scheme,
/// the contract-version sentinel, the permission / forbidden count claims, the actor id) — is enforced
/// exactly as on any other endpoint.
/// </para>
/// <para>
/// There is no blanket "allow all missing attributes" mode: name each attribute explicitly so a
/// later-added required attribute (for example <c>mfa</c>) is not silently exempted on an existing
/// bootstrap endpoint.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class AllowMissingActorAttributesAttribute : Attribute
{
    /// <summary>
    /// Initializes the exemption for the given actor attribute names.
    /// </summary>
    /// <param name="attributeNames">
    /// One or more actor attribute names (matching entries in
    /// <see cref="TrellisInternalJwtActorOptions.RequiredAttributes"/>) that this endpoint may operate
    /// without. At least one name is required — a blanket exemption is not supported.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="attributeNames"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">No names are supplied, or any name is null, empty, or whitespace.</exception>
    public AllowMissingActorAttributesAttribute(params string[] attributeNames)
    {
        ArgumentNullException.ThrowIfNull(attributeNames);
        if (attributeNames.Length == 0)
            throw new ArgumentException(
                "Specify at least one actor attribute name to allow missing; a blanket exemption is not supported.",
                nameof(attributeNames));

        var names = new List<string>(attributeNames.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in attributeNames)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Actor attribute names must be non-empty.", nameof(attributeNames));
            if (seen.Add(name))
                names.Add(name);
        }

        AttributeNames = names.AsReadOnly();
    }

    /// <summary>The actor attribute names this endpoint may operate without (deduplicated, ordinal).</summary>
    public IReadOnlyList<string> AttributeNames { get; }
}

/// <summary>
/// Endpoint-builder extensions for <see cref="AllowMissingActorAttributesAttribute"/>.
/// </summary>
public static class AllowMissingActorAttributesEndpointExtensions
{
    /// <summary>
    /// Exempts the endpoint from <see cref="TrellisInternalJwtActorOptions.RequiredAttributes"/>
    /// enforcement for the named actor attributes (absence-only — see
    /// <see cref="AllowMissingActorAttributesAttribute"/>). Pairs the attribute with the minimal-API
    /// fluent style, for example <c>app.MapPost("/tenants", ...).AllowMissingActorAttributes("tenant_id")</c>.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="attributeNames">The actor attribute names this endpoint may operate without (at least one).</param>
    /// <returns>The same builder, for chaining.</returns>
    public static TBuilder AllowMissingActorAttributes<TBuilder>(this TBuilder builder, params string[] attributeNames)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        var metadata = new AllowMissingActorAttributesAttribute(attributeNames);
        builder.Add(endpoint => endpoint.Metadata.Add(metadata));
        return builder;
    }
}
