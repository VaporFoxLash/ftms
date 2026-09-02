import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ZardButtonComponent } from '@/shared/components/button/button.component';

/**
 * What the caller hands the dialog when opening it.
 */
export interface ConfirmDialogData {
  readonly title?: string;
  readonly message: string;
  readonly confirmLabel?: string;

  /**
   * Runs when the user confirms. The dialog shows progress until it resolves and then closes;
   * if it rejects the dialog STAYS OPEN so the user can retry without hunting for the row again.
   *
   * The alternative, closing immediately and letting the caller cope, is the more common CDK
   * pattern but it loses that retry affordance, which matters when the failure is a 412 telling
   * the user someone else changed the record.
   */
  readonly onConfirm: () => Promise<void>;
}

/**
 * A confirmation step before an irreversible-looking action.
 *
 * design: doc 02 section 5 - Inactive is the end of the road, and nothing comes back without a
 * deliberate future restore feature.
 *
 * Callers say "delete", which is the word users reach for and the word the API already uses
 * (DELETE /api/transactions/{id}). It is accurate about what the user can no longer do and
 * inaccurate about what the database does, so the burden falls on the MESSAGE: every caller
 * states that the record is kept for audit. Read the title and the message together and both
 * halves are true - which is the most a soft delete behind a Delete button can manage.
 *
 * Built on the CDK Dialog rather than rendered inline. That is not a stylistic preference: the
 * previous hand rolled version declared role="alertdialog" and aria-modal="true" while
 * implementing none of what those roles promise. The CDK supplies the focus trap, focus restore
 * on close, Escape handling, backdrop and scroll blocking, so the markup's claims are true.
 * design: doc 07 section 5 already names the Angular CDK, so this is an existing commitment.
 */
@Component({
  selector: 'ftms-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 class="dialog__title">{{ data.title ?? 'Are you sure?' }}</h2>
    <p class="dialog__body">{{ data.message }}</p>

    <div class="dialog__actions">
      <button z-button zType="outline" type="button" [disabled]="busy()" (click)="cancel()">
        Cancel
      </button>
      <button
        z-button
        type="button"
        class="dialog__confirm"
        [disabled]="busy()"
        (click)="confirm()"
      >
        {{ busy() ? 'Working…' : (data.confirmLabel ?? 'Confirm') }}
      </button>
    </div>
  `,
  imports: [ZardButtonComponent],
  styleUrl: './confirm-dialog.scss',
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmDialogData>(DIALOG_DATA);
  private readonly dialogRef = inject<DialogRef<boolean>>(DialogRef);

  protected readonly busy = signal(false);

  constructor() {
    // Escape and backdrop clicks close through the CDK, which would bypass the busy guard
    // below. Disabling both while work is in flight stops a half finished archive from being
    // dismissed and looking like it did not happen.
    this.dialogRef.disableClose = false;
  }

  protected cancel(): void {
    if (!this.busy()) {
      this.dialogRef.close(false);
    }
  }

  protected async confirm(): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.dialogRef.disableClose = true;

    try {
      await this.data.onConfirm();
      this.dialogRef.close(true);
    } catch {
      // The error interceptor has already surfaced the problem. Stay open so the user can retry.
      this.dialogRef.disableClose = false;
    } finally {
      this.busy.set(false);
    }
  }
}
