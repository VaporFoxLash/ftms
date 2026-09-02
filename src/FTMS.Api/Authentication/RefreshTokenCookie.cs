namespace FTMS.Api.Authentication;

/// <summary>
/// The one place that knows how the refresh token reaches and leaves the browser.
///
/// design: doc 06 section 3. Four attributes, each load bearing:
///
/// HttpOnly - script cannot read the cookie, so an XSS that can run arbitrary JavaScript in the
/// page still cannot exfiltrate a credential that renews sessions for two weeks. This is the
/// entire reason the refresh token is a cookie and the access token is not.
///
/// Secure - outside Development the cookie is never sent over plain HTTP. Development is exempt
/// because the dev server is HTTP and a Secure cookie would simply never be stored, which looks
/// exactly like a broken login.
///
/// SameSite=Strict - the CSRF control. The refresh endpoint takes no body and reads only this
/// cookie, so under Lax a cross site POST would be enough to rotate somebody's session. Strict
/// is affordable here because the SPA is served same origin with the API (the generated client's
/// rootUrl is empty), so there is no legitimate cross site request to break. That is why there
/// is no double submit CSRF token: the cookie is simply never attached to a cross site request.
///
/// Path - scoped to the auth endpoints, so the cookie is not attached to the dozens of
/// transaction requests that have no use for it.
/// </summary>
internal static class RefreshTokenCookie
{
    internal const string Name = "ftms_rt";

    private const string Path = "/api/auth";

    internal static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    internal static void Write(HttpResponse response, string rawToken, DateTime expiresAtUtc, bool isDevelopment) =>
        response.Cookies.Append(Name, rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = !isDevelopment,
            SameSite = SameSiteMode.Strict,
            Path = Path,
            Expires = new DateTimeOffset(expiresAtUtc, TimeSpan.Zero),
            IsEssential = true,
        });

    /// <summary>
    /// Clears the cookie. The attributes must match the ones it was written with, or the browser
    /// treats it as a different cookie and quietly leaves the original in place.
    /// </summary>
    internal static void Clear(HttpResponse response, bool isDevelopment) =>
        response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = !isDevelopment,
            SameSite = SameSiteMode.Strict,
            Path = Path,
        });
}
