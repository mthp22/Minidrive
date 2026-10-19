import { Component, effect, ElementRef, input, output, viewChild } from '@angular/core';

@Component({
  selector: 'ui-modal',
  template: `
    <dialog
      #dialog
      class="m-auto w-[min(28rem,calc(100vw-2rem))] rounded-xl border border-slate-200 bg-white p-0 text-slate-900 shadow-2xl dark:border-slate-700 dark:bg-slate-900 dark:text-slate-100"
      (click)="onBackdrop($event)"
      (cancel)="closed.emit()"
    >
      <ng-content />
    </dialog>
  `,
  styles: `
    dialog::backdrop {
      background: rgb(15 23 42 / 0.5);
    }
  `,
})
export class UiModal {
  readonly open = input(false);
  readonly closed = output<void>();

  private readonly dialog = viewChild<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    effect(() => {
      const element = this.dialog()?.nativeElement;
      if (!element) {
        return;
      }
      if (this.open() && !element.open) {
        element.showModal();
      } else if (!this.open() && element.open) {
        element.close();
      }
    });
  }

  protected onBackdrop(event: MouseEvent): void {
    if (event.target === this.dialog()?.nativeElement) {
      this.closed.emit();
    }
  }
}