import { expect, test } from '@playwright/test';

/**
 * The one critical journey.
 *
 * design: doc 08 section 5 - log in, list loads, create a deposit, edit it, soft delete it,
 * verify it left the active list. That is the whole suite on purpose: journeys only.
 *
 * SKIPPED until real authentication lands. The sign in screen today calls the API's
 * development token endpoint, which verifies nothing (doc 06 section 3 TODO). Asserting
 * against a stub would encode the stub's behaviour as the expected behaviour, and the test
 * would have to be rewritten the day real Identity ships rather than simply starting to pass.
 *
 * To enable: remove the .skip, and replace signIn() below with the real login form.
 */
test.describe('capture, edit and archive a transaction', () => {
  test.skip(
    true,
    'Waiting on real authentication. See design doc 06 section 3; the login screen is a stub.',
  );

  test('a capturer can record a deposit and archive it', async ({ page }) => {
    await page.goto('/auth/login');

    // Step 1: sign in.
    await page.getByLabel('User name').fill('e2e.capturer');
    await page.getByLabel('Role').selectOption('Manager');
    await page.getByRole('button', { name: 'Sign in' }).click();

    // Step 2: the list loads, defaulting to Active (doc 05 section 3).
    await expect(page).toHaveURL(/\/transactions$/);
    await expect(page.getByRole('heading', { name: 'Transactions' })).toBeVisible();

    // Step 3: capture a deposit.
    await page.getByRole('link', { name: 'Capture transaction' }).click();
    await page.getByLabel('Type').selectOption('Deposit');
    await page.getByLabel('Amount').fill('1500.00');
    await page.getByLabel('Currency').fill('ZAR');
    await page.getByRole('button', { name: 'Capture' }).click();

    await expect(page).toHaveURL(/\/transactions$/);
    const row = page.getByRole('row').filter({ hasText: 'ZAR 1,500.00' }).first();
    await expect(row).toBeVisible();

    // Step 4: edit it. The client must send the ETag it got from the GET, or the API answers
    // 428 (doc 05 section 6).
    await row.getByRole('link', { name: 'Edit' }).click();
    await page.getByLabel('Type').selectOption('Transfer');
    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(page.getByText('Transaction updated')).toBeVisible();

    // Amount is create-only, so the edit form must not offer it.
    await expect(page.getByLabel('Amount')).toBeDisabled();

    // Step 5: archive it, and confirm.
    const updated = page.getByRole('row').filter({ hasText: 'Transfer' }).first();
    await updated.getByRole('button', { name: 'Archive' }).click();
    await page.getByRole('button', { name: 'Archive', exact: true }).last().click();

    // Step 6: it has left the Active list but still exists under Inactive, because FTMS soft
    // deletes only (doc 05 section 7).
    await expect(page.getByText('No active transactions yet.')).toBeVisible();

    await page.getByLabel('Status').selectOption('Inactive');
    await expect(page.getByRole('row').filter({ hasText: 'Transfer' }).first()).toBeVisible();
  });
});
