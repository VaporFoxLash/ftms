import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  TRANSACTION_LIST_PREFIX,
  TRANSACTION_LIST_STALE_MS,
  TransactionListCache,
} from './transaction-list-cache';

describe('TransactionListCache', () => {
  let cache: TransactionListCache;
  let now: number;

  beforeEach(() => {
    now = 1_000_000;
    vi.spyOn(Date, 'now').mockImplementation(() => now);

    TestBed.configureTestingModule({});
    cache = TestBed.inject(TransactionListCache);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('returns null for a key it has never seen', () => {
    expect(cache.get('tx:list:Active:1:50:transactionDate:desc')).toBeNull();
  });

  it('returns what was stored while the entry is fresh', () => {
    cache.set('tx:list:Active:1:50:transactionDate:desc', { totalCount: 7 });

    now += TRANSACTION_LIST_STALE_MS - 1;

    expect(cache.get('tx:list:Active:1:50:transactionDate:desc')).toEqual({ totalCount: 7 });
  });

  it('expires exactly at the 45 second mark, matching the server lifetime', () => {
    // design: doc 07 section 6 - the client holds the same staleness window as the server cache.
    expect(TRANSACTION_LIST_STALE_MS).toBe(45_000);

    cache.set('tx:list:Active:1:50:transactionDate:desc', { totalCount: 7 });

    now += TRANSACTION_LIST_STALE_MS;

    expect(cache.get('tx:list:Active:1:50:transactionDate:desc')).toBeNull();
  });

  it('keeps different query shapes apart', () => {
    cache.set('tx:list:Active:1:50:transactionDate:desc', { totalCount: 1 });
    cache.set('tx:list:Inactive:1:50:transactionDate:desc', { totalCount: 2 });

    expect(cache.get<{ totalCount: number }>('tx:list:Active:1:50:transactionDate:desc')).toEqual({
      totalCount: 1,
    });
    expect(cache.get<{ totalCount: number }>('tx:list:Inactive:1:50:transactionDate:desc')).toEqual(
      {
        totalCount: 2,
      },
    );
  });

  it('drops the whole family on prefix invalidation and leaves other keys alone', () => {
    // A write can move a row between slices, so clearing only the current key would leave a
    // stale page behind. The server invalidates by prefix for the same reason (doc 07 s4).
    cache.set('tx:list:Active:1:50:transactionDate:desc', { totalCount: 1 });
    cache.set('tx:list:Inactive:2:25:amount:asc', { totalCount: 2 });
    cache.set('tx:statuses', ['Active']);

    cache.invalidatePrefix(TRANSACTION_LIST_PREFIX);

    expect(cache.get('tx:list:Active:1:50:transactionDate:desc')).toBeNull();
    expect(cache.get('tx:list:Inactive:2:25:amount:asc')).toBeNull();
    expect(cache.get('tx:statuses')).toEqual(['Active']);
  });

  it('is shared across injections, so it survives the list component being rebuilt', () => {
    // The whole reason this is root scoped: TransactionsStore is component scoped and is torn
    // down on navigation, so a cache living there would be discarded every visit.
    cache.set('tx:list:Active:1:50:transactionDate:desc', { totalCount: 3 });

    expect(
      TestBed.inject(TransactionListCache).get('tx:list:Active:1:50:transactionDate:desc'),
    ).toEqual({ totalCount: 3 });
  });
});
