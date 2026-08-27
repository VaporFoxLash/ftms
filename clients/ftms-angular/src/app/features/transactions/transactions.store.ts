import { Injectable, computed, inject, signal } from '@angular/core';
import {
  TRANSACTION_LIST_PREFIX,
  TransactionListCache,
} from '../../core/caching/transaction-list-cache';
import { Api } from '../../core/api/generated/api';
import { createTransaction } from '../../core/api/generated/fn/transactions/create-transaction';
import { deleteTransaction } from '../../core/api/generated/fn/transactions/delete-transaction';
import { getTransactionById } from '../../core/api/generated/fn/transactions/get-transaction-by-id';
import { listTransactions } from '../../core/api/generated/fn/transactions/list-transactions';
import { updateTransaction } from '../../core/api/generated/fn/transactions/update-transaction';
import { CreateTransactionRequest } from '../../core/api/generated/models/create-transaction-request';
import { TransactionDto } from '../../core/api/generated/models/transaction-dto';
import { UpdateTransactionRequest } from '../../core/api/generated/models/update-transaction-request';

/** Mirrors the server side cap. design: doc 05 section 3. */
export const MAX_PAGE_SIZE = 200;

export const DEFAULT_PAGE_SIZE = 50;

/** The sort fields the API is willing to order by; anything else is a 400. */
export const SORTABLE_FIELDS = [
  'transactionDate',
  'amount',
  'createdAtUtc',
  'transactionType',
] as const;

export type SortField = (typeof SORTABLE_FIELDS)[number];

/**
 * Column headings for the sortable fields.
 *
 * The field names above are part of the API contract and must be sent verbatim, but they are
 * developer vocabulary. A finance user reading a transaction list should see "Captured", not
 * "createdAtUtc".
 */
export const SORT_FIELD_LABELS: Readonly<Record<SortField, string>> = {
  transactionDate: 'Date',
  amount: 'Amount',
  createdAtUtc: 'Captured',
  transactionType: 'Type',
};

export interface ListQuery {
  readonly status: string;
  readonly page: number;
  readonly pageSize: number;
  readonly sortBy: SortField;
  readonly sortDir: 'asc' | 'desc';
}

/**
 * tx:list:{status}:{page}:{pageSize}:{sortBy}:{sortDir}
 *
 * Byte for byte the shape CacheKeys.TransactionList builds in FTMS.Application. Keeping them
 * identical is the point: it is what lets the client honour the same 45 second staleness window
 * the server does, on the same slices of data. design: doc 07 sections 4 and 6.
 */
export function transactionListCacheKey(query: ListQuery): string {
  return (
    `${TRANSACTION_LIST_PREFIX}${query.status}:${query.page}:` +
    `${query.pageSize}:${query.sortBy}:${query.sortDir}`
  );
}

/** What the cache holds for a list query. */
interface CachedPage {
  readonly items: readonly TransactionDto[];
  readonly totalCount: number;
  readonly loadedAt: number;
}

/**
 * Page state for the transactions screen.
 *
 * design: doc 07 section 5 - the transactions screen is the hot path, so it gets OnPush change
 * detection with signals for state. Angular 22 is zoneless, so signals are not merely the fast
 * option here, they are the only thing that schedules a render.
 *
 * A transaction's ETag is kept alongside the row because doc 05 section 6 requires If-Match on
 * every update. The client cannot invent one: it has to be the exact value a prior GET returned.
 */
@Injectable()
export class TransactionsStore {
  private readonly api = inject(Api);
  private readonly cache = inject(TransactionListCache);

  private readonly rows = signal<readonly TransactionDto[]>([]);
  private readonly total = signal(0);
  private readonly busy = signal(false);
  private readonly loadedAt = signal<number | null>(null);
  private readonly fromCache = signal(false);
  private readonly query = signal<ListQuery>({
    // design: doc 05 section 3 - called bare the endpoint returns Active only, and the screen
    // opens on the same default so the two never disagree about what "the list" means.
    status: 'Active',
    page: 1,
    pageSize: DEFAULT_PAGE_SIZE,
    sortBy: 'transactionDate',
    sortDir: 'desc',
  });

  readonly transactions = this.rows.asReadonly();
  readonly totalCount = this.total.asReadonly();
  readonly isLoading = this.busy.asReadonly();
  readonly currentQuery = this.query.asReadonly();

  /** When the currently displayed rows were fetched from the API. Drives the freshness hint. */
  readonly lastLoadedAt = this.loadedAt.asReadonly();

  /** True when the last load() was answered from cache rather than the network. */
  readonly servedFromCache = this.fromCache.asReadonly();

  readonly pageSize = computed(() => this.query().pageSize);
  readonly page = computed(() => this.query().page);
  readonly status = computed(() => this.query().status);

  readonly totalPages = computed(() => {
    const size = this.query().pageSize;
    return size <= 0 ? 0 : Math.ceil(this.total() / size);
  });

  readonly hasPreviousPage = computed(() => this.query().page > 1);
  readonly hasNextPage = computed(() => this.query().page < this.totalPages());
  readonly isEmpty = computed(() => !this.busy() && this.rows().length === 0);

  /**
   * Loads the current query shape, from cache when it is still fresh.
   *
   * There is deliberately no force flag. Mutations invalidate the tx:list: prefix first, so the
   * next load() misses and refetches on its own, which is exactly how the server behaves: the
   * three commands call RemoveByPrefix and the next query repopulates. One mechanism, two
   * layers. design: doc 07 sections 4 and 6.
   */
  async load(): Promise<void> {
    const current = this.query();
    const key = transactionListCacheKey(current);

    const cached = this.cache.get<CachedPage>(key);
    if (cached) {
      this.rows.set(cached.items);
      this.total.set(cached.totalCount);
      this.loadedAt.set(cached.loadedAt);
      this.fromCache.set(true);
      return;
    }

    this.busy.set(true);

    try {
      const result = await this.api.invoke(listTransactions, {
        status: current.status,
        page: current.page,
        pageSize: current.pageSize,
        sortBy: current.sortBy,
        sortDir: current.sortDir,
      });

      const page: CachedPage = {
        items: result.items ?? [],
        totalCount: Number(result.totalCount ?? 0),
        loadedAt: Date.now(),
      };

      this.cache.set(key, page);

      this.rows.set(page.items);
      this.total.set(page.totalCount);
      this.loadedAt.set(page.loadedAt);
      this.fromCache.set(false);
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Drops the cache and reloads. Backs the Refresh control.
   *
   * Caching a financial list means another user's change can stay invisible for up to 45
   * seconds. That is the contract doc 07 chose, but it is only honest if the user has a way to
   * override it, which is what this is for.
   */
  async refresh(): Promise<void> {
    this.cache.invalidatePrefix(TRANSACTION_LIST_PREFIX);
    await this.load();
  }

  /** Changing a filter resets to page one; staying on page 40 of a different filter is nonsense. */
  async setStatus(status: string): Promise<void> {
    this.query.update((current) => ({ ...current, status, page: 1 }));
    await this.load();
  }

  async setSort(sortBy: SortField, sortDir: 'asc' | 'desc'): Promise<void> {
    this.query.update((current) => ({ ...current, sortBy, sortDir, page: 1 }));
    await this.load();
  }

  async setPageSize(pageSize: number): Promise<void> {
    // Clamped client side as well as server side. The server is the authority, but clamping
    // here means the UI never shows a number the server silently ignored.
    //
    // The two cases are separated deliberately: unparseable input falls back to the default,
    // while a number simply out of range is clamped into it. Writing this as
    // `Math.trunc(pageSize) || DEFAULT` would treat 0 as garbage rather than as too small,
    // because 0 is falsy.
    const requested = Math.trunc(pageSize);
    const clamped = Number.isFinite(requested)
      ? Math.min(Math.max(requested, 1), MAX_PAGE_SIZE)
      : DEFAULT_PAGE_SIZE;
    this.query.update((current) => ({ ...current, pageSize: clamped, page: 1 }));
    await this.load();
  }

  async goToPage(page: number): Promise<void> {
    const pages = this.totalPages();
    const target = Math.min(Math.max(Math.trunc(page) || 1, 1), Math.max(pages, 1));

    if (target === this.query().page) {
      return;
    }

    this.query.update((current) => ({ ...current, page: target }));
    await this.load();
  }

  /**
   * Reads one transaction and its ETag. The ETag is a response HEADER, not a body field
   * (doc 05 section 4), so this uses invoke$Response rather than invoke.
   *
   * Deliberately NOT cached, for two reasons that point the same way. design: doc 07 section 4
   * keeps get by id off the cache in favour of the ETag and a 304, because correctness beats
   * micro savings on a primary key lookup. And this is the call the edit form makes to obtain
   * the If-Match value: a cached ETag would be a stale one, and the server would answer 412 to
   * a user who had changed nothing.
   */
  async loadOne(id: string): Promise<{ transaction: TransactionDto; etag: string }> {
    const response = await this.api.invoke$Response(getTransactionById, { id });

    return {
      transaction: response.body,
      etag: response.headers.get('ETag') ?? '',
    };
  }

  async create(request: CreateTransactionRequest): Promise<TransactionDto> {
    const created = await this.api.invoke(createTransaction, { body: request });
    await this.invalidateAndReload();

    return created;
  }

  /**
   * Updates date and type. The etag must be the one from a prior GET: the server answers 428
   * without an If-Match header and 412 when it is stale, which is what stops two people
   * silently overwriting each other (doc 05 section 6).
   */
  async update(
    id: string,
    etag: string,
    request: UpdateTransactionRequest,
  ): Promise<TransactionDto> {
    const updated = await this.api.invoke(updateTransaction, {
      id,
      'If-Match': etag,
      body: request,
    });

    await this.invalidateAndReload();

    return updated;
  }

  /** Soft delete. The row is archived to Inactive, never removed. design: doc 05 section 7. */
  async softDelete(id: string): Promise<void> {
    await this.api.invoke(deleteTransaction, { id });
    await this.invalidateAndReload();
  }

  /**
   * Every write clears the whole tx:list: family before reloading.
   *
   * The whole family, not just the current key, because a write can move a row between slices:
   * archiving takes it out of Active and puts it into Inactive, so leaving the Inactive page
   * cached would show a list missing a row that is now in it. design: doc 07 section 4, which is
   * why the server invalidates by prefix too.
   */
  private async invalidateAndReload(): Promise<void> {
    this.cache.invalidatePrefix(TRANSACTION_LIST_PREFIX);
    await this.load();
  }
}
