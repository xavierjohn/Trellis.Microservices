namespace Trellis.Microservices.AspNetCore;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

/// <summary>
/// Startup validator that fails closed when a later <c>PostConfigure&lt;TrellisInternalJwtActorOptions&gt;</c>
/// repoints the actor provider's scheme, <see cref="TrellisInternalJwtActorOptions.ExpectedIssuer"/>, or
/// <see cref="TrellisInternalJwtActorOptions.ExpectedAudience"/> away from the values
/// <see cref="ServiceCollectionExtensions.AddTrellisInternalJwtBearer"/> pinned. The helper forces these in a
/// <c>PostConfigure</c>, but a later <c>PostConfigure</c> could still drift them apart from the bearer handler —
/// pointing the actor provider at a different (possibly weaker) scheme, or relaxing the runtime issuer/audience
/// cross-checks. This re-asserts the pin at host start. Only the default (unnamed) options instance the provider
/// consumes is checked; this is distinct from <see cref="TrellisInternalJwtActorOptionsValidator"/>, which
/// validates the general claim-mapping contract.
/// </summary>
internal sealed class TrellisInternalJwtBearerActorOptionsValidator
    : IValidateOptions<TrellisInternalJwtActorOptions>
{
    private readonly string _scheme;
    private readonly string _issuer;
    private readonly string _audience;

    public TrellisInternalJwtBearerActorOptionsValidator(string scheme, string issuer, string audience)
    {
        _scheme = scheme;
        _issuer = issuer;
        _audience = audience;
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TrellisInternalJwtActorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // AddTrellisInternalJwtBearer pins the default options instance; named instances are not consumed by
        // the provider here and are not pinned.
        if (!string.IsNullOrEmpty(name))
            return ValidateOptionsResult.Skip;

        var failures = new List<string>();

        if (!string.Equals(options.AuthenticationScheme, _scheme, StringComparison.Ordinal))
            failures.Add($"AuthenticationScheme must remain '{_scheme}' as pinned by AddTrellisInternalJwtBearer — a later PostConfigure set it to '{options.AuthenticationScheme}', which would authenticate the actor against a different scheme than the bearer handler validates.");
        if (!string.Equals(options.ExpectedIssuer, _issuer, StringComparison.Ordinal))
            failures.Add($"ExpectedIssuer must remain '{_issuer}' as pinned by AddTrellisInternalJwtBearer — a later PostConfigure set it to '{options.ExpectedIssuer}'.");
        if (!string.Equals(options.ExpectedAudience, _audience, StringComparison.Ordinal))
            failures.Add($"ExpectedAudience must remain '{_audience}' as pinned by AddTrellisInternalJwtBearer — a later PostConfigure set it to '{options.ExpectedAudience}'.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
