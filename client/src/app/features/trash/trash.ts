import { Component, OnInit, inject, signal } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { ApiError, TrashItem, formatBytes } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { UiIcon } from '../../shared/icon';
import { UiConfirmDialog } from '../../shared/confirm-dialog';

/**
 * Trash view: lists soft-deleted items with the time they will be permanently purged, and offers
 * Restore, Delete-forever per item, plus an Empty-trash action. Restoring puts an item back into
 * the drive; deleting is permanent and confirmed.
 */
@Component({
  selector: 'app-trash',
  imports: [UiIcon, UiConfirmDialog],
  template: `
    <div class="px-4 py-4">
      <div class="flex items-center justify-between gap-3">
        <h1 class="text-sm font-semibold text-slate-500 dark:text-slate-400">
          Trash
          @if (!loading() && !error()) {
            <span class="font-normal">— {{ items().length }} item(s)</span>
          }
        </h1>
        @if (items().length > 0) {
          <button
            type="button"
            class="rounded-lg border border-red-200 px-3 py-1.5 text-xs font-semibold text-red-600 hover:bg-red-50 disabled:opacity-50 dark:border-red-900 dark:text-red-400 dark:hover:bg-red-950/50"
            [disabled]="busy() !== null"
            (click)="askEmpty()"
          >
            Empty trash
          </button>
        }
      </div>

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
      } @else if (items().length === 0) {
        <div class="mx-auto mt-12 max-w-md text-center">
          <ui-icon [size]="36" name="trash" class="mx-auto text-slate-300 dark:text-slate-700" />
          <p class="mt-4 text-sm text-slate-500 dark:text-slate-400">
            The trash is empty. Deleted items appear here and can be restored for a week.
          </p>
        </div>
      } @else {
        <p class="mt-2 text-xs text-slate-400 dark:text-slate-500">
          Items are permanently deleted after 7 days.
        </p>
        <div class="mt-3">
          @for (item of items(); track item.id) {
            <div
              class="flex items-center gap-3 rounded-lg px-3 py-2.5 hover:bg-slate-100 dark:hover:bg-slate-800/60"
            >
              <ui-icon
                [size]="18"
                [name]="item.type === 'Folder' ? 'folder' : 'file'"
                [class]="item.type === 'Folder' ? 'text-amber-500' : 'text-sky-500'"
              />
              <span class="min-w-0 grow">
                <span class="block truncate text-sm">{{ item.name }}</span>
                <span class="block truncate text-xs text-slate-400 dark:text-slate-500">
                  Deleted {{ when(item) }} · purges {{ remaining(item) }}
                </span>
              </span>
              <span
                class="w-20 shrink-0 text-right text-xs tabular-nums text-slate-500 dark:text-slate-400"
              >
                {{ item.type === 'File' ? formatBytes(item.sizeBytes) : '—' }}
              </span>
              <span class="flex flex-none items-center gap-1.5">
                <button
                  type="button"
                  class="rounded-lg px-2.5 py-1.5 text-xs font-semibold text-sky-600 hover:bg-sky-50 disabled:opacity-50 dark:text-sky-400 dark:hover:bg-sky-950/50"
                  [disabled]="busy() !== null"
                  (click)="restore(item)"
                >
                  Restore
                </button>
                <button
                  type="button"
                  class="rounded-lg px-2 py-1.5 text-slate-400 hover:bg-red-50 hover:text-red-600 disabled:opacity-50 dark:hover:bg-red-950/50 dark:hover:text-red-400"
                  [disabled]="busy() !== null"
                  (click)="askPurge(item)"
                  [attr.aria-label]="'Delete ' + item.name + ' forever'"
                >
                  <ui-icon [size]="16" name="trash" />
                </button>
              </span>
            </div>
          }
        </div>
      }

      <ui-confirm-dialog
        [open]="confirmOpen()"
        [title]="confirmTitle()"
        [message]="confirmMessage()"
        [confirmLabel]="confirmLabel()"
        tone="danger"
        (confirmed)="onConfirm()"
        (closed)="onCancel()"
      />
    </div>
  `,
})
export class Trash implements OnInit {
  protected readonly items = signal<TrashItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  /** Id of the item being acted on, or a sentinel while a bulk action runs; null when idle. */
  protected readonly busy = signal<string | null>(null);

  protected readonly confirmOpen = signal(false);
  protected readonly confirmTitle = signal('');
  protected readonly confirmMessage = signal('');
  protected readonly confirmLabel = signal('Delete');
  private pendingAction: (() => Promise<void>) | null = null;

  protected readonly formatBytes = formatBytes;

  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  ngOnInit(): void {
    void this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.items.set(await this.api.listTrash());
    } catch {
      this.items.set([]);
      this.error.set('Could not load the trash right now.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async restore(item: TrashItem): Promise<void> {
    this.busy.set(item.id);
    try {
      const restored = await this.api.restoreNode(item.id);
      this.toast.success(`Restored “${restored.name}”.`);
      await this.load();
    } catch (e) {
      this.toast.error(e instanceof ApiError ? e.message : 'Could not restore the item.');
    } finally {
      this.busy.set(null);
    }
  }

  protected askPurge(item: TrashItem): void {
    this.confirmTitle.set(`Delete “${item.name}” forever?`);
    this.confirmMessage.set(
      'This permanently removes the item and its files. It cannot be undone.',
    );
    this.confirmLabel.set('Delete forever');
    this.pendingAction = async () => {
      await this.api.purgeNode(item.id);
      this.toast.success(`Deleted “${item.name}” forever.`);
      await this.load();
    };
    this.confirmOpen.set(true);
  }

  protected askEmpty(): void {
    this.confirmTitle.set('Empty the trash?');
    this.confirmMessage.set(
      `Permanently deletes all ${this.items().length} item(s). This cannot be undone.`,
    );
    this.confirmLabel.set('Empty trash');
    this.pendingAction = async () => {
      await this.api.emptyTrash();
      this.toast.success('Trash emptied.');
      await this.load();
    };
    this.confirmOpen.set(true);
  }

  protected async onConfirm(): Promise<void> {
    this.confirmOpen.set(false);
    const action = this.pendingAction;
    this.pendingAction = null;
    if (!action) {
      return;
    }
    this.busy.set('confirm');
    try {
      await action();
    } catch (e) {
      this.toast.error(e instanceof ApiError ? e.message : 'The action failed.');
    } finally {
      this.busy.set(null);
    }
  }

  protected onCancel(): void {
    this.confirmOpen.set(false);
    this.pendingAction = null;
  }

  protected when(item: TrashItem): string {
    return new Date(item.deletedAtUtc).toLocaleDateString(undefined, {
      month: 'short',
      day: 'numeric',
    });
  }

  protected remaining(item: TrashItem): string {
    const ms = new Date(item.purgeAfterUtc).getTime() - Date.now();
    if (ms <= 0) {
      return 'soon';
    }
    const days = Math.ceil(ms / 86_400_000);
    return days === 1 ? 'in 1 day' : `in ${days} days`;
  }
}
