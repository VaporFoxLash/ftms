import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TransactionDto } from '../../../core/api/generated/models/transaction-dto';
import { StatusBadge } from '../../../shared/status-badge/status-badge';
import { TransactionsStore } from '../transactions.store';
import { ZardButtonComponent } from '@/shared/components/button/button.component';

/**
 * One transaction, in any status.
 *
 * design: doc 05 section 4 and decision 2 - get by id returns transactions in any status,
 * including Inactive, because this endpoint is the audit window. So this page must render an
 * archived record perfectly happily rather than treating it as an error.
 */
@Component({
  selector: 'ftms-transaction-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe, RouterLink, StatusBadge, ZardButtonComponent],
  providers: [TransactionsStore],
  templateUrl: './transaction-detail.html',
  styleUrl: './transaction-detail.scss',
})
export class TransactionDetail implements OnInit {
  private readonly store = inject(TransactionsStore);
  private readonly route = inject(ActivatedRoute);

  protected readonly transaction = signal<TransactionDto | null>(null);
  protected readonly etag = signal('');
  protected readonly loading = signal(true);

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');

    if (!id) {
      this.loading.set(false);
      return;
    }

    try {
      const { transaction, etag } = await this.store.loadOne(id);
      this.transaction.set(transaction);
      this.etag.set(etag);
    } finally {
      this.loading.set(false);
    }
  }
}
