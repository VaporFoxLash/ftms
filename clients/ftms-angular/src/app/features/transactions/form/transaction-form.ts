import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ToastService } from '../../../core/notifications/toast.service';
import { summariseProblem } from '../../../core/http/problem-details';
import { TransactionsStore } from '../transactions.store';

/**
 * The transaction types the domain accepts.
 * design: doc 02 section 1.5 - the domain layer only accepts values from a smart enum, so a
 * free text box here would just produce 400s. Kept in one place so the list is not scattered.
 */
export const TRANSACTION_TYPES = ['Deposit', 'Withdrawal', 'Transfer', 'Payment'] as const;

/**
 * At most two decimal places.
 * design: doc 02 section 1.1 - the column is DECIMAL(18,2). This is a courtesy so the user
 * sees the problem before a round trip; the server refuses regardless, and the CHECK
 * constraint refuses after that. Client validation is UX, never security.
 */
export function twoDecimalPlaces(control: AbstractControl): ValidationErrors | null {
  const value = control.value;

  if (value === null || value === '' || value === undefined) {
    return null;
  }

  return /^\d+(\.\d{1,2})?$/.test(String(value)) ? null : { twoDecimalPlaces: true };
}

@Component({
  selector: 'ftms-transaction-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  providers: [TransactionsStore],
  templateUrl: './transaction-form.html',
  styleUrl: './transaction-form.scss',
})
export class TransactionForm implements OnInit {
  private readonly builder = inject(FormBuilder);
  private readonly store = inject(TransactionsStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  protected readonly types = TRANSACTION_TYPES;

  protected readonly id = signal<string | null>(null);
  protected readonly etag = signal<string>('');
  protected readonly status = signal<string>('Active');
  protected readonly saving = signal(false);
  protected readonly loading = signal(false);

  protected readonly isEdit = computed(() => this.id() !== null);

  /**
   * design: doc 05 section 6 - updates are only legal while the transaction is Active or
   * Pending. Showing an editable form for a Completed record and then failing with a 409
   * would be a worse experience than saying so up front.
   */
  protected readonly isEditable = computed(
    () => !this.isEdit() || this.status() === 'Active' || this.status() === 'Pending',
  );

  protected readonly form = this.builder.nonNullable.group({
    transactionDate: ['', Validators.required],
    transactionType: ['Deposit', Validators.required],

    // Amount and currency are create-only. design: doc 05 section 6 - they are not on the
    // update DTO at all, so they cannot even be attempted.
    amount: [null as number | null, [Validators.required, Validators.min(0.01), twoDecimalPlaces]],
    currencyCode: ['ZAR', [Validators.required, Validators.pattern(/^[A-Za-z]{3}$/)]],
  });

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');

    if (!id) {
      // Create: default the date to now, which is what a capturer almost always wants.
      this.form.controls.transactionDate.setValue(toLocalInputValue(new Date()));
      return;
    }

    this.id.set(id);
    this.loading.set(true);

    try {
      const { transaction, etag } = await this.store.loadOne(id);

      this.etag.set(etag);
      this.status.set(transaction.status);

      this.form.patchValue({
        transactionDate: toLocalInputValue(new Date(transaction.transactionDate)),
        transactionType: transaction.transactionType,
        amount: transaction.amount,
        currencyCode: transaction.currencyCode,
      });

      // Only date and type are modifiable on an existing record.
      this.form.controls.amount.disable();
      this.form.controls.currencyCode.disable();

      if (!this.isEditable()) {
        this.form.disable();
      }
    } finally {
      this.loading.set(false);
    }
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);

    try {
      const value = this.form.getRawValue();

      // The input is a local datetime; the API contract is UTC ISO 8601 (doc 05 section 1).
      const transactionDate = new Date(value.transactionDate).toISOString();

      if (this.isEdit()) {
        await this.store.update(this.id()!, this.etag(), {
          transactionDate,
          transactionType: value.transactionType,
        });

        this.toasts.success('Transaction updated');
      } else {
        await this.store.create({
          transactionDate,
          transactionType: value.transactionType,
          amount: Number(value.amount),
          currencyCode: value.currencyCode.toUpperCase(),
        });

        this.toasts.success('Transaction captured');
      }

      await this.router.navigate(['/transactions']);
    } catch (error) {
      this.applyServerErrors(error);
    } finally {
      this.saving.set(false);
    }
  }

  /**
   * Pushes the server's field errors onto the matching controls.
   *
   * design: doc 05 section 1 - a 400 carries an errors dictionary keyed by field name, and the
   * ValidationDecorator camel cases those keys precisely so they line up with what the client
   * sent. That is what makes this loop a few lines rather than a mapping table.
   *
   * 412 and 428 get their own treatment: they are not field problems, they mean the record
   * moved underneath the user, and the only honest fix is to reload.
   */
  private applyServerErrors(error: unknown): void {
    if (!(error instanceof HttpErrorResponse)) {
      return;
    }

    const problem = summariseProblem(error.status, error.error);

    if (error.status === 412 || error.status === 428) {
      this.toasts.error(
        'This transaction changed while you were editing it',
        'Reload the page to see the current values, then reapply your change.',
      );
      return;
    }

    for (const [field, messages] of Object.entries(problem.fieldErrors)) {
      const control = this.form.get(field);

      if (control) {
        control.setErrors({ server: messages.join(' ') });
        control.markAsTouched();
      } else {
        this.toasts.error(problem.message, messages.join(' '));
      }
    }
  }
}

/** Formats a Date for an <input type="datetime-local">, which wants local time, no zone. */
function toLocalInputValue(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0');

  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
    `T${pad(date.getHours())}:${pad(date.getMinutes())}`
  );
}
