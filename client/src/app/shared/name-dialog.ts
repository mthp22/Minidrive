import { Component, effect, ElementRef, input, output, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiModal } from './modal';

/**
 * Single-field text prompt used for "New folder" and "Rename". The caller owns the open state,
 * passes the initial value, and handles the API call when `submitted` fires with the new name.
 * Inline validation mirrors the server's naming rules for instant feedback; the server remains
 * the authority and its error message is shown via `serverError`.
 */
@Component({
  selector: 'ui-name-dialog',
  imports: [UiModal, FormsModule],
  template: `
    <ui-modal [open]="open()" (closed)="closed.emit()">
      <form (ngSubmit)="submit()">
        <div class="p-6">
          <h2 class="text-base font-semibold">{{ title() }}</h2>

          <div class="mt-4">
            <label
              for="name-dialog-input"
              class="text-sm font-medium text-slate-700 dark:text-slate-300"
            >
              {{ label() }}
            </label>
            <input
              #input
              id="name-dialog-input"
              name="name"
              type="text"
              [(ngModel)]="value"
              [attr.placeholder]="placeholder()"
              maxlength="255"
              autocomplete="off"
              spellcheck="false"
              class="mt-1.5 w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 outline-none transition focus:border-sky-500 focus:ring-2 focus:ring-sky-500/30 dark:border-slate-600 dark:bg-slate-950 dark:text-slate-100"
              [class.border-red-400]="showValidation()"
            />
            @if (showValidation()) {
              <p class="mt-1.5 text-xs text-red-600 dark:text-red-400">{{ validationMessage() }}</p>
            } @else if (serverError()) {
              <p class="mt-1.5 text-xs text-red-600 dark:text-red-400">{{ serverError() }}</p>
            }
          </div>
        </div>

        <div
          class="flex justify-end gap-2 border-t border-slate-200 px-6 py-4 dark:border-slate-700"
        >
          <button
            type="button"
            class="rounded-lg px-3 py-1.5 text-sm font-medium text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800"
            (click)="closed.emit()"
          >
            Cancel
          </button>
          <button
            type="submit"
            class="rounded-lg bg-sky-600 px-3 py-1.5 text-sm font-semibold text-white shadow-sm hover:bg-sky-500 disabled:opacity-50"
            [disabled]="showValidation()"
          >
            {{ submitLabel() }}
          </button>
        </div>
      </form>
    </ui-modal>
  `,
})
export class UiNameDialog {
  readonly open = input(false);
  readonly title = input.required<string>();
  readonly label = input('Name');
  readonly placeholder = input('');
  readonly initialValue = input('');
  readonly serverError = input<string | null>(null);
  readonly submitLabel = input('Save');

  readonly submitted = output<string>();
  readonly closed = output<void>();

  protected value = '';
  private readonly inputEl = viewChild<ElementRef<HTMLInputElement>>('input');

  constructor() {
    // Reset and focus whenever the dialog opens. Waiting one frame ensures the dialog has been
    // painted (showModal is synchronous, but selection needs a laid-out element) so the whole
    // current name is selected and typing replaces it.
    effect(() => {
      if (this.open()) {
        this.value = this.initialValue();
        requestAnimationFrame(() => {
          const element = this.inputEl()?.nativeElement;
          element?.focus();
          element?.select();
        });
      }
    });
  }

  protected showValidation(): boolean {
    return this.value.trim().length === 0;
  }

  protected validationMessage(): string {
    return 'A name is required.';
  }
  protected submit(): void {
    const name = this.value.trim();
    if (name.length === 0) {
      return;
    }
    this.submitted.emit(name);
  }
}
