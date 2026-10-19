import { Component, effect, inject, input, output, signal } from '@angular/core';
import { ApiService } from '../core/api.service';
import { UiIcon } from './icon';
import { UiModal } from './modal';

interface TreeNode {
  id: string;
  name: string;
  level: number;
  expanded: boolean;
  loading: boolean;
  children: TreeNode[] | null;
}

@Component({
  selector: 'ui-move-dialog',
  imports: [UiModal, UiIcon],
  template: `
    <ui-modal [open]="open()" (closed)="closed.emit()">
      <div
        class="flex items-center justify-between border-b border-slate-200 px-6 py-4 dark:border-slate-700"
      >
        <h2 class="text-base font-semibold">Move {{ count() }} item(s) to…</h2>
        <button
          type="button"
          class="rounded-lg p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600 dark:hover:bg-slate-800"
          (click)="closed.emit()"
          aria-label="Close"
        >
          <ui-icon [size]="18" name="x" />
        </button>
      </div>

      <div class="max-h-[50vh] overflow-y-auto px-3 py-2">
        <button
          type="button"
          class="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-sm"
          [class.bg-sky-50]="destination() === null"
          [class.font-medium]="destination() === null"
          [class.text-sky-700]="destination() === null"
          [class.dark:bg-sky-950]="destination() === null"
          [class.dark:text-sky-300]="destination() === null"
          [class.text-slate-700]="destination() !== null"
          [class.dark:text-slate-300]="destination() !== null"
          [class.hover]="destination() !== null"
          (click)="destination.set(null)"
        >
          <ui-icon [size]="16" name="home" />
          All files
        </button>

        @for (node of tree(); track node.id) {
          <button
            type="button"
            class="flex w-full items-center gap-2 rounded-lg py-1.5 pr-3 text-left text-sm hover:bg-slate-100 dark:hover:bg-slate-800"
            [style.paddingLeft.px]="8 + node.level * 16"
            [class.bg-sky-50]="destination() === node.id"
            [class.font-medium]="destination() === node.id"
            [class.text-sky-700]="destination() === node.id"
            [class.dark:bg-sky-950]="destination() === node.id"
            [class.dark:text-sky-300]="destination() === node.id"
            (click)="destination.set(node.id)"
          >
            <span
              class="flex h-5 w-5 flex-none items-center justify-center rounded hover:bg-slate-200 dark:hover:bg-slate-700"
              (click)="toggle(node, $event)"
            >
              @if (node.loading) {
                <span
                  class="h-3 w-3 animate-spin rounded-full border-2 border-slate-300 border-t-sky-600"
                ></span>
              } @else {
                <ui-icon [size]="14" [name]="node.expanded ? 'chevron-down' : 'chevron-right'" />
              }
            </span>
            <ui-icon [size]="16" name="folder" class="text-amber-500" />
            <span class="truncate">{{ node.name }}</span>
          </button>
        }
      </div>

      <div
        class="flex items-center justify-between gap-2 border-t border-slate-200 px-6 py-4 dark:border-slate-700"
      >
        <span class="truncate text-xs text-slate-500 dark:text-slate-400">
          {{ destination() === null ? 'All files' : destinationName() }}
        </span>
        <div class="flex gap-2">
          <button
            type="button"
            class="rounded-lg px-3 py-1.5 text-sm font-medium text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800"
            (click)="closed.emit()"
          >
            Cancel
          </button>
          <button
            type="button"
            class="rounded-lg bg-sky-600 px-3 py-1.5 text-sm font-semibold text-white shadow-sm hover:bg-sky-500 disabled:opacity-50"
            [disabled]="destination() === currentParentId()"
            (click)="confirmed.emit(destination())"
          >
            Move here
          </button>
        </div>
      </div>
    </ui-modal>
  `,
})
export class UiMoveDialog {
  private readonly api = inject(ApiService);

  readonly open = input(false);
  readonly count = input(1);
  readonly excludedIds = input<readonly string[]>([]);
  readonly currentParentId = input<string | null>(null);

  readonly confirmed = output<string | null>();
  readonly closed = output<void>();

  protected readonly tree = signal<TreeNode[]>([]);
  protected readonly destination = signal<string | null>(null);
  private destinationNode: TreeNode | null = null;

  constructor() {
    effect(() => {
      if (this.open()) {
        this.tree.set([]);
        this.destination.set(null);
        this.destinationNode = null;
        void this.loadRoot();
      }
    });

    effect(() => {
      const id = this.destination();
      this.destinationNode = id === null ? null : (this.findNode(this.tree(), id) ?? null);
    });
  }

  private async loadRoot(): Promise<void> {
    try {
      const page = await this.api.listChildren(null, 200);
      const excluded = new Set(this.excludedIds());
      this.tree.set(
        page.items
          .filter((item) => item.type === 'Folder' && !excluded.has(item.id))
          .map<TreeNode>((item) => ({
            id: item.id,
            name: item.name,
            level: 0,
            expanded: false,
            loading: false,
            children: null,
          })),
      );
    } catch {
    }
  }

  protected destinationName(): string {
    return this.destinationNode?.name ?? '';
  }

  protected toggle(node: TreeNode, event: MouseEvent): void {
    event.stopPropagation();
    if (node.loading) {
      return;
    }

    if (node.expanded) {
      node.expanded = false;
      this.tree.set(this.tree().filter((current) => !this.isDescendantOf(current, node)));
      return;
    }

    if (node.children !== null) {
      node.expanded = true;
      return;
    }

    void this.expand(node);
  }

  private async expand(node: TreeNode): Promise<void> {
    node.loading = true;
    this.tree.set([...this.tree()]);

    try {
      const page = await this.api.listChildren(node.id, 200);
      const excluded = new Set(this.excludedIds());
      const children = page.items
        .filter((item) => item.type === 'Folder' && !excluded.has(item.id))
        .map<TreeNode>((item) => ({
          id: item.id,
          name: item.name,
          level: node.level + 1,
          expanded: false,
          loading: false,
          children: null,
        }));

      node.children = children;
      node.expanded = true;

      const all = this.tree();
      const index = all.findIndex((current) => current.id === node.id);
      this.tree.set([...all.slice(0, index + 1), ...children, ...all.slice(index + 1)]);
    } catch {
      node.children = [];
      node.expanded = false;
    } finally {
      node.loading = false;
      this.tree.set([...this.tree()]);
    }
  }

  private findNode(nodes: readonly TreeNode[], id: string): TreeNode | null {
    for (const node of nodes) {
      if (node.id === id) {
        return node;
      }
      const nested = node.children ? this.findNode(node.children, id) : null;
      if (nested) {
        return nested;
      }
    }
    return null;
  }

  private isDescendantOf(candidate: TreeNode, ancestor: TreeNode): boolean {
    if (candidate.level <= ancestor.level) {
      return false;
    }
    return this.subtreeIds(ancestor).has(candidate.id);
  }

  private subtreeIds(node: TreeNode): Set<string> {
    const ids = new Set<string>();
    for (const child of node.children ?? []) {
      ids.add(child.id);
      for (const nested of this.subtreeIds(child)) {
        ids.add(nested);
      }
    }
    return ids;
  }
}