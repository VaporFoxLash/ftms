import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

/**
 * Page controls driven by the server's paging envelope.
 *
 * design: doc 05 section 3 - the response is a paging envelope so clients never guess whether
 * more data exists. This component consumes exactly that and invents nothing: if the server
 * says one page, there is no next button to press.
 */
@Component({
  selector: 'ftms-paging',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="paging" aria-label="Transaction pages">
      <button
        type="button"
        class="paging__button"
        [disabled]="page() <= 1 || disabled()"
        (click)="pageChange.emit(page() - 1)"
      >
        Previous
      </button>

      <span class="paging__summary" aria-live="polite">
        @if (totalCount() === 0) {
          No transactions
        } @else {
          {{ firstRow() }}–{{ lastRow() }} of {{ totalCount() }}
          <span class="paging__pages">(page {{ page() }} of {{ totalPages() }})</span>
        }
      </span>

      <button
        type="button"
        class="paging__button"
        [disabled]="page() >= totalPages() || disabled()"
        (click)="pageChange.emit(page() + 1)"
      >
        Next
      </button>
    </nav>
  `,
  styleUrl: './paging.scss',
})
export class Paging {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly totalCount = input.required<number>();
  readonly disabled = input(false);

  readonly pageChange = output<number>();

  protected readonly totalPages = computed(() =>
    this.pageSize() <= 0 ? 0 : Math.ceil(this.totalCount() / this.pageSize()),
  );

  protected readonly firstRow = computed(() =>
    this.totalCount() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1,
  );

  protected readonly lastRow = computed(() =>
    Math.min(this.page() * this.pageSize(), this.totalCount()),
  );
}
