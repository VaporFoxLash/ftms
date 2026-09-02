import { Dialog, DialogModule } from '@angular/cdk/dialog';
import { Component, inject } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { firstValueFrom } from 'rxjs';
import { ConfirmDialog, ConfirmDialogData } from './confirm-dialog';

/**
 * Host that opens the dialog the way the real caller does, through the CDK Dialog service.
 * Testing the component in isolation would mean supplying DIALOG_DATA and DialogRef by hand and
 * would prove nothing about whether it actually opens.
 */
@Component({ template: '', imports: [DialogModule] })
class DialogHost {
  readonly dialog = inject(Dialog);
}

describe('ConfirmDialog', () => {
  let fixture: ComponentFixture<DialogHost>;
  let host: DialogHost;

  const open = (data: Partial<ConfirmDialogData> & { onConfirm: () => Promise<void> }) =>
    host.dialog.open<boolean>(ConfirmDialog, {
      data: { message: 'Delete this?', ...data } satisfies ConfirmDialogData,

      // Mirrors what TransactionList passes, so the test exercises the real configuration
      // rather than the CDK defaults.
      ariaModal: true,
      autoFocus: 'first-tabbable',
      restoreFocus: true,
    });

  const overlayText = () => document.body.textContent ?? '';

  /**
   * An Escape keydown the CDK will actually act on.
   *
   * The CDK checks `event.keyCode === ESCAPE`, not `event.key`, for legacy browser coverage.
   * keyCode is deprecated and non standard, so KeyboardEvent's constructor ignores it and jsdom
   * will not derive it from `key` — a synthetic event that looks entirely correct is silently
   * ignored. Defining it explicitly is what the CDK's own tests do. The listener is attached to
   * body (overlay keyboard dispatcher), so this needs to bubble.
   */
  const escapeKeydown = (): KeyboardEvent => {
    const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true });
    Object.defineProperty(event, 'keyCode', { get: () => 27 });

    return event;
  };

  const buttonLabelled = (label: string): HTMLButtonElement | undefined =>
    [...document.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === label,
    );

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DialogHost, DialogModule],
    }).compileComponents();
    fixture = TestBed.createComponent(DialogHost);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders the caller supplied message and labels', () => {
    open({
      title: 'Delete this transaction?',
      confirmLabel: 'Delete',
      onConfirm: async () => {},
    });
    fixture.detectChanges();

    expect(overlayText()).toContain('Delete this transaction?');
    expect(overlayText()).toContain('Delete this?');
    expect(buttonLabelled('Delete')).toBeDefined();
  });

  it('runs the handler and closes with true when confirmed', async () => {
    const onConfirm = vi.fn().mockResolvedValue(undefined);
    const ref = open({ confirmLabel: 'Delete', onConfirm });
    fixture.detectChanges();

    const closed = firstValueFrom(ref.closed);
    buttonLabelled('Delete')!.click();
    fixture.detectChanges();

    await expect(closed).resolves.toBe(true);
    expect(onConfirm).toHaveBeenCalledOnce();
  });

  it('stays open when the handler rejects, so the user can retry', async () => {
    // design: doc 05 section 6 - a failed delete is usually a 412 telling the user someone
    // else changed the record. Closing would make them find the row again to try once more.
    const onConfirm = vi.fn().mockRejectedValue(new Error('412'));
    const ref = open({ confirmLabel: 'Delete', onConfirm });
    fixture.detectChanges();

    let closedWith: boolean | undefined | symbol = Symbol('still open');
    ref.closed.subscribe((value) => (closedWith = value));

    buttonLabelled('Delete')!.click();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    expect(typeof closedWith).toBe('symbol');
    expect(buttonLabelled('Delete')).toBeDefined();
  });

  it('shows progress and blocks a second click while the handler is in flight', async () => {
    let release!: () => void;
    const onConfirm = vi.fn().mockReturnValue(
      new Promise<void>((resolve) => {
        release = resolve;
      }),
    );

    open({ confirmLabel: 'Delete', onConfirm });
    fixture.detectChanges();

    buttonLabelled('Delete')!.click();
    await Promise.resolve();
    fixture.detectChanges();

    const working = buttonLabelled('Working…');
    expect(working).toBeDefined();
    expect(working!.disabled).toBe(true);
    expect(buttonLabelled('Cancel')!.disabled).toBe(true);

    // A double click must not delete twice.
    working!.click();
    expect(onConfirm).toHaveBeenCalledOnce();

    release();
  });

  it('closes with false and runs nothing when cancelled', async () => {
    const onConfirm = vi.fn();
    const ref = open({ onConfirm });
    fixture.detectChanges();

    const closed = firstValueFrom(ref.closed);
    buttonLabelled('Cancel')!.click();
    fixture.detectChanges();

    await expect(closed).resolves.toBe(false);
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it('closes on Escape without running the handler', async () => {
    const onConfirm = vi.fn();
    const ref = open({ onConfirm });
    fixture.detectChanges();

    const closed = firstValueFrom(ref.closed);

    document.querySelector('cdk-dialog-container')!.dispatchEvent(escapeKeydown());

    fixture.detectChanges();

    await closed;
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it('refuses to be dismissed by Escape while the handler is in flight', async () => {
    // Ours, not the CDK's. A half finished delete dismissed by a stray Escape would look to
    // the user as though nothing happened, while the write completed underneath.
    let release!: () => void;
    const onConfirm = vi.fn().mockReturnValue(
      new Promise<void>((resolve) => {
        release = resolve;
      }),
    );

    open({ confirmLabel: 'Delete', onConfirm });
    fixture.detectChanges();

    buttonLabelled('Delete')!.click();
    await Promise.resolve();
    fixture.detectChanges();

    document.querySelector('cdk-dialog-container')!.dispatchEvent(escapeKeydown());
    fixture.detectChanges();

    expect(document.querySelector('cdk-dialog-container')).not.toBeNull();

    release();
  });

  it('is announced as a modal dialog', () => {
    // The point of moving to the CDK: these attributes are now backed by real behaviour
    // (focus trap, focus restore, scroll blocking) rather than being a claim the markup made
    // and did not honour. The behaviour itself is verified in the browser, not in jsdom.
    open({ onConfirm: async () => {} });
    fixture.detectChanges();

    const container = document.querySelector('cdk-dialog-container');
    expect(container).not.toBeNull();
    expect(container!.getAttribute('role')).toBe('dialog');
    expect(container!.getAttribute('aria-modal')).toBe('true');
  });
});
