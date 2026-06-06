namespace E2EHarness;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;

/// <summary>
/// The eight release-gate scenarios that must pass before publishing a new preview
/// of the Trellis.Microservices NuGet packages. Each scenario maps to a specific
/// P4 security amendment (see <c>.github/copilot-instructions.md</c> "P4 invariants
/// — never regress" table) and validates end-to-end behavior, not just type shapes.
///
/// <para>The scenarios divide into two categories:</para>
/// <list type="bullet">
/// <item><description>
/// <b>Gateway-to-downstream flow</b> (Scenarios 1-3 — HappyPath, NoActor, CrossAudience):
/// a real YARP gateway TestServer routes a real HTTP request through a real
/// destination TestServer via the in-process forwarder. Verifies the actual YARP
/// pipeline + transform + downstream JwtBearer + TrellisInternalJwtActorProvider
/// chain works end-to-end.
/// </description></item>
/// <item><description>
/// <b>Downstream-only attack tokens</b> (Scenarios 4-8 — Sentinel*, *CountMismatch,
/// StrictClaimShape*, ExpectedIssuer*): the test hand-crafts a malformed JWT signed
/// with the trusted key and sends it as the <c>Authorization: Bearer</c> header on
/// a GET request to the destination's <c>/probe</c> endpoint, asserting the
/// downstream fails closed (HTTP 401). These bypass the gateway because the attack
/// is in the shape of the token, not in the gateway's behavior.
/// </description></item>
/// </list>
/// </summary>
public sealed class ReleaseGateScenarios
{
    private static readonly Actor TestActor = new(
        id: "user-42",
        permissions: new HashSet<string>(StringComparer.Ordinal) { "incidents:read", "incidents:write" },
        forbiddenPermissions: new HashSet<string>(StringComparer.Ordinal) { "incidents:delete" },
        attributes: new Dictionary<string, string>(StringComparer.Ordinal) { ["tid"] = "tenant-7" });

    // === Scenario 1: happy path ===

    /// <summary>
    /// A configured gateway mints a contract-conformant JWT; the downstream JwtBearer
    /// validates it; the TrellisInternalJwtActorProvider hydrates a full Actor with
    /// the same id + permissions + forbidden + attributes the gateway started with.
    /// This is the baseline — if this scenario fails, every other scenario is moot.
    /// </summary>
    [Fact]
    public async Task Scenario1_HappyPath_GatewayMintsActorRoundtripsToDownstream()
    {
        var (signingKey, credentials) = HarnessFixtures.CreateSigningKey();
        using var destination = await HarnessFixtures.StartDestinationAsync(
            trustedSigningKey: signingKey,
            configureActor: o =>
            {
                o.AttributeClaimMap["tid"] = "tid";
                o.RequiredAttributes = ["tid"];
            });
        using var gateway = await GatewayHarness.StartAsync(
            destination: destination.GetTestServer(),
            actor: TestActor,
            credentials: credentials);

        using var client = gateway.GetTestServer().CreateClient();
        using var response = await client.GetAsync("/probe", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var body = await response.Content.ReadFromJsonAsync<ProbeResponse>(TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Id.Should().Be("user-42");
        body.Permissions.Should().BeEquivalentTo(["incidents:read", "incidents:write"]);
        body.ForbiddenPermissions.Should().BeEquivalentTo(["incidents:delete"]);
        body.Attributes.Should().ContainKey("tid").WhoseValue.Should().Be("tenant-7");
    }

    // === Scenario 2: no actor → upstream Authorization cleared ===

    /// <summary>
    /// When the gateway's IActorProvider returns <c>Maybe.None</c>, the YARP transform
    /// MUST clear the upstream Authorization header before forwarding. Without this,
    /// the external bearer token (audience: gateway) would reach the downstream — and
    /// any downstream not pinning audience strictly would accept it. P4 round-1
    /// security review.
    /// </summary>
    /// <remarks>
    /// The leaked token used here is SIGNED with the same key the destination trusts
    /// AND has the iss/aud/contract claims the destination accepts. So if the gateway
    /// regresses and forwards it, the destination will accept it and return 200.
    /// The only way this scenario returns 401 is if the gateway actually clears the
    /// header (forcing the destination's JwtBearer to fail closed on missing token).
    /// This isolates the "header cleared" invariant from "downstream rejected the
    /// random garbage token" — a less-isolated assertion would mask a regression.
    /// </remarks>
    [Fact]
    public async Task Scenario2_NoActor_UpstreamAuthorizationHeaderCleared()
    {
        var (signingKey, credentials) = HarnessFixtures.CreateSigningKey();
        using var destination = await HarnessFixtures.StartDestinationAsync(
            trustedSigningKey: signingKey);
        using var gateway = await GatewayHarness.StartAsync(
            destination: destination.GetTestServer(),
            actor: null,
            credentials: credentials);

        // A token the destination would happily accept: signed with the trusted key,
        // matching iss/aud, contract-conformant. If the gateway regresses and forwards
        // it, the response will be 200; if the gateway clears it (correct behavior),
        // the response will be 401 because no token reaches the destination at all.
        var leakedButCryptoValidToken = HarnessFixtures.MintToken(
            credentials,
            permissions: ["incidents:read"]);

        using var client = gateway.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {leakedButCryptoValidToken}");
        using var response = await client.GetAsync("/probe", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "no-actor path MUST clear the upstream Authorization header — the leaked token used here would AUTHENTICATE if forwarded (it's signed with the trusted key + matches iss/aud/contract claims), so a 200 response would prove the gateway regressed and is leaking the upstream Authorization");
    }

    // === Scenario 3: cross-audience mismatch ===

    /// <summary>
    /// Defense-in-depth against a misconfigured gateway minting tokens with the wrong
    /// audience for a destination cluster. The downstream JwtBearer's
    /// <c>ValidAudience</c> rejects any token whose <c>aud</c> doesn't match.
    /// </summary>
    /// <remarks>
    /// The fixture's default <see cref="HarnessFixtures.StartDestinationAsync"/> sets
    /// <c>TrellisInternalJwtActorOptions.ExpectedAudience</c> (defense-in-depth on the
    /// actor-provider side) to the same value as JwtBearer's <c>ValidAudience</c>. To
    /// isolate this scenario to the JwtBearer transport-layer check, we override the
    /// actor provider's ExpectedAudience to empty (the default "skip" value) — that
    /// way a 401 here can only come from JwtBearer's audience pin, not from the
    /// actor provider also rejecting. (A separate scenario could be added to isolate
    /// the actor-provider ExpectedAudience check; this scenario covers the JwtBearer
    /// side because that's the consumer-visible primary defense.)
    /// </remarks>
    [Fact]
    public async Task Scenario3_CrossAudienceMismatch_DownstreamRejects401()
    {
        var (signingKey, credentials) = HarnessFixtures.CreateSigningKey();
        // Downstream pinned to "incidents-service" audience.
        using var destination = await HarnessFixtures.StartDestinationAsync(
            trustedSigningKey: signingKey,
            expectedAudience: HarnessFixtures.DefaultAudience,
            configureActor: o => o.ExpectedAudience = ""); // isolate the JwtBearer check from actor-provider runtime check

        // Gateway mints with the WRONG audience.
        using var gateway = await GatewayHarness.StartAsync(
            destination: destination.GetTestServer(),
            actor: TestActor,
            credentials: credentials,
            audience: "different-service");

        using var client = gateway.GetTestServer().CreateClient();
        using var response = await client.GetAsync("/probe", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "downstream's strict JwtBearer ValidAudience MUST reject any token whose aud does not match — defense in depth against gateway misconfiguration (with ExpectedAudience disabled here, the JwtBearer check is the only thing that can reject; a 200 would prove JwtBearer audience validation has regressed)");
    }

    // === Scenario 4: sentinel-strip attack ===

    /// <summary>
    /// A misbehaving proxy strips the multi-valued <c>forbidden_permissions</c> claims
    /// but leaves the <c>trellis_forbidden_permissions_count</c> claim intact at its
    /// original (non-zero) value. Without the deny-overrides-allow contract integrity
    /// check, the consumer would see an empty deny set and grant requests that should
    /// be denied. The strict count-check MUST detect the mismatch and fail closed.
    /// </summary>
    [Fact]
    public async Task Scenario4_SentinelStripped_CountMismatchFailsClosed()
    {
        var (key, credentials) = HarnessFixtures.CreateSigningKey();
        using var destination = await HarnessFixtures.StartDestinationAsync(trustedSigningKey: key);

        // Token claims forbidden_permissions_count=2 but emits ZERO forbidden_permissions claims.
        // This is exactly the proxy-strip attack the sentinel + count claims defend against.
        var attackToken = HarnessFixtures.MintToken(
            credentials,
            permissions: ["incidents:read"],
            forbiddenPermissions: [], // proxy stripped these
            forbiddenPermissionsCountOverride: "2"); // but left the count

        using var response = await HarnessFixtures.ProbeAsync(destination, attackToken, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the consumer MUST detect the forbidden-permissions count mismatch (claimed 2, observed 0) and fail closed — this is the deny-overrides-allow contract integrity invariant");
    }

    // === Scenario 5: contract-version sentinel missing ===

    /// <summary>
    /// A token without the <c>trellis_actor_contract_version</c> sentinel claim MUST
    /// be rejected. Defends against a third-party gateway (or a gateway running an
    /// older contract version) silently bypassing the count-based integrity checks.
    /// </summary>
    [Fact]
    public async Task Scenario5_ContractVersionSentinelMissing_FailsClosed()
    {
        var (key, credentials) = HarnessFixtures.CreateSigningKey();
        using var destination = await HarnessFixtures.StartDestinationAsync(trustedSigningKey: key);

        var attackToken = HarnessFixtures.MintToken(
            credentials,
            permissions: ["incidents:read"],
            emitContractVersion: false);

        using var response = await HarnessFixtures.ProbeAsync(destination, attackToken, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the missing trellis_actor_contract_version sentinel MUST cause the consumer to fail closed — defends against pre-v1 or third-party gateways that bypass the contract");
    }

    // === Scenario 6: permissions count mismatch (allow side) ===

    /// <summary>
    /// Same mechanism as Scenario 4 but on the allow side: a count that exceeds the
    /// observed multi-valued permission claims MUST also fail closed. (The reverse —
    /// stripping permissions to reduce capability — is not a privilege escalation, but
    /// the contract still rejects it because the gateway is now lying about what it
    /// projected, and silent mismatch is operationally dangerous.)
    /// </summary>
    [Fact]
    public async Task Scenario6_PermissionsCountMismatch_FailsClosed()
    {
        var (key, credentials) = HarnessFixtures.CreateSigningKey();
        using var destination = await HarnessFixtures.StartDestinationAsync(trustedSigningKey: key);

        var attackToken = HarnessFixtures.MintToken(
            credentials,
            permissions: ["incidents:read", "incidents:write"], // 2 claims observed
            permissionsCountOverride: "5"); // but count claims 5

        using var response = await HarnessFixtures.ProbeAsync(destination, attackToken, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the permissions count mismatch (claimed 5, observed 2) MUST cause the consumer to fail closed — the gateway must not lie about its projected counts");
    }

    // === Scenario 7: strict claim shape — reject comma-joined ===

    /// <summary>
    /// A token where the gateway has accidentally comma-joined the permissions into a
    /// single claim value (instead of emitting multi-valued) MUST be rejected. Defends
    /// against a common gateway-side bug pattern where the gateway author tries to
    /// "serialize" a list into a single string.
    /// </summary>
    [Fact]
    public async Task Scenario7_StrictClaimShape_CommaJoinedPermissionsRejected()
    {
        var (key, credentials) = HarnessFixtures.CreateSigningKey();
        using var destination = await HarnessFixtures.StartDestinationAsync(trustedSigningKey: key);

        // permissions = ["incidents:read,incidents:write"] — one claim with comma-joined value
        var attackToken = HarnessFixtures.MintToken(
            credentials,
            permissions: ["incidents:read,incidents:write"],
            permissionsCountOverride: "1"); // technically 1 claim, but with comma-joined value

        using var response = await HarnessFixtures.ProbeAsync(destination, attackToken, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "StrictClaimShape=true (default) MUST reject any permission value containing a comma — defends against the gateway-side bug pattern of comma-joining a list into one claim");
    }

    // === Scenario 8: ExpectedIssuer defense-in-depth mismatch ===

    /// <summary>
    /// The <c>ExpectedIssuer</c> runtime check is a defense-in-depth complement to
    /// <c>JwtBearerOptions.TokenValidationParameters.ValidIssuer</c>. If a JWT passes
    /// JwtBearer's signature + issuer check but the <c>ExpectedIssuer</c> in
    /// <c>TrellisInternalJwtActorOptions</c> is different (a misconfiguration drift),
    /// the actor provider MUST still fail closed.
    /// </summary>
    [Fact]
    public async Task Scenario8_ExpectedIssuerMismatch_ActorProviderFailsClosed()
    {
        var (key, credentials) = HarnessFixtures.CreateSigningKey();
        // JwtBearer accepts issuer "https://gateway.internal", but ExpectedIssuer for
        // the actor provider is set to a DIFFERENT value (simulating drift). The JWT
        // is signed by the trusted key and has the JwtBearer-accepted issuer, so it
        // passes JwtBearer; but the actor provider's runtime check fails closed.
        using var destination = await HarnessFixtures.StartDestinationAsync(
            trustedSigningKey: key,
            expectedIssuer: HarnessFixtures.GatewayIssuer,
            configureActor: o => o.ExpectedIssuer = "https://different-internal");

        var attackToken = HarnessFixtures.MintToken(
            credentials,
            permissions: ["incidents:read"],
            issuer: HarnessFixtures.GatewayIssuer); // matches JwtBearer's ValidIssuer

        using var response = await HarnessFixtures.ProbeAsync(destination, attackToken, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "TrellisInternalJwtActorOptions.ExpectedIssuer runtime check is a defense-in-depth complement to ValidIssuer; a mismatch (drift between the two configs) MUST still fail closed");
    }

    // ====================================================================================
    // Helpers
    // ====================================================================================

    private sealed record ProbeResponse(
        string Id,
        string[] Permissions,
        string[] ForbiddenPermissions,
        Dictionary<string, string> Attributes);
}
