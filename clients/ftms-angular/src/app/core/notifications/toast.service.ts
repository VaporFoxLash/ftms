import { LiveAnnouncer } from '@angular/cdk/a11y';
import { Injectable, inject, signal } from '@angular/core';

export type ToastLevel = 'info' | 'success' | 'error';

export interface Toast {
  readonly id: number;
  readonly level: ToastLevel;
  readonly message: string;
  readonly detail?: string;
}

/**
 * The one place user-facing messages are collected.
 *
 * design: doc 05 section 1 - every failure response has the same ProblemDetails shape, so both
 * clients write ONE error handler. On the Angular side that handler is the error interceptor,
 * and this is where it puts what it found.
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly announcer = inject(LiveAnnouncer);

  private nextId = 1;
  private readonly items = signal<readonly Toast[]>([]);

  /** Read-only view for templates. */
  readonly toasts = this.items.asReadonly();

  info(message: string, detail?: string): void {
    this.push('info', message, detail);
  }

  success(message: string, detail?: string): void {
    this.push('success', message, detail);
  }

  error(message: string, detail?: string): void {
    this.push('error', message, detail);
  }

  dismiss(id: number): void {
    this.items.update((current) => current.filter((toast) => toast.id !== id));
  }

  clear(): void {
    this.items.set([]);
  }

  private push(level: ToastLevel, message: string, detail?: string): void {
    const toast: Toast = { id: this.nextId++, level, message, detail };
    this.items.update((current) => [...current, toast]);

    // Announcement goes through the CDK LiveAnnouncer rather than an aria-live region in the
    // template. The template markup carries NO aria-live and NO role="status" precisely because
    // of this: with both in place a screen reader announces every toast twice.
    //
    // Errors interrupt, everything else waits its turn. A failed archive is worth cutting into
    // whatever is being read; "Transaction captured" is not.
    void this.announcer.announce(
      detail ? `${message}. ${detail}` : message,
      level === 'error' ? 'assertive' : 'polite',
    );
  }
}
