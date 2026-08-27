import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Api } from '../../core/api/generated/api';
import { TRANSACTION_LIST_STALE_MS } from '../../core/caching/transaction-list-cache';
import {
  DEFAULT_PAGE_SIZE,
  MAX_PAGE_SIZE,
  TransactionsStore,
  transactionListCacheKey,
} from './transactions.store';

/**
 * design: doc 08 section 5 - test what the user sees rather than component internals. For a
 * store that means asserting the state it exposes and the calls it makes, never its privates.
 */
describe('TransactionsStore', () => {
  let store: TransactionsStore;
  let invoke: ReturnType<typeof vi.fn>;
  let invokeResponse: ReturnType<typeof vi.fn>;
  let now: number;

  const emptyPage = { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 };

  beforeEach(() => {
    invoke = vi.fn().mockResolvedValue(emptyPage);
    invokeResponse = vi.fn();

    // The clock is read through Date.now() so it can be moved with a spy, rather than fake
    // timers, which would also intercept the promise scheduling the store relies on.
    now = 1_000_000;
    vi.spyOn(Date, 'now').mockImplementation(() => now);

    TestBed.configureTestingModule({
      providers: [
        TransactionsStore,
        { provide: Api, useValue: { invoke, invoke$Response: invokeResponse } },
      ],
    });

    store = TestBed.inject(TransactionsStore);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('opens on Active, page one, newest first, matching the endpoint defaults', () => {
    // design: doc 05 section 3 - called bare the endpoint returns Active only, so the screen
    // must open on the same slice or the two disagree about what "the list" means.
    expect(store.currentQuery()).toEqual({
      status: 'Active',
      page: 1,
      pageSize: DEFAULT_PAGE_SIZE,
      sortBy: 'transactionDate',
      sortDir: 'desc',
    });
  });

  it('sends the whole query shape to the API', async () => {
    await store.load();

    expect(invoke).toHaveBeenCalledTimes(1);
    expect(invoke.mock.calls[0][1]).toEqual({
      status: 'Active',
      page: 1,
      pageSize: 50,
      sortBy: 'transactionDate',
      sortDir: 'desc',
    });
  });

  it('reads rows and the total out of the paging envelope', async () => {
    invoke.mockResolvedValueOnce({
      items: [{ id: 'a' }, { id: 'b' }],
      page: 1,
      pageSize: 50,
      totalCount: 137,
    });

    await store.load();

    expect(store.transactions()).toHaveLength(2);
    expect(store.totalCount()).toBe(137);
    expect(store.totalPages()).toBe(3);
    expect(store.hasNextPage()).toBe(true);
    expect(store.hasPreviousPage()).toBe(false);
  });

  it('clamps page size to the server cap rather than asking for more', async () => {
    // design: doc 05 section 3 - pageSize is capped at 200 server side. Clamping here too
    // means the UI never displays a number the server quietly ignored.
    await store.setPageSize(5000);
    expect(store.pageSize()).toBe(MAX_PAGE_SIZE);

    await store.setPageSize(0);
    expect(store.pageSize()).toBe(1);

    await store.setPageSize(25);
    expect(store.pageSize()).toBe(25);
  });

  it('returns to page one when the filter changes', async () => {
    invoke.mockResolvedValue({ items: [], page: 1, pageSize: 50, totalCount: 500 });
    await store.load();
    await store.goToPage(4);
    expect(store.page()).toBe(4);

    await store.setStatus('Inactive');

    expect(store.status()).toBe('Inactive');
    expect(store.page()).toBe(1);
  });

  it('will not page past the end or before the start', async () => {
    invoke.mockResolvedValue({ items: [], page: 1, pageSize: 50, totalCount: 60 });
    await store.load();

    await store.goToPage(99);
    expect(store.page()).toBe(2);

    await store.goToPage(-5);
    expect(store.page()).toBe(1);
  });

  it('flips sort direction and resets to page one', async () => {
    await store.setSort('amount', 'asc');

    expect(store.currentQuery().sortBy).toBe('amount');
    expect(store.currentQuery().sortDir).toBe('asc');
    expect(store.page()).toBe(1);
  });

  it('reads the ETag from the response header, not the body', async () => {
    // design: doc 05 section 4 - the ETag is a response HEADER. A client that looked for it in
    // the body would be coupled to a representation the contract never promised.
    invokeResponse.mockResolvedValue({
      body: { id: 'abc', status: 'Active' },
      headers: { get: (name: string) => (name === 'ETag' ? '"AAAAAAAAB9E="' : null) },
    });

    const result = await store.loadOne('abc');

    expect(result.etag).toBe('"AAAAAAAAB9E="');
    expect(result.transaction.id).toBe('abc');
  });

  it('sends the ETag as If-Match when updating', async () => {
    // design: doc 05 section 6 - silent last writer wins is not acceptable on financial records.
    invoke.mockResolvedValue({ id: 'abc' });

    await store.update('abc', '"AAAAAAAAB9E="', {
      transactionDate: '2026-08-21T10:00:00Z',
      transactionType: 'Transfer',
    });

    expect(invoke.mock.calls[0][1]).toMatchObject({
      id: 'abc',
      'If-Match': '"AAAAAAAAB9E="',
    });
  });

  it('reloads the list after a soft delete so the archived row leaves the Active view', async () => {
    await store.softDelete('abc');

    // One call to delete, one to reload.
    expect(invoke).toHaveBeenCalledTimes(2);
  });

  it('reports empty only once loading has finished', async () => {
    expect(store.isEmpty()).toBe(true);

    const pending = store.load();
    expect(store.isLoading()).toBe(true);

    await pending;
    expect(store.isLoading()).toBe(false);
  });

  /**
   * design: doc 07 section 6 - "The same 45 second staleness contract as the server cache
   * applies to the client's own memory of the list, so both clients agree on how fresh is
   * fresh." The server half is doc 07 section 4: tx:list: entries live 45 seconds and all three
   * commands invalidate by prefix.
   */
  describe('the 45 second staleness contract', () => {
    it('builds the same cache key shape the server does', () => {
      // Matches CacheKeys.TransactionList in FTMS.Application. Identical keys and an identical
      // TTL are what make the two caches agree rather than merely coexist.
      expect(transactionListCacheKey(store.currentQuery())).toBe(
        'tx:list:Active:1:50:transactionDate:desc',
      );
    });

    it('serves a repeated load from cache without touching the API', async () => {
      await store.load();
      expect(invoke).toHaveBeenCalledTimes(1);

      now += 44_000;
      await store.load();

      expect(invoke).toHaveBeenCalledTimes(1);
      expect(store.servedFromCache()).toBe(true);
    });

    it('refetches once the entry is older than the window', async () => {
      await store.load();
      now += TRANSACTION_LIST_STALE_MS + 1;

      await store.load();

      expect(invoke).toHaveBeenCalledTimes(2);
      expect(store.servedFromCache()).toBe(false);
    });

    it('does not serve one query shape from another shape cache entry', async () => {
      await store.load();

      await store.setStatus('Inactive');

      // A different slice is a different key, so the cache cannot hand back Active rows for an
      // Inactive filter. This is the failure mode a bare timestamp would have had.
      expect(invoke).toHaveBeenCalledTimes(2);
      expect(store.servedFromCache()).toBe(false);
    });

    it('reports when rows came from cache and when they came from the network', async () => {
      await store.load();
      expect(store.servedFromCache()).toBe(false);
      expect(store.lastLoadedAt()).toBe(now);

      const fetchedAt = now;
      now += 1_000;
      await store.load();

      expect(store.servedFromCache()).toBe(true);

      // The age is when the rows were FETCHED, not when they were read out of the cache.
      // Stamping it on read would make a stale list claim to be brand new.
      expect(store.lastLoadedAt()).toBe(fetchedAt);
    });

    it.each([
      ['create', async () => void (await store.create({} as never))],
      ['update', async () => void (await store.update('id', 'etag', {} as never))],
      ['softDelete', async () => void (await store.softDelete('id'))],
    ])('%s invalidates the cache and refetches rather than replaying it', async (_name, mutate) => {
      await store.load();
      invoke.mockClear();

      await mutate();

      // Two calls: the write itself, then a genuine reload. Still well inside the 45 second
      // window, so without the prefix invalidation the reload would have been served from
      // cache and shown a list the write had just made wrong.
      expect(invoke).toHaveBeenCalledTimes(2);
      expect(store.servedFromCache()).toBe(false);
    });

    it('refresh forces a fetch even when the entry is fresh', async () => {
      await store.load();
      invoke.mockClear();

      await store.refresh();

      expect(invoke).toHaveBeenCalledTimes(1);
      expect(store.servedFromCache()).toBe(false);
    });

    it('never caches get by id, because that is where the ETag comes from', async () => {
      // design: doc 07 section 4 keeps get by id off the cache in favour of ETag and 304. A
      // cached ETag would be a stale one, and the edit form would get a 412 for a change the
      // user did make cleanly.
      invokeResponse.mockResolvedValue({
        body: { id: 'abc' },
        headers: { get: () => '"AAAAAAAAB9E="' },
      });

      await store.loadOne('abc');
      await store.loadOne('abc');

      expect(invokeResponse).toHaveBeenCalledTimes(2);
    });
  });
});
