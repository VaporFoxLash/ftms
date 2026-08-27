import { Dialog } from '@angular/cdk/dialog';
import { ScrollingModule } from '@angular/cdk/scrolling';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { DatePipe, DecimalPipe, LowerCasePipe } from '@angular/common';
import { debounceTime, distinctUntilChanged, firstValueFrom } from 'rxjs';
import { TransactionDto } from '../../../core/api/generated/models/transaction-dto';
import { StatusStore } from '../../../core/transaction-statuses/status.store';
import { ToastService } from '../../../core/notifications/toast.service';
import { ConfirmDialog, ConfirmDialogData } from '../../../shared/confirm-dialog/confirm-dialog';
import { Paging } from '../../../shared/paging/paging';
import { StatusBadge } from '../../../shared/status-badge/status-badge';
import {
  SORTABLE_FIELDS,
  SORT_FIELD_LABELS,
  SortField,
  TransactionsStore,
} from '../transactions.store';

/**
 * Row height in pixels.
 *
 * Virtual scrolling needs a fixed row height, and the CSS needs the same number. Declaring it
 * once here and feeding it to both the [itemSize] binding and a CSS custom property is what
 * stops the two drifting, which would show up as rows overlapping or gaps appearing mid scroll.
 */
export const ROW_HEIGHT_PX = 44;

/** How often the "updated Ns ago" label recomputes. */
const FRESHNESS_TICK_MS = 10_000;

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
    ScrollingModule,
    StatusBadge,
    Paging,
  ],
  providers: [TransactionsStore],
  templateUrl: './transaction-list.html',
  styleUrl: './transaction-list.scss',
})
export class TransactionList implements OnInit {
  protected readonly store = inject(TransactionsStore);
  protected readonly statuses = inject(StatusStore);
  private readonly toasts = inject(ToastService);
  private readonly dialog = inject(Dialog);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly sortableFields = SORTABLE_FIELDS;
  protected readonly sortFieldLabels = SORT_FIELD_LABELS;
  protected readonly rowHeight = ROW_HEIGHT_PX;

  protected readonly statusFilter = new FormControl<string>('Active', { nonNullable: true });

  /** The grid wrapper, so focus has somewhere to land when the opener is archived away. */
  private readonly grid = viewChild<ElementRef<HTMLElement>>('grid');

  /** Ticks so the freshness label re-renders. Angular 22 is zoneless: without a signal
      changing, nothing schedules a render and the label would freeze at its first value. */
  private readonly tick = signal(0);

  /**
   * Height of the scrolling viewport. Capped so a long list scrolls, but shrunk to fit so a
   * three row list does not reserve half a screen of empty space.
   */
  protected readonly viewportHeight = computed(() => {
    const rows = Math.max(this.store.transactions().length, 1);
    return `min(60vh, ${rows * ROW_HEIGHT_PX}px)`;
  });

  /** "just now" / "12s ago" / "3m ago", or null before the first load. */
  protected readonly freshness = computed(() => {
    this.tick();

    const loadedAt = this.store.lastLoadedAt();
    if (loadedAt === null) {
      return null;
    }

    const seconds = Math.max(0, Math.round((Date.now() - loadedAt) / 1000));

    if (seconds < 5) {
      return 'just now';
    }

    return seconds < 60 ? `${seconds}s ago` : `${Math.floor(seconds / 60)}m ago`;
  });

  constructor() {
    // design: doc 07 section 5 - filter inputs debounce 300 ms before hitting the API so
    // typing does not generate a request per keystroke. distinctUntilChanged matters as much
    // as the debounce: re-selecting the value already applied should cost nothing.
    this.statusFilter.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((status) => void this.store.setStatus(status));

    const ticker = setInterval(() => this.tick.update((value) => value + 1), FRESHNESS_TICK_MS);
    this.destroyRef.onDestroy(() => clearInterval(ticker));
  }

  async ngOnInit(): Promise<void> {
    // Statuses first: the filter dropdown needs them, and after the first call of the session
    // they come from the client side store rather than the network at all.
    await Promise.all([this.statuses.load(), this.store.load()]);
  }

  /**
   * trackBy for the row list.
   * design: doc 07 section 5 - trackBy on every list so Angular patches rows instead of
   * rebuilding them. cdkVirtualFor takes the same trackBy, so virtualising the list did not
   * cost us this.
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

  protected async refresh(): Promise<void> {
    await this.store.refresh();
    this.tick.update((value) => value + 1);
  }

  /**
   * Opens the archive confirmation.
   *
   * The dialog owns the in flight state and stays open if the archive fails, so the handler
   * passed here is simply the work to do. The toast fires only on success.
   */
  protected async askToArchive(transaction: TransactionDto): Promise<void> {
    const data: ConfirmDialogData = {
      title: 'Archive this transaction?',
      message:
        `This moves the ${transaction.transactionType} of ${transaction.currencyCode} ` +
        `${transaction.amount} to Inactive. The record is kept for audit, but it cannot be ` +
        `reactivated from here.`,
      confirmLabel: 'Archive',
      onConfirm: () => this.store.softDelete(transaction.id),
    };

    const ref = this.dialog.open<boolean>(ConfirmDialog, {
      data,
      panelClass: 'ftms-dialog-panel',
      backdropClass: 'ftms-dialog-backdrop',

      // Set explicitly rather than left to the CDK default, which is false. We do trap focus
      // and block background scrolling, so claiming modality here is accurate, and the whole
      // reason for moving off the hand rolled dialog was to stop the markup claiming things
      // the behaviour did not back up.
      ariaModal: true,

      // Focus starts inside the dialog and returns to the Archive button that opened it.
      autoFocus: 'first-tabbable',
      restoreFocus: true,
    });

    const archived = await firstValueFrom(ref.closed);

    if (!archived) {
      // Cancelled or dismissed. The CDK has already put focus back on the Archive button.
      return;
    }

    this.toasts.success(
      'Transaction archived',
      `${transaction.transactionType} of ${transaction.currencyCode} ${transaction.amount}`,
    );

    // On success the Archive button that opened the dialog no longer exists: the row has left
    // the Active list. The CDK has nothing to restore focus to and drops it on <body>, which
    // strands a keyboard user at the top of the document. Move focus to the grid instead, so
    // the next Tab continues from where they were working.
    this.grid()?.nativeElement.focus();
  }
}
