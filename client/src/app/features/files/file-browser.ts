import { Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { ApiError, NodeSummary, formatBytes, formatTimestamp } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { UploadManager } from '../../core/upload.service';
import { UiIcon } from '../../shared/icon';
import { UiConfirmDialog } from '../../shared/confirm-dialog';
import { UiMoveDialog } from '../../shared/move-dialog';
import { UiNameDialog } from '../../shared/name-dialog';

interface Breadcrumb {
  id: string | null;
  name: string;
}

interface MenuState {
  node: NodeSummary;
  x: number;
  y: number;
}

const PAGE_SIZE = 50;

/**
 * The drive: breadcrumbs, the item listing (with multi-select and a context menu), and every
 * node mutation — create folder, rename, move, delete, download, upload. The current folder
 * comes from the route param, so navigation is plain router navigation and the browser back
 * button works.
 */
@Component({
  selector: 'app-file-browser',
  imports: [UiIcon, UiNameDialog, UiConfirmDialog, UiMoveDialog],
  templateUrl: './file-browser.html',
  styleUrl: './file-browser.css',
})
export class FileBrowser {
  readonly folderId = input<string>();
  protected readonly items = signal<NodeSummary[]>([]);
  protected readonly crumbs = signal<Breadcrumb[]>([{ id: null, name: 'All files' }]);
  protected readonly selection = signal<ReadonlySet<string>>(new Set());
  protected readonly loading = signal(false);
  protected readonly loadingMore = signal(false);
  protected readonly hasMore = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly dragActive = signal(false);
  protected readonly menu = signal<MenuState | null>(null);

  // Dialog state
  protected readonly newFolderOpen = signal(false);
  protected readonly renameOpen = signal(false);
  protected readonly deleteOpen = signal(false);
  protected readonly moveOpen = signal(false);
  protected readonly renameTarget = signal<NodeSummary | null>(null);
  protected readonly moveTargets = signal<NodeSummary[]>([]);
  protected readonly dialogError = signal<string | null>(null);
  protected readonly busy = signal(false);

  private cursor?: string;

  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly uploads = inject(UploadManager);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly sortedItems = signal<NodeSummary[]>([]);

  protected readonly formatBytes = formatBytes;
  protected readonly formatTimestamp = formatTimestamp;

  constructor() {
    // Reload whenever the routed folder changes (including the first render).
    effect(() => {
      const id = this.folderId() ?? null;
      void this.loadFolder(id);
    });

    // Refresh the listing whenever an upload finishes in this folder.
    this.uploads.completed.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((node) => {
      if ((node.parentId ?? null) === (this.folderId() ?? null)) {
        this.insertItem(node);
      }
    });

    // Close the context menu on any outside click.
    if (typeof document !== 'undefined') {
      const abort = new AbortController();
      this.destroyRef.onDestroy(() => abort.abort());
      document.addEventListener('click', () => this.menu.set(null), { signal: abort.signal });
    }
  }

  // ---- loading ---------------------------------------------------------------

  private async loadFolder(parentId: string | null): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);
    this.selection.set(new Set());
    this.menu.set(null);
    this.cursor = undefined;

    try {
      const [page, crumbs] = await Promise.all([
        this.api.listChildren(parentId, PAGE_SIZE),
        this.buildCrumbs(parentId),
      ]);
      this.items.set(page.items);
      this.sortedItems.set([...page.items].sort(sortItems));
      this.cursor = page.nextCursor ?? undefined;
      this.hasMore.set(page.hasMore);
      this.crumbs.set(crumbs);
    } catch (error) {
      this.items.set([]);
      this.sortedItems.set([]);
      this.hasMore.set(false);
      this.loadError.set(describeLoadError(error));
    } finally {
      this.loading.set(false);
    }
  }

  private async buildCrumbs(folderId: string | null): Promise<Breadcrumb[]> {
    const crumbs: Breadcrumb[] = [];
    let cursor = folderId;
    const seen = new Set<string>();

    while (cursor && !seen.has(cursor)) {
      seen.add(cursor);
      try {
        const node = await this.api.getNode(cursor);
        crumbs.unshift({ id: node.id, name: node.name });
        cursor = node.parentId;
      } catch {
        break;
      }
    }

    crumbs.unshift({ id: null, name: 'All files' });
    return crumbs;
  }

  protected reload(): void {
    void this.loadFolder(this.folderId() ?? null);
  }

  protected async loadMore(): Promise<void> {
    if (!this.cursor || this.loadingMore()) {
      return;
    }

    this.loadingMore.set(true);
    try {
      const page = await this.api.listChildren(this.folderId() ?? null, PAGE_SIZE, this.cursor);
      const existing = this.items();
      this.items.set([...existing, ...page.items]);
      this.sortedItems.set([...this.items()].sort(sortItems));
      this.cursor = page.nextCursor ?? undefined;
      this.hasMore.set(page.hasMore);
    } catch (error) {
      this.toast.error(describeLoadError(error) ?? 'Could not load more items.');
    } finally {
      this.loadingMore.set(false);
    }
  }

  // ---- navigation ------------------------------------------------------------

  protected navigateTo(folderId: string | null): void {
    void this.router.navigate(folderId === null ? ['/files'] : ['/files', folderId]);
  }

  protected open(node: NodeSummary): void {
    if (node.type === 'Folder') {
      this.navigateTo(node.id);
    } else {
      void this.download(node);
    }
  }

  protected openAndCloseMenu(node: NodeSummary): void {
    this.menu.set(null);
    this.open(node);
  }

  // ---- selection -------------------------------------------------------------

  protected onRowClick(node: NodeSummary, event: MouseEvent): void {
    if (event.ctrlKey || event.metaKey) {
      this.toggleSelected(node);
      return;
    }
    this.selection.set(new Set([node.id]));
  }

  protected toggleSelected(node: NodeSummary, event?: Event): void {
    event?.stopPropagation();
    event?.preventDefault();
    const next = new Set(this.selection());
    if (next.has(node.id)) {
      next.delete(node.id);
    } else {
      next.add(node.id);
    }
    this.selection.set(next);
  }

  protected toggleSelectAll(): void {
    if (this.allSelected()) {
      this.clearSelection();
      return;
    }
    this.selection.set(new Set(this.items().map((item) => item.id)));
  }

  protected allSelected(): boolean {
    return this.items().length > 0 && this.selection().size === this.items().length;
  }

  protected someSelected(): boolean {
    return this.selection().size > 0 && !this.allSelected();
  }

  protected clearSelection(): void {
    this.selection.set(new Set());
  }

  // ---- context menu ----------------------------------------------------------

  protected openContextMenu(node: NodeSummary, event: MouseEvent): void {
    event.preventDefault();
    event.stopPropagation();

    // Keep the menu inside the viewport.
    const x = Math.min(event.clientX, window.innerWidth - 190);
    const y = Math.min(event.clientY, window.innerHeight - 190);
    this.menu.set({ node, x, y });
  }

  // ---- mutations -------------------------------------------------------------

  protected openNewFolder(): void {
    this.dialogError.set(null);
    this.newFolderOpen.set(true);
  }

  protected async createFolder(name: string): Promise<void> {
    this.busy.set(true);
    this.dialogError.set(null);
    try {
      const folder = await this.api.createFolder(this.folderId() ?? null, name);
      this.newFolderOpen.set(false);
      this.insertItem(folder);
      this.toast.success(`Folder "${folder.name}" created.`);
    } catch (error) {
      this.dialogError.set(describeMutationError(error, 'name'));
    } finally {
      this.busy.set(false);
    }
  }

  protected openRename(node: NodeSummary): void {
    this.menu.set(null);
    this.dialogError.set(null);
    this.renameTarget.set(node);
    this.renameOpen.set(true);
  }

  protected async rename(name: string): Promise<void> {
    const target = this.renameTarget();
    if (!target) {
      return;
    }

    this.busy.set(true);
    this.dialogError.set(null);
    try {
      const updated = await this.api.rename(target.id, name);
      this.renameOpen.set(false);
      this.replaceItem(updated);
      this.toast.success(`Renamed to "${updated.name}".`);
    } catch (error) {
      this.dialogError.set(describeMutationError(error, 'name'));
    } finally {
      this.busy.set(false);
    }
  }

  protected openDelete(): void {
    this.deleteOpen.set(true);
  }

  protected deleteMessage(): string {
    const targets = this.selectedNodes();
    if (targets.length > 1) {
      return `${targets.length} items will be deleted. Folders and their contents are removed permanently; storage objects are cleaned up shortly after.`;
    }
    if (targets.length === 1) {
      return targets[0].type === 'Folder'
        ? `“${targets[0].name}” and everything inside it will be deleted.`
        : `“${targets[0].name}” will be deleted.`;
    }
    return 'The selected items will be deleted.';
  }

  protected askDelete(node: NodeSummary): void {
    this.menu.set(null);
    if (!this.selection().has(node.id)) {
      this.selection.set(new Set([node.id]));
    }
    this.deleteOpen.set(true);
  }

  protected async confirmDelete(): Promise<void> {
    const targets = this.selectedNodes();
    if (targets.length === 0) {
      return;
    }

    this.busy.set(true);
    try {
      const failures: string[] = [];
      for (const target of targets) {
        try {
          await this.api.delete(target.id);
          this.removeItem(target.id);
        } catch (error) {
          failures.push(`${target.name}: ${describeMutationError(error)}`);
        }
      }

      if (failures.length === 0) {
        this.toast.success(
          targets.length === 1
            ? `Deleted "${targets[0].name}".`
            : `Deleted ${targets.length} items.`,
        );
      } else {
        this.toast.error(`Some items could not be deleted — ${failures[0]}`);
      }
      this.clearSelection();
      this.deleteOpen.set(false);
    } finally {
      this.busy.set(false);
    }
  }

  protected openMove(): void {
    this.moveTargets.set(this.selectedNodes());
    this.moveOpen.set(true);
  }

  protected openMoveFor(node: NodeSummary): void {
    this.menu.set(null);
    this.moveTargets.set([node]);
    this.moveOpen.set(true);
  }

  protected async move(destination: string | null): Promise<void> {
    const targets = this.moveTargets();
    if (targets.length === 0) {
      return;
    }

    this.busy.set(true);
    try {
      const failures: string[] = [];
      for (const target of targets) {
        try {
          await this.api.move(target.id, destination);
          this.removeItem(target.id);
        } catch (error) {
          failures.push(`${target.name}: ${describeMutationError(error)}`);
        }
      }

      if (failures.length === 0) {
        this.toast.success(
          targets.length === 1 ? `Moved "${targets[0].name}".` : `Moved ${targets.length} items.`,
        );
        this.moveOpen.set(false);
        this.clearSelection();
      } else {
        this.toast.error(`Some items could not be moved — ${failures[0]}`);
        this.moveOpen.set(false);
        this.reload();
      }
    } finally {
      this.busy.set(false);
    }
  }

  // ---- downloads -------------------------------------------------------------

  protected downloadSelection(): void {
    const nodes = this.selectedNodes().filter((node) => node.type === 'File');
    if (nodes.length === 0) {
      this.toast.info('Folders cannot be downloaded directly.');
      return;
    }
    for (const node of nodes) {
      void this.download(node);
    }
  }

  protected async download(node: NodeSummary): Promise<void> {
    this.menu.set(null);
    if (node.type !== 'File') {
      return;
    }
    try {
      const download = await this.api.createDownloadUrl(node.id);
      window.location.assign(download.url);
    } catch (error) {
      this.toast.error(describeMutationError(error) ?? 'Could not create a download link.');
    }
  }

  // ---- uploads ---------------------------------------------------------------

  protected onFilesPicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    if (files.length > 0) {
      this.uploads.enqueue(this.folderId() ?? null, files);
      this.toast.info(
        files.length === 1 ? `Uploading "${files[0].name}"…` : `Uploading ${files.length} files…`,
      );
    }
    input.value = '';
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragActive.set(true);
  }

  protected onDragLeave(event: DragEvent): void {
    if (event.currentTarget === event.target) {
      this.dragActive.set(false);
    }
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragActive.set(false);
    const files = Array.from(event.dataTransfer?.files ?? []);
    if (files.length > 0) {
      this.uploads.enqueue(this.folderId() ?? null, files);
      this.toast.info(
        files.length === 1 ? `Uploading "${files[0].name}"…` : `Uploading ${files.length} files…`,
      );
    }
  }

  // ---- helpers ---------------------------------------------------------------

  protected selectedNodes(): NodeSummary[] {
    const ids = this.selection();
    return this.items().filter((item) => ids.has(item.id));
  }

  protected closeDialogs(): void {
    this.newFolderOpen.set(false);
    this.renameOpen.set(false);
    this.deleteOpen.set(false);
    this.moveOpen.set(false);
    this.dialogError.set(null);
  }

  private insertItem(node: NodeSummary): void {
    this.items.set([...this.items().filter((item) => item.id !== node.id), node]);
    this.sortedItems.set([...this.items()].sort(sortItems));
  }

  private replaceItem(node: NodeSummary): void {
    this.items.set(this.items().map((item) => (item.id === node.id ? node : item)));
    this.sortedItems.set([...this.items()].sort(sortItems));
  }

  private removeItem(id: string): void {
    this.items.set(this.items().filter((item) => item.id !== id));
    this.sortedItems.set(this.items().filter((item) => item.id !== id));
  }
}

/** Folders first, then files, both by name — matching the server's within-group ordering. */
function sortItems(a: NodeSummary, b: NodeSummary): number {
  if (a.type !== b.type) {
    return a.type === 'Folder' ? -1 : 1;
  }
  return a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' });
}

function describeLoadError(error: unknown): string {
  if (error instanceof ApiError && error.status === 404) {
    return 'This folder no longer exists or you do not have access to it.';
  }
  return describeMutationError(error);
}

function describeMutationError(error: unknown, field?: string): string {
  if (error instanceof ApiError) {
    const fieldMessage = field ? error.fieldError(field) : undefined;
    return fieldMessage ?? error.message;
  }
  return error instanceof Error ? error.message : 'The operation failed unexpectedly.';
}
