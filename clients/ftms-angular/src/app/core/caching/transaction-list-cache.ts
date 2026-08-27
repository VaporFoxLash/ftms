import { Injectable } from '@angular/core';

/**
 * The key prefix every list shaped entry hangs off, so one call invalidates the whole family.
 * Mirrors CacheKeys.TransactionListPrefix in FTMS.Application.
 */
export const TRANSACTION_LIST_PREFIX = 'tx:list:';

/**
 * 45 seconds, mirroring CacheKeys.TransactionListLifetime on the server.
 *
 * design: doc 07 section 6 - "The same 45 second staleness contract as the server cache applies
 * to the client's own memory of the list, so both clients agree on how fresh is fresh."
 */
export const TRANSACTION_LIST_STALE_MS = 45_000;

interface CacheEntry {
  readonly value: unknown;
  readonly expiresAt: number;
}

/**
 * A small time bounded cache for list results.
 *
 * Root scoped on purpose. TransactionsStore is provided by the list COMPONENT, so it is torn
 * down and rebuilt every time the user navigates away and back; a timestamp held there would be
 * discarded each time and the staleness contract would buy nothing. Keeping the cache here and
 * the page state (current filter, page) in the component scoped store gives the right lifetime
 * to each: what the user was looking at resets per visit, what the server told us does not.
 *
 * The key shape deliberately matches the server's, right down to the string format
 * (tx:list:{status}:{page}:{pageSize}:{sortBy}:{sortDir}). Identical keys and an identical TTL
 * are what make "both clients agree on how fresh is fresh" a fact rather than an aspiration.
 */
@Injectable({ providedIn: 'root' })
export class TransactionListCache {
  private readonly entries = new Map<string, CacheEntry>();

  /** The cached value, or null when absent or expired. */
  get<T>(key: string): T | null {
    const entry = this.entries.get(key);

    if (!entry) {
      return null;
    }

    // Read the clock through Date.now() rather than a timer so tests can move time with a spy
    // instead of fighting fake timers against the store's awaits.
    if (Date.now() >= entry.expiresAt) {
      this.entries.delete(key);
      return null;
    }

    return entry.value as T;
  }

  set<T>(key: string, value: T, staleMs = TRANSACTION_LIST_STALE_MS): void {
    this.entries.set(key, { value, expiresAt: Date.now() + staleMs });
  }

  /**
   * Drops every entry whose key starts with the prefix.
   *
   * design: doc 07 section 4 - the list is cached per query shape, so invalidating after a write
   * means clearing the whole tx:list: family rather than guessing which shapes exist. The three
   * commands do exactly this server side via ICacheService.RemoveByPrefix.
   */
  invalidatePrefix(prefix: string): void {
    for (const key of [...this.entries.keys()]) {
      if (key.startsWith(prefix)) {
        this.entries.delete(key);
      }
    }
  }

  /** Test and sign out hook. Nothing in the app should need this during normal use. */
  clear(): void {
    this.entries.clear();
  }
}
