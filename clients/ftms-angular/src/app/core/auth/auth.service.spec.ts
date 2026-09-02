import { TestBed } from '@angular/core/testing';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { Api } from '@/core/api/generated/api';
import { login } from '@/core/api/generated/fn/auth/login';
import { logout } from '@/core/api/generated/fn/auth/logout';
import { refreshSession } from '@/core/api/generated/fn/auth/refresh-session';
import { TransactionListCache } from '@/core/caching/transaction-list-cache';
import { AuthService } from './auth.service';

const session = (userName: string, token: string) => ({
  accessToken: token,
  expiresInSeconds: 900,
  userName,
  displayName: `${userName} display`,
  roles: ['Manager'],
});

describe('AuthService', () => {
  let invoke: ReturnType<typeof vi.fn>;
  let auth: AuthService;
  let cache: TransactionListCache;

  beforeEach(() => {
    invoke = vi.fn();

    TestBed.configureTestingModule({
      providers: [{ provide: Api, useValue: { invoke } }],
    });

    auth = TestBed.inject(AuthService);
    cache = TestBed.inject(TransactionListCache);
  });

  it('holds the access token in memory after signing in', async () => {
    invoke.mockResolvedValue(session('manager', 'token-1'));

    await auth.signIn('manager', 'Manager#2026');

    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.accessToken()).toBe('token-1');
    expect(auth.userName()).toBe('manager');
    expect(auth.displayName()).toBe('manager display');
    expect(auth.roles()).toEqual(['Manager']);

    // Nothing may be written to storage that a script could read back. The whole reason the
    // token lives in a signal is that localStorage is readable by any script on the page.
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it('sends only the username and password, never a role', async () => {
    invoke.mockResolvedValue(session('manager', 'token-1'));

    await auth.signIn('manager', 'Manager#2026');

    // The old login let the caller name the roles they wanted, because the dev token endpoint
    // handed out whatever it was asked for. Roles now come from the identity store.
    expect(invoke).toHaveBeenCalledWith(login, {
      body: { userName: 'manager', password: 'Manager#2026' },
    });
  });

  describe('signing out', () => {
    beforeEach(async () => {
      invoke.mockResolvedValue(session('manager', 'token-1'));
      await auth.signIn('manager', 'Manager#2026');
      invoke.mockReset();
    });

    it('clears the cached transaction list', async () => {
      cache.set('tx:list:Active:1:50:transactionDate:desc', ['manager rows']);
      invoke.mockResolvedValue(undefined);

      await auth.signOut();

      // The regression this pins: TransactionListCache is root scoped with a 45 second TTL, so
      // without this clear, signing out and back in as somebody else inside that window served
      // the second user the first user's rows straight from memory - no request, and therefore
      // no authorization check, ever reached the API.
      expect(cache.get('tx:list:Active:1:50:transactionDate:desc')).toBeNull();
      expect(auth.isAuthenticated()).toBe(false);
    });

    it('revokes the session server side', async () => {
      invoke.mockResolvedValue(undefined);

      await auth.signOut();

      expect(invoke).toHaveBeenCalledWith(logout, {});
    });

    it('still signs out locally when the server call fails', async () => {
      invoke.mockRejectedValue(new Error('network down'));
      cache.set('tx:list:Active:1:50:transactionDate:desc', ['manager rows']);

      await auth.signOut();

      // A user who asked to sign out must end up signed out whatever the network did.
      expect(auth.isAuthenticated()).toBe(false);
      expect(cache.get('tx:list:Active:1:50:transactionDate:desc')).toBeNull();
    });
  });

  describe('refreshing', () => {
    it('adopts the new token', async () => {
      invoke.mockResolvedValue(session('manager', 'token-2'));

      await expect(auth.refresh()).resolves.toBe(true);
      expect(auth.accessToken()).toBe('token-2');
      expect(invoke).toHaveBeenCalledWith(refreshSession, {});
    });

    it('reports failure and clears state when the cookie is gone', async () => {
      invoke.mockResolvedValue(session('manager', 'token-1'));
      await auth.signIn('manager', 'Manager#2026');

      invoke.mockRejectedValue(new Error('401'));

      await expect(auth.refresh()).resolves.toBe(false);
      expect(auth.isAuthenticated()).toBe(false);
    });

    it('coalesces concurrent refreshes into one request', async () => {
      // The important one. Refresh tokens are SINGLE USE: if a page issues several requests that
      // all 401 at once and each starts its own refresh, the second presents a token the first
      // already spent. The server correctly reads that as a replay and revokes the entire
      // session - so racing ourselves is indistinguishable from a stolen token, and the user is
      // logged out for being busy.
      let resolve: (value: unknown) => void = () => {};
      invoke.mockReturnValue(
        new Promise((r) => {
          resolve = r;
        }),
      );

      const [first, second, third] = [auth.refresh(), auth.refresh(), auth.refresh()];

      expect(invoke).toHaveBeenCalledTimes(1);

      resolve(session('manager', 'token-2'));

      expect(await Promise.all([first, second, third])).toEqual([true, true, true]);
    });

    it('starts a fresh request once the previous one has settled', async () => {
      invoke.mockResolvedValue(session('manager', 'token-2'));

      await auth.refresh();
      await auth.refresh();

      // Coalescing must not turn into caching: a later refresh is a new rotation, not a replay
      // of the previous answer.
      expect(invoke).toHaveBeenCalledTimes(2);
    });
  });

  it('restores a session from the refresh cookie at startup', async () => {
    invoke.mockResolvedValue(session('manager', 'token-2'));

    await expect(auth.restore()).resolves.toBe(true);
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('reports no session rather than throwing on a first visit', async () => {
    invoke.mockRejectedValue(new Error('401'));

    // Arriving with no cookie is the normal case for a first visit, not an error worth
    // surfacing - the guard just redirects to the login screen.
    await expect(auth.restore()).resolves.toBe(false);
  });
});
