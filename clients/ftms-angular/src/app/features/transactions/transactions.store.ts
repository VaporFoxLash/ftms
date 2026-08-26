import { Injectable, computed, inject, signal } from '@angular/core';
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
export const SORTABLE_FIELDS = ['transactionDate', 'amount', 'createdAtUtc', 'transactionType'] as const;

export type SortField = (typeof SORTABLE_FIELDS)[number];

export interface ListQuery {
  readonly status: string;
  readonly page: number;
  readonly pageSize: number;
  readonly sortBy: SortField;
  readonly sortDir: 'asc' | 'desc';
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

  private readonly rows = signal<readonly TransactionDto[]>([]);
  private readonly total = signal(0);
  private readonly busy = signal(false);
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

  async load(): Promise<void> {
    this.busy.set(true);

    try {
      const current = this.query();
      const result = await this.api.invoke(listTransactions, {
        status: current.status,
        page: current.page,
        pageSize: current.pageSize,
        sortBy: current.sortBy,
        sortDir: current.sortDir,
      });

      this.rows.set(result.items ?? []);
      this.total.set(Number(result.totalCount ?? 0));
    } finally {
      this.busy.set(false);
    }
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
    await this.load();

    return created;
  }

  /**
   * Updates date and type. The etag must be the one from a prior GET: the server answers 428
   * without an If-Match header and 412 when it is stale, which is what stops two people
   * silently overwriting each other (doc 05 section 6).
   */
  async update(id: string, etag: string, request: UpdateTransactionRequest): Promise<TransactionDto> {
    const updated = await this.api.invoke(updateTransaction, {
      id,
      'If-Match': etag,
      body: request,
    });

    await this.load();

    return updated;
  }

  /** Soft delete. The row is archived to Inactive, never removed. design: doc 05 section 7. */
  async softDelete(id: string): Promise<void> {
    await this.api.invoke(deleteTransaction, { id });
    await this.load();
  }
}
