import { isPlatformBrowser } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import {
  EnvironmentInjector,
  Injectable,
  computed,
  inject,
  PLATFORM_ID,
  signal,
} from '@angular/core';
import { AuthService as Auth0Service, User } from '@auth0/auth0-angular';
import { firstValueFrom, of, catchError, map } from 'rxjs';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { IssuedToken } from './models';

/**
 * Authentication facade over two modes:
 *
 *  - **Auth0** (when `environment.auth0` is set): the user signs in through Auth0 and an access
 *    token is fetched silently per request. The Auth0 client is resolved lazily so it is never
 *    instantiated in dev mode.
 *  - **Dev** (otherwise): a fixed local user's token is minted at bootstrap — no login screen,
 *    matching the original single-user behaviour.
 *
 * Either way, `getAuthToken()` emits the bearer token to attach to API calls, and the per-user id
 * is always the token's `sub` claim.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly envInjector = inject(EnvironmentInjector);

  private auth0Instance: Auth0Service | null = null;

  /** True when Auth0 is configured; false runs the local dev-token flow. */
  readonly auth0Enabled = environment.auth0 != null;

  /** Fixed local development user every visitor shares (dev mode only). */
  readonly userId = 'dev-user';

  private readonly tokenSignal = signal<string | null>(null);
  private readonly errorSignal = signal<string | null>(null);
  private readonly readySignal = signal(false);
  private readonly userSignal = signal<User | null>(null);

  /** Dev mode: the minted token, or null before bootstrap (or when the API is unreachable). */
  readonly token = this.tokenSignal.asReadonly();
  readonly bootstrapError = this.errorSignal.asReadonly();
  readonly ready = this.readySignal.asReadonly();
  readonly user = this.userSignal.asReadonly();

  readonly isAuthenticated = computed(() =>
    this.auth0Enabled ? this.userSignal() !== null : this.tokenSignal() !== null,
  );

  /** Resolves the Auth0 service only in Auth0 mode, so dev mode never constructs it. */
  private get auth0(): Auth0Service | null {
    if (!this.auth0Enabled) {
      return null;
    }
    if (this.auth0Instance === null) {
      this.auth0Instance = this.envInjector.get(Auth0Service);
    }
    return this.auth0Instance;
  }

  async init(): Promise<void> {
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }

    if (this.auth0Enabled) {
      this.initAuth0();
      return;
    }

    await this.initDevToken();
  }

  /** Emits the current bearer token (or null). Async because Auth0 refreshes tokens silently. */
  getAuthToken(): Observable<string | null> {
    const auth0 = this.auth0;
    if (auth0 === null) {
      return of(this.tokenSignal());
    }
    return auth0.getAccessTokenSilently().pipe(
      map((token) => token ?? null),
      catchError(() => of(null)),
    );
  }

  /** Starts an Auth0 sign-in (popup). No-op in dev mode. */
  login(): void {
    const auth0 = this.auth0;
    if (auth0 === null) {
      return;
    }
    auth0.loginWithPopup().subscribe({
      error: (error: unknown) => {
        this.errorSignal.set('Sign-in was cancelled or blocked. Please try again.');
        console.error('auth0 loginWithPopup failed', error);
      },
    });
  }

  /** Signs out of Auth0 and returns to the app origin. No-op in dev mode. */
  logout(): void {
    const auth0 = this.auth0;
    if (auth0 === null) {
      return;
    }
    const returnTo = typeof window !== 'undefined' ? window.location.origin : '/';
    auth0.logout({ logoutParams: { returnTo } });
  }

  /** Retries the dev-token bootstrap (dev mode only). */
  async retry(): Promise<void> {
    this.readySignal.set(false);
    await this.initDevToken();
  }

  private initAuth0(): void {
    const auth0 = this.auth0;
    if (auth0 === null) {
      return;
    }

    // `provideAuth0` already ran the SDK's init (handling any redirect callback). We only mirror
    // the authentication state into local signals; the first emission marks bootstrap as done so
    // the sign-in screen is not shown before Auth0 finishes its silent check.
    let first = true;
    auth0.isAuthenticated$.subscribe((isAuthenticated) => {
      if (first) {
        this.readySignal.set(true);
        first = false;
      }
    });
    auth0.user$.subscribe((user) => this.userSignal.set(user ?? null));
  }

  private async initDevToken(): Promise<void> {
    if (this.tokenSignal() !== null) {
      this.readySignal.set(true);
      return;
    }

    try {
      const issued = await firstValueFrom(
        this.http.post<IssuedToken>('/api/v1/auth/dev-token', { userId: this.userId }),
      );
      this.tokenSignal.set(issued.accessToken);
      this.errorSignal.set(null);
    } catch (error) {
      this.errorSignal.set(
        'Cannot reach the Minidrive API. Start it with `dotnet run --project backend/src/Minidrive.Api`.',
      );
      console.error('dev-token bootstrap failed', error);
    } finally {
      this.readySignal.set(true);
    }
  }
}
