import { Component, effect, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { ApiError, NodeSummary, formatBytes, formatTimestamp } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { UiIcon } from '../../shared/icon';

/**
 * Search results page. The query lives in the URL (`/search?q=`) so results are shareable and
 * the back button works; the header box updates it through the router. Each result shows where
 * it lives and can be opened (folders navigate, files download).
 */
@Component({
  selector: 'app-search',
  imports: [UiIcon],
  template: `
    <div class="px-4 py-4">
      <h1 class="text-sm font-semibold text-slate-500 dark:text-slate-400">
        Results for “{{ query() }}”
        @if (!loading() && !error()) {
          <span class="font-normal">— {{ results().length }} found</span>
        }
      </h1>

      @if (loading()) {
        @for (row of [1, 2, 3]; track row) {
          <div class="mt-3 flex animate-pulse items-center gap-3 rounded-lg px-3 py-3">
            <div class="h-5 w-5 rounded bg-slate-200 dark:bg-slate-800"></div>
            <div class="h-4 flex-1 rounded bg-slate-200 dark:bg-slate-800"></div>
          </div>
        }
      } @else if (error()) {
        <div
          class="mx-auto mt-12 max-w-md rounded-xl border border-red-200 bg-red-50 p-6 text-center dark:border-red-900 dark:bg-red-950/40"
        >
          <ui-icon [size]="26" name="alert" class="mx-auto text-red-500" />
          <p class="mt-3 text-sm text-red-800 dark:text-red-300">{{ error() }}</p>
        </div>
      } @else if (results().length === 0) {
        <div class="mx-auto mt-12 max-w-md text-center">
          <ui-icon [size]="36" name="search" class="mx-auto text-slate-300 dark:text-slate-700" />
          <p class="mt-4 text-sm text-slate-500 dark:text-slate-400">
            Nothing matches “{{ query() }}”.
          </p>
        </div>
      } @else {
        <div class="mt-3">
          @for (item of results(); track item.id) {
            <button
              type="button"
              class="flex w-full items-center gap-3 rounded-lg px-3 py-2.5 text-left hover:bg-slate-100 dark:hover:bg-slate-800/60"
              (click)="open(item)"
            >
              <ui-icon
                [size]="18"
                [name]="item.type === 'Folder' ? 'folder' : 'file'"
                [class]="item.type === 'Folder' ? 'text-amber-500' : 'text-sky-500'"
              />
              <span class="min-w-0 grow">
                <span class="block truncate text-sm">{{ item.name }}</span>
                <span class="block truncate text-xs text-slate-400 dark:text-slate-500">
                  {{ locationOf(item) }}
                </span>
              </span>
              <span
                class="w-20 shrink-0 text-right text-xs tabular-nums text-slate-500 dark:text-slate-400"
              >
                {{ item.type === 'File' ? formatBytes(item.sizeBytes) : '—' }}
              </span>
              <span
                class="hidden w-36 shrink-0 text-right text-xs tabular-nums text-slate-500 dark:text-slate-400 sm:block"
              >
                {{ formatTimestamp(item.updatedAtUtc, item.type) }}
              </span>
            </button>
          }
        </div>
      }
    </div>
  `,
})
export class Search {
  /** Bound from the `q` query parameter. */
  readonly query = input<string>('', { alias: 'q' });

  protected readonly results = signal<NodeSummary[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly formatBytes = formatBytes;
  protected readonly formatTimestamp = formatTimestamp;

  private readonly parentNames = new Map<string, string>();

  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  constructor() {
    effect(() => {
      const query = this.query().trim();
      void this.run(query);
    });
  }

  private async run(query: string): Promise<void> {
    if (!query) {
      this.results.set([]);
      this.loading.set(false);
      this.error.set(null);
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    try {
      const items = await this.api.search(query);
      this.results.set(items);
      void this.resolveLocations(items);
    } catch (error) {
      this.results.set([]);
      this.error.set(
        error instanceof ApiError && error.status === 400
          ? 'Type at least one character to search.'
          : 'Search is unavailable right now.',
      );
    } finally {
      this.loading.set(false);
    }
  }

  protected open(item: NodeSummary): void {
    if (item.type === 'Folder') {
      void this.router.navigate(['/files', item.id]);
      return;
    }
    void this.download(item);
  }

  private async download(item: NodeSummary): Promise<void> {
    try {
      const download = await this.api.createDownloadUrl(item.id);
      window.location.assign(download.url);
    } catch (error) {
      this.toast.error(
        error instanceof ApiError ? error.message : 'Could not create a download link.',
      );
    }
  }

  protected locationOf(item: NodeSummary): string {
    if (!item.parentId) {
      return 'All files';
    }
    const name = this.parentNames.get(item.parentId);
    return name ? `In ${name}` : 'In a folder';
  }

  private async resolveLocations(items: readonly NodeSummary[]): Promise<void> {
    const ids = [
      ...new Set(items.map((item) => item.parentId).filter((id): id is string => !!id)),
    ].filter((id) => !this.parentNames.has(id));

    await Promise.all(
      ids.map(async (id) => {
        try {
          const node = await this.api.getNode(id);
          this.parentNames.set(id, node.name);
        } catch {
        }
      }),
    );
  }
}
