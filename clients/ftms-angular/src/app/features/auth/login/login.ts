import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { ToastService } from '../../../core/notifications/toast.service';

/**
 * Sign in.
 *
 * TODO design: doc 06 section 3 - this is a STUB and must not ship. It calls the API's
 * development token endpoint, which verifies nothing and hands out whatever role it is asked
 * for. The real thing is ASP.NET Core Identity self hosted: password plus TOTP MFA where the
 * role requires it, a 15 minute access token, and a rotating one time refresh token in an
 * httpOnly Secure SameSite cookie.
 *
 * The role picker exists precisely so the authorization matrix from doc 06 is exercisable by
 * hand during development: sign in as Auditor and the archive buttons should refuse, sign in
 * as Admin and transactions should be invisible entirely.
 */
@Component({
  selector: 'ftms-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class Login {
  private readonly builder = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  protected readonly roles = ['Capturer', 'Manager', 'Auditor', 'Admin'] as const;
  protected readonly busy = signal(false);

  protected readonly form = this.builder.nonNullable.group({
    userName: ['finance.user', Validators.required],
    role: ['Manager', Validators.required],
  });

  protected async signIn(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);

    try {
      const { userName, role } = this.form.getRawValue();
      await this.auth.signIn(userName, [role]);
      await this.router.navigate(['/transactions']);
    } catch {
      this.toasts.error('Could not sign in', 'Is the API running on http://localhost:5150?');
    } finally {
      this.busy.set(false);
    }
  }
}
