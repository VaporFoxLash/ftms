import { Page, expect, test } from '@playwright/test';

/**
 * The one critical journey.
 *
 * design: doc 08 section 5 - log in, list loads, create a deposit, edit it, soft delete it,
 * verify it left the active list. That is the whole suite on purpose: journeys only.
 *
 * This used to be `test.skip(true, ...)`, waiting on real authentication, and it was skipped for
 * long enough that its selectors rotted underneath it: it still called selectOption() on
 * controls that had since become ZardUI comboboxes, so removing the skip would have failed
 * immediately. Both are fixed here - the login is real, and the selectors match the controls
 * that actually render.
 *
 * Needs the API running with the Development seed accounts:
 *   dotnet run --project src/FTMS.Api
 */

/** The seeded Manager account. Manager, because step 5 deletes, and delete is Manager only. */
const USER = 'manager';
const PASSWORD = 'Manager#2026';

/**
 * Picks a value from a ZardUI select.
 *
 * These are NOT native <select> elements - they are a button[role="combobox"] that opens a CDK
 * overlay listbox - so Playwright's selectOption() does not apply. Opening the popup and
 * clicking the option is also closer to what a user does, and it exercises the overlay that a
 * native select would have bypassed.
 */
async function choose(page: Page, label: string, value: string): Promise<void> {
  await page.getByRole('combobox', { name: label }).click();
  await page.getByRole('option', { name: value, exact: true }).click();
}

async function signIn(page: Page): Promise<void> {
  await page.goto('/auth/login');
  await page.getByLabel('User name').fill(USER);
  await page.getByLabel('Password').fill(PASSWORD);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

test.describe('capture, edit and delete a transaction', () => {
  test('a manager can record a deposit, edit it and delete it', async ({ page }) => {
    // A unique amount per run, so the row this test creates can be told apart from the seed data
    // and from a previous run's leftovers - the API soft deletes, so nothing ever disappears.
    const cents = Date.now() % 100;
    const amount = `1500.${cents.toString().padStart(2, '0')}`;
    const formatted = `ZAR 1,500.${cents.toString().padStart(2, '0')}`;

    // Step 1: sign in, for real, against a hashed password.
    await signIn(page);

    // Step 2: the list loads, defaulting to Active (doc 05 section 3).
    await expect(page).toHaveURL(/\/transactions$/);
    await expect(page.getByRole('heading', { name: 'Transactions' })).toBeVisible();

    // Step 3: capture a deposit.
    await page.getByRole('link', { name: 'Capture transaction' }).click();
    await choose(page, 'Type', 'Deposit');
    await page.getByLabel('Amount').fill(amount);
    await page.getByRole('button', { name: 'Capture' }).click();

    await expect(page).toHaveURL(/\/transactions$/);
    const row = page.getByRole('row').filter({ hasText: formatted }).first();
    await expect(row).toBeVisible();

    // The brief asks the grid to show the Id. It is truncated to its last segment - these are
    // GUIDv7, whose leading characters are a timestamp and therefore identical across rows
    // captured in the same minute - with the full value on the button's accessible name.
    await expect(row.getByRole('button', { name: /^Copy full id [0-9a-f-]{36}$/ })).toBeVisible();

    // Step 4: edit it. The client sends the ETag it got from the GET, so this is a
    // compare-and-swap rather than a blind overwrite (doc 05 section 6).
    await row.getByRole('link', { name: 'Edit' }).click();
    await choose(page, 'Type', 'Transfer');

    // Amount is create-only, so the edit form must not offer it.
    await expect(page.getByLabel('Amount')).toBeDisabled();

    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(page).toHaveURL(/\/transactions$/);

    // Step 5: delete it, and confirm.
    const updated = page.getByRole('row').filter({ hasText: formatted }).first();
    await expect(updated).toContainText('Transfer');
    await updated.getByRole('button', { name: 'Delete' }).click();

    // Scoped to the dialog. An unscoped "last Delete button on the page" also matches the row
    // buttons still rendered behind the CDK overlay, and Playwright then waits forever for a
    // backdrop that is deliberately intercepting pointer events to stop covering them.
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await dialog.getByRole('button', { name: 'Delete', exact: true }).click();

    // Step 6: it has left the Active list but still exists under Inactive, because FTMS soft
    // deletes only (doc 05 section 7). This is the assertion the whole journey exists for.
    await expect(page.getByRole('row').filter({ hasText: formatted })).toHaveCount(0);

    await choose(page, 'Status', 'Inactive');
    await expect(page.getByRole('row').filter({ hasText: formatted }).first()).toBeVisible();
  });

  test('a page reload keeps the session', async ({ page }) => {
    // The access token is held in memory only and is genuinely lost on reload; the httpOnly
    // refresh cookie is not, and the guard trades it for a new token before the route resolves.
    // Worth its own test because the failure mode - being thrown back to the login screen
    // mid-task - is exactly what this design was chosen to avoid.
    await signIn(page);
    await expect(page).toHaveURL(/\/transactions$/);

    await page.reload();

    await expect(page).toHaveURL(/\/transactions$/);
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible();
  });

  test('an unauthenticated deep link returns you to where you were going', async ({ page }) => {
    await page.goto('/transactions');

    await expect(page).toHaveURL(/\/auth\/login\?returnUrl=%2Ftransactions$/);

    await page.getByLabel('User name').fill(USER);
    await page.getByLabel('Password').fill(PASSWORD);
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page).toHaveURL(/\/transactions$/);
  });

  test('a failed sign in is reported on the form, not as a toast', async ({ page }) => {
    // Deliberately an account that does not exist, rather than USER with a bad password.
    //
    // Identity locks an account after five failed attempts, and this suite runs in parallel and
    // gets re-run - so pointing failures at the same account the other three tests sign in with
    // locks them all out, which is exactly what happened the first time. Using an unknown
    // username tests the identical code path: the server refuses to distinguish "no such user"
    // from "wrong password", so both produce a byte-identical 401 (see doc 06 section 3), and
    // nothing has a lockout counter to increment.
    await page.goto('/auth/login');
    await page.getByLabel('User name').fill('nobody.at.all');
    await page.getByLabel('Password').fill('DefinitelyNotThePassword1');
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page.getByRole('alert')).toContainText('did not match');
    await expect(page).toHaveURL(/\/auth\/login/);
  });
});
