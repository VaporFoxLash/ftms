import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/**
 * A confirmation step before an irreversible-looking action.
 *
 * design: doc 02 section 5 - Inactive is the end of the road, and nothing comes back without a
 * deliberate future restore feature. So the honest wording here is "archive", not "delete":
 * the record is never destroyed, but the user genuinely cannot undo this from the UI today,
 * and a dialog that pretends otherwise would be lying in both directions.
 */
@Component({
  selector: 'ftms-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="backdrop" (click)="cancelled.emit()">
      <div
        class="dialog"
        role="alertdialog"
        aria-modal="true"
        [attr.aria-label]="title()"
        (click)="$event.stopPropagation()"
      >
        <h2 class="dialog__title">{{ title() }}</h2>
        <p class="dialog__body">{{ message() }}</p>

        <div class="dialog__actions">
          <button type="button" class="dialog__cancel" (click)="cancelled.emit()">Cancel</button>
          <button
            type="button"
            class="dialog__confirm"
            [disabled]="busy()"
            (click)="confirmed.emit()"
          >
            {{ busy() ? 'Working…' : confirmLabel() }}
          </button>
        </div>
      </div>
    </div>
  `,
  styleUrl: './confirm-dialog.scss',
})
export class ConfirmDialog {
  readonly title = input('Are you sure?');
  readonly message = input.required<string>();
  readonly confirmLabel = input('Confirm');
  readonly busy = input(false);

  readonly confirmed = output<void>();
  readonly cancelled = output<void>();
}
