import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth/auth.service';
import { ToastService } from './core/notifications/toast.service';

@Component({
  selector: 'ftms-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly router = inject(Router);

  protected readonly auth = inject(AuthService);
  protected readonly toasts = inject(ToastService);

  /**
   * Set when public/g4s-logo.png fails to load, which swaps the header back to the text wordmark.
   *
   * The header is the one piece of chrome on every screen, so it must not depend on an asset
   * being present. A missing file gives a broken-image icon on every route and in every
   * screenshot; this degrades to the lockup the app shipped with instead.
   */
  protected readonly logoUnavailable = signal(false);

  protected async signOut(): Promise<void> {
    await this.auth.signOut();
    await this.router.navigate(['/auth/login']);
  }
}
