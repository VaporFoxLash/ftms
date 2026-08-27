import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { DatePipe, DecimalPipe, LowerCasePipe } from '@angular/common';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { TransactionDto } from '../../../core/api/generated/models/transaction-dto';
import { StatusStore } from '../../../core/transaction-statuses/status.store';
import { ToastService } from '../../../core/notifications/toast.service';
import { ConfirmDialog } from '../../../shared/confirm-dialog/confirm-dialog';
import { Paging } from '../../../shared/paging/paging';
import { StatusBadge } from '../../../shared/status-badge/status-badge';
import {
  SORTABLE_FIELDS,
  SORT_FIELD_LABELS,
  SortField,
  TransactionsStore,
} from '../transactions.store';

/**
 * The transactions screen. design: doc 07 section 5 - this is the hot path.
 */
@Component({
  selector: 'ftms-transaction-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    DatePipe,
    DecimalPipe,
    LowerCasePipe,
    StatusBadge,
    Paging,
    ConfirmDialog,
  ],
  providers: [TransactionsStore],
  templateUrl: './transaction-list.html',
  styleUrl: './transaction-list.scss',
})
export class TransactionList implements OnInit {
  protected readonly store = inject(TransactionsStore);
  protected readonly statuses = inject(StatusStore);
  private readonly toasts = inject(ToastService);

  protected readonly sortableFields = SORTABLE_FIELDS;
  protected readonly sortFieldLabels = SORT_FIELD_LABELS;

  protected readonly statusFilter = new FormControl<string>('Active', { nonNullable: true });

  /** The row awaiting confirmation, or null when no dialog is open. */
  protected readonly pendingArchive = signal<TransactionDto | null>(null);
  protected readonly archiving = signal(false);

  constructor() {
    // design: doc 07 section 5 - filter inputs debounce 300 ms before hitting the API so
    // typing does not generate a request per keystroke. distinctUntilChanged matters as much
    // as the debounce: re-selecting the value already applied should cost nothing.
    this.statusFilter.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((status) => void this.store.setStatus(status));
  }

  async ngOnInit(): Promise<void> {
    // Statuses first: the filter dropdown needs them, and after the first call of the session
    // they come from the client side store rather than the network at all.
    await Promise.all([this.statuses.load(), this.store.load()]);
  }

  /**
   * trackBy for the row list.
   * design: doc 07 section 5 - trackBy on every list so Angular patches rows instead of
   * rebuilding them. Without it, every poll or filter change throws away and recreates every
   * DOM row, which is what makes long financial lists feel sluggish.
   */
  protected trackById = (_: number, transaction: TransactionDto): string => transaction.id;

  /**
   * Active and Pending are the working states; everything else is history.
   * design: doc 02 section 5. Mirrored here only to decide which row actions to render. The
   * domain enforces it regardless, so this is about not offering a button that would fail.
   */
  protected isWorking(status: string): boolean {
    return status === 'Active' || status === 'Pending';
  }

  protected async changeSort(field: string): Promise<void> {
    const current = this.store.currentQuery();
    const sortBy = field as SortField;

    // Clicking the column you are already sorted by flips direction, which is what people
    // expect from a table header.
    const sortDir = current.sortBy === sortBy && current.sortDir === 'desc' ? 'asc' : 'desc';

    await this.store.setSort(sortBy, sortDir);
  }

  protected askToArchive(transaction: TransactionDto): void {
    this.pendingArchive.set(transaction);
  }

  protected cancelArchive(): void {
    this.pendingArchive.set(null);
  }

  protected async confirmArchive(): Promise<void> {
    const target = this.pendingArchive();

    if (!target) {
      return;
    }

    this.archiving.set(true);

    try {
      await this.store.softDelete(target.id);
      this.toasts.success(
        `Transaction archived`,
        `${target.transactionType} of ${target.currencyCode} ${target.amount}`,
      );
      this.pendingArchive.set(null);
    } catch {
      // The error interceptor has already surfaced the problem. Leave the dialog open so the
      // user can retry without hunting for the row again.
    } finally {
      this.archiving.set(false);
    }
  }
}
