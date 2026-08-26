import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * Shows a transaction status with a colour that carries meaning.
 *
 * The colours follow the doc 02 state machine rather than taste: working states (Active,
 * Pending) read as live, terminal outcomes (Completed, Cancelled) as settled, and Inactive as
 * archived. A finance user scanning a list should be able to tell "still moving" from
 * "finished" without reading a word.
 */
@Component({
  selector: 'ftms-status-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="badge" [class]="'badge--' + tone()">{{ status() }}</span>`,
  styleUrl: './status-badge.scss',
})
export class StatusBadge {
  readonly status = input.required<string>();

  protected readonly tone = computed(() => {
    switch (this.status()) {
      case 'Active':
        return 'active';
      case 'Pending':
        return 'pending';
      case 'Completed':
        return 'completed';
      case 'Cancelled':
        return 'cancelled';
      case 'Inactive':
        return 'inactive';
      default:
        return 'unknown';
    }
  });
}
