import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, beforeEach, expect, it } from 'vitest';
import { StatusBadge } from './status-badge';

/**
 * design: doc 08 section 5 - test what the user sees rather than component internals. These
 * assertions read the rendered DOM, so they survive any refactor that keeps the output the same.
 */
describe('StatusBadge', () => {
  let fixture: ComponentFixture<StatusBadge>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [StatusBadge] }).compileComponents();
    fixture = TestBed.createComponent(StatusBadge);
  });

  it('shows the status name', () => {
    fixture.componentRef.setInput('status', 'Active');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent.trim()).toBe('Active');
  });

  it.each([
    ['Active', 'badge--active'],
    ['Pending', 'badge--pending'],
    ['Completed', 'badge--completed'],
    ['Cancelled', 'badge--cancelled'],
    ['Inactive', 'badge--inactive'],
  ])('gives %s its own tone so a list is scannable', (status, expectedClass) => {
    fixture.componentRef.setInput('status', status);
    fixture.detectChanges();

    const badge = fixture.nativeElement.querySelector('.badge') as HTMLElement;
    expect(badge.classList.contains(expectedClass)).toBe(true);
  });

  it('does not throw on a status it has never seen', () => {
    // A sixth status arriving from the server should degrade to a visible fallback rather than
    // rendering a blank cell in a financial list.
    fixture.componentRef.setInput('status', 'Reversed');
    fixture.detectChanges();

    const badge = fixture.nativeElement.querySelector('.badge') as HTMLElement;
    expect(badge.classList.contains('badge--unknown')).toBe(true);
    expect(badge.textContent?.trim()).toBe('Reversed');
  });
});
