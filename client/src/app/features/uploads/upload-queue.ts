import { Component, computed, inject, signal } from '@angular/core';
import { UploadManager, UploadTask } from '../../core/upload.service';
import { formatBytes } from '../../core/models';
import { UiIcon } from '../../shared/icon';

/**
 * Collapsible drawer showing every upload in flight: progress bars, per-file status, and
 * cancel/retry controls. Appears automatically when something is uploading.
 */
@Component({
  selector: 'app-upload-queue',
  imports: [UiIcon],
  template: `
    @if (visible()) {
      <div
        class="fixed bottom-4 right-4 z-30 w-[min(24rem,calc(100vw-2rem))] overflow-hidden rounded-xl border border-slate-200 bg-white shadow-2xl dark:border-slate-700 dark:bg-slate-900"
      >
        <div
          class="flex items-center gap-2 border-b border-slate-200 px-4 py-3 dark:border-slate-700"
        >
          <ui-icon [size]="16" name="upload" class="text-sky-600" />
          <span class="grow text-sm font-semibold">
            @if (active() > 0) {
              Uploading {{ active() }} file(s)
            } @else {
              Uploads
            }
          </span>
          @if (finished() > 0) {
            <button
              type="button"
              class="rounded px-1.5 py-0.5 text-xs text-slate-500 hover:bg-slate-100 dark:text-slate-400 dark:hover:bg-slate-800"
              (click)="uploads.clearFinished()"
            >
              Clear finished
            </button>
          }
          <button
            type="button"
            class="rounded p-1 text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800"
            (click)="collapsed.set(!collapsed())"
            [attr.aria-label]="collapsed() ? 'Expand uploads' : 'Collapse uploads'"
          >
            <ui-icon [size]="15" [name]="collapsed() ? 'chevron-up' : 'chevron-down'" />
          </button>
        </div>

        @if (!collapsed()) {
          <div class="max-h-72 overflow-y-auto">
            @for (task of uploads.tasks(); track task.id) {
              <div
                class="border-b border-slate-100 px-4 py-2.5 last:border-b-0 dark:border-slate-800"
              >
                <div class="flex items-center gap-2">
                  <span class="min-w-0 grow truncate text-sm" [title]="task.fileName">
                    {{ task.fileName }}
                  </span>
                  <span class="shrink-0 text-xs" [class]="statusClass(task)">
                    {{ statusLabel(task) }}
                  </span>
                  @switch (task.status) {
                    @case ('uploading') {
                      <button
                        type="button"
                        class="rounded p-0.5 text-slate-400 hover:text-red-500"
                        (click)="uploads.cancel(task.id)"
                        [attr.aria-label]="'Cancel ' + task.fileName"
                      >
                        <ui-icon [size]="14" name="x" />
                      </button>
                    }
                    @case ('queued') {
                      <button
                        type="button"
                        class="rounded p-0.5 text-slate-400 hover:text-red-500"
                        (click)="uploads.cancel(task.id)"
                        [attr.aria-label]="'Cancel ' + task.fileName"
                      >
                        <ui-icon [size]="14" name="x" />
                      </button>
                    }
                    @case ('error') {
                      <button
                        type="button"
                        class="rounded p-0.5 text-slate-400 hover:text-sky-600"
                        (click)="uploads.retry(task.id)"
                        title="Retry"
                        [attr.aria-label]="'Retry ' + task.fileName"
                      >
                        <ui-icon [size]="14" name="refresh" />
                      </button>
                      <button
                        type="button"
                        class="rounded p-0.5 text-slate-400 hover:text-red-500"
                        (click)="uploads.dismiss(task.id)"
                        title="Dismiss"
                        [attr.aria-label]="'Dismiss ' + task.fileName"
                      >
                        <ui-icon [size]="14" name="x" />
                      </button>
                    }
                    @case ('canceled') {
                      <button
                        type="button"
                        class="rounded p-0.5 text-slate-400 hover:text-sky-600"
                        (click)="uploads.retry(task.id)"
                        title="Retry"
                        [attr.aria-label]="'Retry ' + task.fileName"
                      >
                        <ui-icon [size]="14" name="refresh" />
                      </button>
                      <button
                        type="button"
                        class="rounded p-0.5 text-slate-400 hover:text-red-500"
                        (click)="uploads.dismiss(task.id)"
                        title="Dismiss"
                        [attr.aria-label]="'Dismiss ' + task.fileName"
                      >
                        <ui-icon [size]="14" name="x" />
                      </button>
                    }
                  }
                </div>

                @if (task.status === 'uploading' || task.status === 'completing') {
                  <div
                    class="mt-1.5 h-1.5 overflow-hidden rounded-full bg-slate-100 dark:bg-slate-800"
                  >
                    <div
                      class="h-full rounded-full bg-sky-500 transition-[width] duration-150"
                      [style.width.%]="task.progress"
                    ></div>
                  </div>
                  <div class="mt-1 text-[11px] tabular-nums text-slate-400">
                    {{ formatBytes(task.uploadedBytes) }} of {{ formatBytes(task.sizeBytes) }}
                  </div>
                } @else if (task.status === 'error' && task.error) {
                  <div class="mt-1 text-[11px] text-red-500 dark:text-red-400">
                    {{ task.error }}
                  </div>
                }
              </div>
            }
          </div>
        }
      </div>
    }
  `,
})
export class UploadQueue {
  protected readonly uploads = inject(UploadManager);
  protected readonly collapsed = signal(false);

  protected readonly formatBytes = formatBytes;

  protected readonly visible = computed(() => this.uploads.tasks().length > 0);

  protected readonly active = computed(
    () =>
      this.uploads
        .tasks()
        .filter(
          (task) =>
            task.status === 'uploading' || task.status === 'completing' || task.status === 'queued',
        ).length,
  );

  protected readonly finished = computed(
    () =>
      this.uploads
        .tasks()
        .filter(
          (task) => task.status === 'done' || task.status === 'error' || task.status === 'canceled',
        ).length,
  );

  protected statusLabel(task: UploadTask): string {
    switch (task.status) {
      case 'queued':
        return 'Waiting…';
      case 'uploading':
        return `${task.progress}%`;
      case 'completing':
        return 'Finalising…';
      case 'done':
        return 'Done';
      case 'error':
        return 'Failed';
      case 'canceled':
        return 'Canceled';
    }
  }

  protected statusClass(task: UploadTask): string {
    switch (task.status) {
      case 'done':
        return 'text-emerald-600 dark:text-emerald-400';
      case 'error':
        return 'text-red-600 dark:text-red-400';
      case 'canceled':
        return 'text-slate-400';
      default:
        return 'text-slate-500 dark:text-slate-400';
    }
  }
}
