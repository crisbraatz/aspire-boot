using AspireBoot.Api.Identity.Account.AddPasskey;
using AspireBoot.Api.Identity.Account.BeginAddPasskey;
using AspireBoot.Api.Identity.Account.GetAvatar;
using AspireBoot.Api.Identity.Account.GetMe;
using AspireBoot.Api.Identity.Account.ListPasskeys;
using AspireBoot.Api.Identity.Account.RemoveAvatar;
using AspireBoot.Api.Identity.Account.RemovePasskey;
using AspireBoot.Api.Identity.Account.RenamePasskey;
using AspireBoot.Api.Identity.Account.UpdatePreferences;
using AspireBoot.Api.Identity.Account.UpdateProfile;
using AspireBoot.Api.Identity.Account.UploadAvatar;
using AspireBoot.Api.Identity.Auth.BeginRegistration;
using AspireBoot.Api.Identity.Auth.BeginSignIn;
using AspireBoot.Api.Identity.Auth.CompleteRegistration;
using AspireBoot.Api.Identity.Auth.RequestRecovery;
using AspireBoot.Api.Identity.Auth.ResendConfirmation;
using AspireBoot.Api.Identity.Auth.SignIn;
using AspireBoot.Api.Identity.Auth.SignOut;
using AspireBoot.Api.Identity.Auth.SignUp;
using AspireBoot.Api.Platform.GetAppInfo;

using Microsoft.AspNetCore.Mvc;

namespace AspireBoot.Api.Shared;

/// <summary>The single place where bounded-context route groups are created and slices are mapped.</summary>
internal static class EndpointMapping
{
    /// <summary>Largest body accepted under /api/identity. Credentials are capped at 16 KiB, so this leaves room for the JSON envelope.</summary>
    private const int MaxIdentityRequestBytes = 32 * 1024;

    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api")
            .AddEndpointFilter<AntiforgeryValidationFilter>();

        // Every /api endpoint needs a rate-limit policy: the platform and antiforgery groups carry the public default.
        var platform = api.MapGroup("/platform").WithTags("Platform").RequireRateLimiting(RateLimitPolicies.Public);
        GetAppInfoEndpoint.Map(platform);

        var antiforgery = api.MapGroup("/antiforgery").WithTags("Antiforgery").RequireRateLimiting(RateLimitPolicies.Public);
        GetAntiforgeryTokenEndpoint.Map(antiforgery);

        // Inherits AntiforgeryValidationFilter from the api group, so unsafe identity requests need the XSRF token.
        // The size limit rejects oversized bodies before JSON binding, so credentials are never parsed unbounded.
        var identity = api.MapGroup("/identity")
            .WithTags("Identity")
            .WithMetadata(new RequestSizeLimitAttribute(MaxIdentityRequestBytes));
        SignUpEndpoint.Map(identity);
        ResendConfirmationEndpoint.Map(identity);
        RequestRecoveryEndpoint.Map(identity);
        BeginRegistrationEndpoint.Map(identity);
        CompleteRegistrationEndpoint.Map(identity);
        BeginSignInEndpoint.Map(identity);
        SignInEndpoint.Map(identity);
        SignOutEndpoint.Map(identity);

        var account = identity.MapGroup("/account");
        GetMeEndpoint.Map(account);
        UpdatePreferencesEndpoint.Map(account);
        UpdateProfileEndpoint.Map(account);
        UploadAvatarEndpoint.Map(account);
        RemoveAvatarEndpoint.Map(account);
        GetAvatarEndpoint.Map(account);
        ListPasskeysEndpoint.Map(account);
        BeginAddPasskeyEndpoint.Map(account);
        AddPasskeyEndpoint.Map(account);
        RenamePasskeyEndpoint.Map(account);
        RemovePasskeyEndpoint.Map(account);

        return app;
    }
}