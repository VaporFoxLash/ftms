import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { ZardButtonComponent } from '@/shared/components/button/button.component';
import { ZardInputComponent } from '@/shared/components/input/input.component';

/**
 * Sign in.
 *
 * design: doc 06 section 3 - a username and a password, verified against ASP.NET Core Identity.
 *
 * The role picker that used to be on this form is gone, and its absence is the whole point. It
 * existed because the API's development token endpoint minted whatever role it was asked for
 * without checking anything; roles now come from the identity store and the client has no say
 * in them. Exercising the authorization matrix by hand is done by signing in as one of the four
 * seeded accounts instead.
 *
 * Still outstanding from doc 06 section 3: TOTP MFA for the privileged roles.
 */
@Component({
  selector: 'ftms-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, ZardButtonComponent, ZardInputComponent],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class Login {
  private readonly builder = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /**
   * Where to go after signing in, bound from the query string by withComponentInputBinding.
   *
   * authGuard has always put this on the redirect it builds; nothing ever read it, so a user
   * who deep linked to a transaction and was bounced to sign in landed on the list afterwards
   * and had to find their way back.
   */
  readonly returnUrl = input('/transactions');

  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);

  protected readonly form = this.builder.nonNullable.group({
    userName: ['', Validators.required],
    password: ['', Validators.required],
  });

  protected async signIn(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.failure.set(null);

    try {
      const { userName, password } = this.form.getRawValue();
      await this.auth.signIn(userName, password);
      await this.router.navigateByUrl(this.returnUrl());
    } catch (error) {
      // Shown inline rather than as a toast. A failed sign in is about the form the user is
      // looking at, and a message that scrolls away is the wrong shape for something they need
      // while retyping. The error interceptor leaves 401 on the credential endpoints alone for
      // exactly this reason.
      this.failure.set(this.describe(error));
      this.form.controls.password.reset();
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Note that 401 says only "incorrect", never which field was wrong - the server refuses to
   * distinguish an unknown user from a bad password, and repeating that distinction here would
   * hand back the account enumeration oracle the server just closed.
   */
  private describe(error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) {
      return 'Could not reach the server. Is the API running on http://localhost:5150?';
    }

    switch (error.status) {
      case 401:
        return 'That username and password did not match. Please try again.';
      case 423:
        return 'This account is temporarily locked after repeated failed attempts. Try again later.';
      case 429:
        return 'Too many sign in attempts. Wait a few minutes and try again.';
      case 0:
        return 'Could not reach the server. Is the API running on http://localhost:5150?';
      default:
        return 'Something went wrong signing in. Please try again.';
    }
  }
}
