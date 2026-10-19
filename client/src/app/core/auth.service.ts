import { isPlatformBrowser } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, PLATFORM_ID, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { IssuedToken } from './models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly platformId = inject(PLATFORM_ID);

  /** fixed local development user every visitor shares. */
  readonly userId = 'dev-user';

  private readonly tokenSignal = signal<string | null>(null);
  private readonly errorSignal = signal<string | null>(null);
  private readonly readySignal = signal(false);

  /** The bearer token, or null before bootstrap (or when the API is unreachable). */
  readonly token = this.tokenSignal.asReadonly();
  readonly bootstrapError = this.errorSignal.asReadonly();
  readonly ready = this.readySignal.asReadonly();
  readonly isAuthenticated = computed(() => this.tokenSignal() !== null);

  async init(): Promise<void> {
    if (!isPlatformBrowser(this.platformId) || this.tokenSignal() !== null) {
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

  async retry(): Promise<void> {
    this.readySignal.set(false);
    await this.init();
  }
}
