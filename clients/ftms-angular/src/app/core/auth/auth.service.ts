import { Injectable, computed, inject, signal } from '@angular/core';
import { Api } from '@/core/api/generated/api';
import { login } from '@/core/api/generated/fn/auth/login';
import { logout } from '@/core/api/generated/fn/auth/logout';
import { refreshSession } from '@/core/api/generated/fn/auth/refresh-session';
import type { SessionResponse } from '@/core/api/generated/models/session-response';
import { TransactionListCache } from '@/core/caching/transaction-list-cache';

/**
 * Holds the access token for the session.
 *
 * design: doc 06 section 3 - the access token is held IN MEMORY ONLY, never in localStorage,
 * with the refresh token in an httpOnly Secure SameSite=Strict cookie. That is why this is a
 * signal on a root service rather than a localStorage read: a token in localStorage is readable
 * by any script that reaches the page, and this one is not.
 *
 * The cost of that choice is that a page reload loses the access token. It is paid back by
 * restore() below, which trades the cookie for a fresh token before the router runs - so a
 * reload no longer looks like a logout, which it did for as long as there was no refresh
 * endpoint to call.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(Api);
  private readonly cache = inject(TransactionListCache);

  private readonly token = signal<string | null>(null);
  private readonly user = signal<string | null>(null);
  private readonly display = signal<string | null>(null);
  private readonly userRoles = signal<readonly string[]>([]);

  readonly accessToken = this.token.asReadonly();
  readonly userName = this.user.asReadonly();
  readonly displayName = this.display.asReadonly();
  readonly roles = this.userRoles.asReadonly();
  readonly isAuthenticated = computed(() => this.token() !== null);

  /**
   * In flight refresh, if any.
   *
   * Shared rather than started per caller, because the error interceptor can see several 401s
   * land at once when a page issues parallel requests. Without this, each would start its own
   * refresh, and since refresh tokens are single use the second would present a token the first
   * had already spent - which the server correctly treats as a replay and responds to by
   * revoking the entire session. Racing ourselves would look exactly like a stolen token.
   */
  private inFlightRefresh: Promise<boolean> | null = null;

  async signIn(userName: string, password: string): Promise<void> {
    const session = await this.api.invoke(login, { body: { userName, password } });

    this.adopt(session);
  }

  /**
   * Called once at startup. Trades the httpOnly cookie for a session, or reports that there was
   * no usable cookie. Never throws: arriving with no session is the normal case for a first
   * visit, not an error worth surfacing.
   */
  async restore(): Promise<boolean> {
    return this.refresh();
  }

  /**
   * Rotates the refresh cookie and adopts the new access token. Returns false when the session
   * cannot be renewed, which is the caller's cue to send the user to the login screen.
   */
  refresh(): Promise<boolean> {
    this.inFlightRefresh ??= this.performRefresh().finally(() => {
      this.inFlightRefresh = null;
    });

    return this.inFlightRefresh;
  }

  async signOut(): Promise<void> {
    try {
      // Best effort. The server revokes the refresh token so the session cannot be renewed; if
      // the call fails we still clear local state, because a user who asked to sign out must
      // end up signed out whatever the network did.
      await this.api.invoke(logout, {});
    } catch {
      // Deliberately swallowed. See above.
    }

    this.clear();
  }

  private async performRefresh(): Promise<boolean> {
    try {
      this.adopt(await this.api.invoke(refreshSession, {}));

      return true;
    } catch {
      this.clear();

      return false;
    }
  }

  private adopt(session: SessionResponse): void {
    this.token.set(session.accessToken);
    this.user.set(session.userName);
    this.display.set(session.displayName);
    this.userRoles.set(session.roles ?? []);
  }

  /**
   * Drops every trace of the session, cached rows included.
   *
   * The cache clear is not housekeeping. TransactionListCache is root scoped and holds rows for
   * 45 seconds; without this, signing out and signing back in as somebody else inside that
   * window served the second user the first user's transactions out of memory, with no request
   * ever reaching the API to be authorized.
   */
  private clear(): void {
    this.token.set(null);
    this.user.set(null);
    this.display.set(null);
    this.userRoles.set([]);
    this.cache.clear();
  }
}
