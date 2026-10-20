import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from './core/auth.service';
import { ToastService } from './core/toast.service';
import { UploadQueue } from './features/uploads/upload-queue';
import { UiIcon } from './shared/icon';

const THEME_KEY = 'minidrive.theme';

/**
 * Application shell: header (brand, search, API status, theme toggle), the routed page, the
 * upload drawer and the toast stack. Also owns the global search box, which reflects and
 * drives the `/search?q=` route.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, UploadQueue, UiIcon],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly auth = inject(AuthService);
  protected readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  protected readonly searchInput = signal('');
  protected readonly dark = signal(false);
  /** True on the search page; used to seed the header box from the URL. */
  protected readonly onSearchPage = signal(false);

  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  constructor(private readonly destroyRef: DestroyRef) {
    // Theme: stored preference first, else the OS preference.
    if (typeof document !== 'undefined') {
      const stored = localStorage.getItem(THEME_KEY);
      const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
      this.dark.set(stored ? stored === 'dark' : prefersDark);
      this.applyTheme();

      // Keep the header box in sync with the URL (back/forward buttons).
      this.router.events
        .pipe(
          filter((event): event is NavigationEnd => event instanceof NavigationEnd),
          takeUntilDestroyed(this.destroyRef),
        )
        .subscribe((event) => {
          const url = this.router.parseUrl(event.urlAfterRedirects);
          const route = url.root.children['primary']?.segments[0]?.path ?? '';
          this.onSearchPage.set(route === 'search');
          if (route === 'search') {
            this.searchInput.set(url.queryParams['q'] ?? '');
          }
        });
    }
  }

  protected toggleTheme(): void {
    this.dark.update((value) => !value);
    localStorage.setItem(THEME_KEY, this.dark() ? 'dark' : 'light');
    this.applyTheme();
  }

  private applyTheme(): void {
    document.documentElement.classList.toggle('dark', this.dark());
  }

  /** Debounced search-as-you-type; Enter navigates immediately. */
  protected onSearchInput(value: string): void {
    this.searchInput.set(value);
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
    }
    this.searchTimer = setTimeout(() => this.goSearch(value), 300);
  }

  protected onSearchKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      if (this.searchTimer) {
        clearTimeout(this.searchTimer);
      }
      this.goSearch(this.searchInput());
    } else if (event.key === 'Escape') {
      this.searchInput.set('');
      void this.router.navigate(['/files']);
    }
  }

  private goSearch(query: string): void {
    const trimmed = query.trim();
    if (!trimmed) {
      if (this.onSearchPage()) {
        void this.router.navigate(['/files']);
      }
      return;
    }
    void this.router.navigate(['/search'], { queryParams: { q: trimmed } });
  }

  protected retryBootstrap(): void {
    void this.auth.retry();
  }

  /** Starts an Auth0 sign-in (only meaningful when Auth0 is configured). */
  protected signIn(): void {
    this.auth.login();
  }

  /** Signs out of Auth0 and returns to the app origin. */
  protected signOut(): void {
    this.auth.logout();
  }
}
