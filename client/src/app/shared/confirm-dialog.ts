import { Component, input, output } from '@angular/core';
import { UiIcon } from './icon';
import { UiModal } from './modal';

/**
 * Confirmation dialog for destructive actions. The caller owns the open state and performs the
 * action when `confirmed` fires; this component never calls the API itself.
 */
@Component({
  selector: 'ui-confirm-dialog',
  imports: [UiModal, UiIcon],
  template: `
    <ui-modal [open]="open()" (closed)="closed.emit()">
      <div class="flex gap-4 p-6">
        <div
          class="flex h-10 w-10 flex-none items-center justify-center rounded-full"
          [class]="
            tone() === 'danger'
              ? 'bg-red-100 text-red-600 dark:bg-red-950 dark:text-red-400'
              : 'bg-amber-100 text-amber-600 dark:bg-amber-950 dark:text-amber-400'
          "
        >
          <ui-icon [name]="tone() === 'danger' ? 'alert' : 'info'" [size]="20" />
        </div>
        <div class="min-w-0">
          <h2 class="text-base font-semibold">{{ title() }}</h2>
          <p class="mt-1 text-sm text-slate-600 dark:text-slate-400">{{ message() }}</p>
        </div>
      </div>
      <div class="flex justify-end gap-2 border-t border-slate-200 px-6 py-4 dark:border-slate-700">
        <button
          type="button"
          class="rounded-lg px-3 py-1.5 text-sm font-medium text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800"
          (click)="closed.emit()"
        >
          {{ cancelLabel() }}
        </button>
        <button
          type="button"
          class="rounded-lg px-3 py-1.5 text-sm font-semibold text-white shadow-sm"
          [class]="
            tone() === 'danger' ? 'bg-red-600 hover:bg-red-500' : 'bg-amber-600 hover:bg-amber-500'
          "
          (click)="confirmed.emit()"
        >
          {{ confirmLabel() }}
        </button>
      </div>
    </ui-modal>
  `,
})
export class UiConfirmDialog {
  readonly open = input(false);
  readonly title = input.required<string>();
  readonly message = input.required<string>();
  readonly tone = input<'danger' | 'warning'>('danger');
  readonly confirmLabel = input('Delete');
  readonly cancelLabel = input('Cancel');

  readonly confirmed = output<void>();
  readonly closed = output<void>();
}
