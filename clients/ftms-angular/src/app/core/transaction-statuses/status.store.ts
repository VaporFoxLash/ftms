import { Injectable, computed, inject, signal } from '@angular/core';
import { Api } from '../api/generated/api';
import { listTransactionStatuses } from '../api/generated/fn/transaction-statuses/list-transaction-statuses';
import { TransactionStatusDto } from '../api/generated/models/transaction-status-dto';

/**
 * The five statuses, loaded once and kept for the session.
 *
 * design: doc 07 section 5 - statuses load once at startup from the cached endpoint and live in
 * a signal store for the session. The server side of that bargain is doc 05 section 2: the set
 * is tiny and effectively immutable, cached for 24 hours, which makes it the perfect cache warm
 * up call.
 */
@Injectable({ providedIn: 'root' })
export class StatusStore {
  private readonly api = inject(Api);

  private readonly items = signal<readonly TransactionStatusDto[]>([]);
  private readonly loading = signal(false);

  readonly statuses = this.items.asReadonly();
  readonly isLoading = this.loading.asReadonly();

  /** Status names in the order the state machine moves through them, not alphabetically. */
  readonly names = computed(() => {
    const order = ['Active', 'Pending', 'Completed', 'Cancelled', 'Inactive'];
    return [...this.items()]
      .map((status) => status.statusName)
      .sort((left, right) => order.indexOf(left) - order.indexOf(right));
  });

  /** Idempotent: calling it again after a successful load is a no op. */
  async load(): Promise<void> {
    if (this.items().length > 0 || this.loading()) {
      return;
    }

    this.loading.set(true);

    try {
      // The generated Api helper returns a Promise, not an Observable.
      const statuses = await this.api.invoke(listTransactionStatuses);
      this.items.set(statuses);
    } finally {
      this.loading.set(false);
    }
  }
}
