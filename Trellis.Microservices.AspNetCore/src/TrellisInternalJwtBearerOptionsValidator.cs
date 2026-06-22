namespace Trellis.Microservices.AspNetCore;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Startup validator that fails closed when the strict internal-JWT bearer profile produced by
/// <see cref="ServiceCollectionExtensions.AddTrellisInternalJwtBearer"/> has been weakened — by a later
/// <c>PostConfigure&lt;JwtBearerOptions&gt;</c>, a replaced <c>TokenHandlers</c> list, or any setting the
/// post-configure forcing cannot reach. Only the scheme the helper registered is checked; other schemes
/// are skipped.
/// </summary>
internal sealed class TrellisInternalJwtBearerOptionsValidator : IValidateOptions<JwtBearerOptions>
{
    private static readonly string[] RequiredAlgorithms = ["RS256"];

    /// <summary>
    /// Strict upper bound on <c>ClockSkew</c>. A wider skew widens the token expiry/replay window and weakens
    /// lifetime validation, so the helper forces anything looser down to this and the validator rejects more.
    /// </summary>
    internal static readonly TimeSpan MaxClockSkew = TimeSpan.FromSeconds(30);

    private readonly string _scheme;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly IssuerValidator _pinnedIssuerValidator;
    private readonly TokenHandler _pinnedTokenHandler;

    public TrellisInternalJwtBearerOptionsValidator(string scheme, string issuer, string audience, IssuerValidator pinnedIssuerValidator, TokenHandler pinnedTokenHandler)
    {
        _scheme = scheme;
        _issuer = issuer;
        _audience = audience;
        _pinnedIssuerValidator = pinnedIssuerValidator;
        _pinnedTokenHandler = pinnedTokenHandler;
    }

    public ValidateOptionsResult Validate(string? name, JwtBearerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.Equals(name, _scheme, StringComparison.Ordinal))
            return ValidateOptionsResult.Skip;

        var failures = new List<string>();

        if (options.MapInboundClaims)
            failures.Add("MapInboundClaims must be false — the actor provider reads raw JWT claim names.");
        if (options.ForwardAuthenticate is not null || options.ForwardDefault is not null || options.ForwardDefaultSelector is not null)
            failures.Add("Authentication forwarding must not be configured on the internal-JWT scheme — it would bypass token validation.");
        if (options.TokenHandlers.Count != 1 || !ReferenceEquals(options.TokenHandlers[0], _pinnedTokenHandler))
            failures.Add("TokenHandlers must be exactly the pinned handler — a replaced, custom, or additional handler could ignore the TokenValidationParameters and accept any token.");
#pragma warning disable CS0618 // legacy validator path is obsolete; assert it stays off
        if (options.UseSecurityTokenValidators)
            failures.Add("UseSecurityTokenValidators must be false — token validation must use the modern TokenHandlers path.");
#pragma warning restore CS0618

        var v = options.TokenValidationParameters;
        if (!v.ValidateIssuer || !string.Equals(v.ValidIssuer, _issuer, StringComparison.Ordinal) || v.ValidIssuers is not null)
            failures.Add($"Issuer validation must be pinned to '{_issuer}' with no additional ValidIssuers.");
        if (!ReferenceEquals(v.IssuerValidator, _pinnedIssuerValidator))
            failures.Add("The issuer validator must be the pinned exact-match validator — the discovered metadata issuer must not widen the pin.");
        if (!v.ValidateAudience || !v.RequireAudience || !string.Equals(v.ValidAudience, _audience, StringComparison.Ordinal) || v.ValidAudiences is not null)
            failures.Add($"Audience validation must be pinned to '{_audience}' with RequireAudience and no additional ValidAudiences.");
        if (v.IgnoreTrailingSlashWhenValidatingAudience)
            failures.Add("IgnoreTrailingSlashWhenValidatingAudience must be false — the audience pin must be exact.");
        if (!v.ValidateLifetime)
            failures.Add("ValidateLifetime must be true.");
        if (!v.RequireExpirationTime)
            failures.Add("RequireExpirationTime must be true — a token without exp must be rejected.");
        if (v.ClockSkew > MaxClockSkew)
            failures.Add($"ClockSkew must be at most {MaxClockSkew.TotalSeconds:0}s — a wider skew widens the token expiry/replay window and weakens lifetime validation.");
        if (!v.RequireSignedTokens)
            failures.Add("RequireSignedTokens must be true.");
        if (!v.ValidateIssuerSigningKey)
            failures.Add("ValidateIssuerSigningKey must be true.");
        if (v.TryAllIssuerSigningKeys)
            failures.Add("TryAllIssuerSigningKeys must be false — it breaks kid-pinned rotation isolation.");
        if (v.ValidAlgorithms is null || !v.ValidAlgorithms.SequenceEqual(RequiredAlgorithms, StringComparer.Ordinal))
            failures.Add("ValidAlgorithms must be exactly [\"RS256\"].");
        if (HasBypassDelegate(v))
            failures.Add("No custom validator or signing-key resolver delegate may be set — it could bypass the forced checks.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool HasBypassDelegate(TokenValidationParameters v) =>
        v.IssuerValidatorUsingConfiguration is not null
        || v.AudienceValidator is not null
        || v.LifetimeValidator is not null
        || v.AlgorithmValidator is not null
        || v.IssuerSigningKeyValidator is not null
        || v.IssuerSigningKeyValidatorUsingConfiguration is not null
        || v.IssuerSigningKeyResolver is not null
        || v.IssuerSigningKeyResolverUsingConfiguration is not null
        || v.SignatureValidator is not null
        || v.SignatureValidatorUsingConfiguration is not null
        || v.TokenReader is not null;
}
