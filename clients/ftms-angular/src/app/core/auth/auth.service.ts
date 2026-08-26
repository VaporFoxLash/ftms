import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface DevelopmentTokenResponse {
  readonly accessToken: string;
  readonly tokenType: string;
  readonly expiresInSeconds: number;
}

/**
 * Holds the access token for the session.
 *
 * design: doc 06 section 3 - the Angular SPA keeps the access token IN MEMORY ONLY, never in
 * localStorage, with the refresh token in an httpOnly Secure SameSite cookie so script
 * injection cannot steal it. That is why this is a signal on a root service and not a
 * localStorage read: a token in localStorage is readable by any script that reaches the page.
 *
 * The consequence is deliberate and worth knowing: a page refresh loses the access token, and
 * the app recovers by calling refresh against the httpOnly cookie. Until that endpoint exists,
 * a refresh means logging in again.
 *
 * TODO design: doc 06 section 3 - replace signIn() with the real ASP.NET Core Identity login
 * (password plus TOTP where the role requires it), add silent refresh against the rotating one
 * time refresh token, and delete the development token call below.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly token = signal<string | null>(null);
  private readonly user = signal<string | null>(null);

  readonly accessToken = this.token.asReadonly();
  readonly userName = this.user.asReadonly();
  readonly isAuthenticated = computed(() => this.token() !== null);

  /**
   * TODO design: doc 06 - development only. Calls the API's dev token endpoint, which verifies
   * nothing. It exists so the stack runs end to end before real Identity lands, and it must be
   * deleted with that endpoint.
   */
  async signIn(userName: string, roles: readonly string[]): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<DevelopmentTokenResponse>('/api/dev/token', { userName, roles }),
    );

    this.token.set(response.accessToken);
    this.user.set(userName);
  }

  signOut(): void {
    this.token.set(null);
    this.user.set(null);
  }
}
