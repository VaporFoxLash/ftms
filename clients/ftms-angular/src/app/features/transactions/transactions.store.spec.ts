import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Api } from '../../core/api/generated/api';
import { DEFAULT_PAGE_SIZE, MAX_PAGE_SIZE, TransactionsStore } from './transactions.store';

/**
 * design: doc 08 section 5 - test what the user sees rather than component internals. For a
 * store that means asserting the state it exposes and the calls it makes, never its privates.
 */
describe('TransactionsStore', () => {
  let store: TransactionsStore;
  let invoke: ReturnType<typeof vi.fn>;
  let invokeResponse: ReturnType<typeof vi.fn>;

  const emptyPage = { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 };

  beforeEach(() => {
    invoke = vi.fn().mockResolvedValue(emptyPage);
    invokeResponse = vi.fn();

    TestBed.configureTestingModule({
      providers: [TransactionsStore, { provide: Api, useValue: { invoke, invoke$Response: invokeResponse } }],
    });

    store = TestBed.inject(TransactionsStore);
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
});
