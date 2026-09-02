using System.Security.Cryptography;
using FTMS.Application.Abstractions;
using FTMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FTMS.Infrastructure.Identity;

/// <summary>
/// Issues, rotates and revokes refresh tokens. design: doc 06 section 3.
///
/// The raw token is 256 bits from a cryptographic RNG, so it needs no salt and no key stretching
/// on the way into storage: there is nothing to brute force. It is stored as a plain SHA-256,
/// which turns a leaked database into a list of useless digests while keeping lookup a single
/// index seek.
/// </summary>
internal sealed class RefreshTokenStore(FtmsDbContext context, IOptions<JwtOptions> options)
    : IRefreshTokenStore
{
    /// <summary>256 bits. Long enough that guessing is not a threat model.</summary>
    private const int TokenBytes = 32;

    public async Task<IssuedRefreshToken> IssueAsync(
        Guid userId,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        var (raw, hash) = NewToken();
        var expiresAt = DateTime.UtcNow.AddDays(options.Value.RefreshTokenDays);

        context.RefreshTokens.Add(
            new RefreshToken(Guid.CreateVersion7(), userId, hash, expiresAt, clientIp));

        await context.SaveChangesAsync(cancellationToken);

        return new IssuedRefreshToken(raw, expiresAt);
    }

    public async Task<RefreshResult> RotateAsync(
        string rawToken,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        var hash = Hash(rawToken);

        // EnableRetryOnFailure means an explicit transaction has to go through the execution
        // strategy, or EF throws rather than risk retrying half a transaction.
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var existing = await context.RefreshTokens
                .AsNoTracking()
                .SingleOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);

            if (existing is null)
            {
                return RefreshResult.Rejected(RefreshFailure.Unknown);
            }

            if (existing.RevokedAtUtc is not null)
            {
                return RefreshResult.Rejected(RefreshFailure.Revoked);
            }

            if (existing.UsedAtUtc is not null)
            {
                // Replay. The token was already redeemed, so two parties hold it and one of them
                // is not the user. Kill every live session for the account rather than only this
                // token: we cannot tell which of the two holders is legitimate, and forcing a
                // real user to sign in again is a far smaller harm than leaving an attacker with
                // a working session. design: doc 06 section 3.
                await RevokeAllForUserAsync(existing.UserId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return RefreshResult.Rejected(RefreshFailure.Replayed);
            }

            var now = DateTime.UtcNow;
            if (existing.ExpiresAtUtc <= now)
            {
                return RefreshResult.Rejected(RefreshFailure.Expired);
            }

            var (raw, successorHash) = NewToken();
            var successorId = Guid.CreateVersion7();
            var expiresAt = now.AddDays(options.Value.RefreshTokenDays);

            // Claim the predecessor with a conditional UPDATE rather than a load-modify-save. The
            // WHERE clause is what makes rotation atomic: if two refreshes race, the database
            // picks a winner and the loser sees zero rows affected instead of both succeeding and
            // both minting a successor.
            var claimed = await context.RefreshTokens
                .Where(token => token.Id == existing.Id
                    && token.UsedAtUtc == null
                    && token.RevokedAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(token => token.UsedAtUtc, now)
                        .SetProperty(token => token.ReplacedByTokenId, successorId),
                    cancellationToken);

            if (claimed == 0)
            {
                // Lost the race. Treated as replay because that is what it is from the outside:
                // the token was redeemed twice, whoever got there first.
                await RevokeAllForUserAsync(existing.UserId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return RefreshResult.Rejected(RefreshFailure.Replayed);
            }

            context.RefreshTokens.Add(
                new RefreshToken(successorId, existing.UserId, successorHash, expiresAt, clientIp));

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return RefreshResult.Success(
                existing.UserId,
                new IssuedRefreshToken(raw, expiresAt));
        });
    }

    public async Task RevokeAsync(string rawToken, CancellationToken cancellationToken)
    {
        var hash = Hash(rawToken);

        // Idempotent by construction: a token that is unknown or already revoked matches nothing
        // and the statement affects zero rows, which is a success for a logout.
        await context.RefreshTokens
            .Where(token => token.TokenHash == hash && token.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAtUtc, DateTime.UtcNow),
                cancellationToken);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await context.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAtUtc, DateTime.UtcNow),
                cancellationToken);
    }

    private static (string Raw, string Hash) NewToken()
    {
        // Base64Url so the value survives a cookie, a URL and a log redaction filter unmangled.
        var raw = Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

        return (raw, Hash(raw));
    }

    private static string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
