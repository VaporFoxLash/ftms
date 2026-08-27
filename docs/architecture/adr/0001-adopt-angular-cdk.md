# 0001. Adopt @angular/cdk for dialog, announcements and virtual scrolling

- **Status:** Accepted
- **Date:** 2026-08-27
- **Deciders:** Brody
- **Relates to:** doc 07 §5 (Angular client), doc 08 §5 (client testing), doc 09 (client decision)

## Context

The confirm dialog shipped in the Angular skeleton declared `role="alertdialog"` and
`aria-modal="true"` and implemented none of what those roles promise:

| Promised by the markup | Actually implemented |
| --- | --- |
| Focus trapped inside the dialog | No — Tab walked into the page behind |
| Focus restored on close | No |
| Escape dismisses | No |
| Background scroll locked | No |
| Initial focus placed in the dialog | No |
| Backdrop | A `div` with a click handler |

A dialog that announces modality and then does not behave modally is worse than one that never
claimed the role: a screen reader user is told they are in a modal, and then finds themselves
reading the page behind it with no way back.

Separately, doc 07 §5 already commits to the CDK — "CDK virtual scrolling if a user ever holds
more than a couple hundred rows on screen (paging should prevent it, virtualisation is the
seatbelt)" — and `pageSize` caps at 200, so that condition is reachable at the largest page
size. And `ToastService` announced through a hand-written `aria-live` region, which works but is
inconsistently honoured across screen readers.

The prompt for revisiting this was an evaluation of Angular UI libraries generally.

## Options

**A. Keep hand-rolling.** Add `cdkTrapFocus` or a bespoke focus trap, Escape handling, focus
restore and scroll lock by hand. No new dependency, but it reimplements — badly, and with our
own bugs — what a maintained library already does. Rejected.

**B. Adopt `@angular/cdk` only.** Behaviour primitives, no components, no opinions about
appearance. Tree-shakeable, MIT, maintained by the Angular team, already named in doc 07 §5.
The existing hand-written SCSS survives untouched. **Chosen.**

**C. Adopt a full component library** (Angular Material, PrimeNG, Kendo, Syncfusion). Rejected
on three counts:

- **No design driver.** The screens are a paged list, two forms and a detail view. They exist
  and work. A component library would mean discarding working SCSS to gain components we have
  already built.
- **Bundle budget.** The initial bundle was 291 kB against a 400 kB warning (doc 07 §5 puts
  budgets in `angular.json` so bloat fails the build). Material or PrimeNG with a data grid
  would consume most of the remaining headroom for no functional gain.
- **Licensing.** Kendo is $999/developer and Syncfusion $395/month. This project has twice
  refused a dependency on licensing grounds — MediatR (doc 03 §3) and FluentAssertions
  (doc 08 §2) — and there is no reason to make an exception for something we do not need.

## Decision

Adopt `@angular/cdk` (22.1.4, MIT) for three specific behaviours, and no component library.

1. **`@angular/cdk/dialog`** replaces the hand-rolled `ConfirmDialog` shell. One change closes
   all six defects above, and `ariaModal: true` is now set explicitly rather than left to the
   CDK default of `false`, because with a focus trap and scroll blocking in place the claim is
   finally accurate.
2. **`@angular/cdk/a11y` `LiveAnnouncer`** replaces the `aria-live` region for toasts, with
   `assertive` for errors and `polite` for everything else. The `aria-live` attribute and
   `role="status"` were removed from `app.html` in the same change — leaving both in place makes
   screen readers announce every toast twice.
3. **`@angular/cdk/scrolling`** virtualises the transaction list, per doc 07 §5.

## Consequences

**The list is no longer a `<table>.`** `cdk-virtual-scroll-viewport` must be the scrolling
container, and table layout does not survive the `display: block` that requires — columns stop
aligning. The rows are now a CSS grid carrying explicit ARIA roles (`role="table"`, `"row"`,
`"columnheader"`, `"cell"`), which preserves the semantics a screen reader needs and the
`getByRole('row')` selectors in the Playwright journey. This is the largest and most reversible-
looking part of the change, and the one to review first if the list ever looks wrong.

Row height is now load-bearing. `ROW_HEIGHT_PX` in `transaction-list.ts` feeds `[itemSize]`, and
the SCSS `$row-height` must match it, or rows overlap and gaps open mid-scroll. Both carry a
comment saying so.

**Bundle cost, measured:** initial 291.69 kB → 335.83 kB raw (80.6 → 91.5 kB transfer), against
a 400 kB warning. Dialog and scrolling landed in the lazy `transaction-list` chunk as intended
(13 kB → 75 kB); the growth in `main` is `LiveAnnouncer`, which is reachable from the root
component through the root-provided `ToastService`. Headroom is now roughly 64 kB. The budget
was **not** raised to accommodate this.

**Now available, deliberately not yet used:** `cdk/overlay` for tooltips and menus, `cdk/table`,
`cdk/drag-drop`, and the rest of `cdk/a11y`. Adopting the CDK does not oblige us to use it; the
same "prepared seam, no premature implementation" position doc 03 §9 takes elsewhere applies.

**Focus-trap and focus-restore behaviour is not asserted in unit tests.** jsdom does not model
focus faithfully enough for those assertions to mean anything. They are verified in a real
browser instead, and the dialog spec covers what jsdom can honestly check: the handler contract,
the busy guard, Escape, and the ARIA attributes.
