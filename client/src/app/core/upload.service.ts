import { Injectable, inject, signal } from '@angular/core';
import { Subject } from 'rxjs';
import { ApiService } from './api.service';
import { ApiError, NodeDetails } from './models';

export type UploadStatus = 'queued' | 'uploading' | 'completing' | 'done' | 'error' | 'canceled';

export interface UploadTask {
  readonly id: string;
  readonly fileName: string;
  readonly sizeBytes: number;
  readonly parentId: string | null;
  status: UploadStatus;
  progress: number;
  uploadedBytes: number;
  error: string | null;
}

interface InternalTask {
  task: UploadTask;
  file: File;
  uploadId: string | null;
  aborts: Set<() => void>;
  canceled: boolean;
}

/** How many files move at the same time. */
const MAX_CONCURRENT_FILES = 2;
/** How many parts of a single file are PUT in parallel. */
const MAX_CONCURRENT_PARTS = 3;

/**
 * Client side of the three-phase upload protocol:
 * initiate → pre-signed part URLs → complete. Tracks byte-level progress across parallel part
 * PUTs, supports canceling (which aborts the provider upload server-side) and retrying a failed
 * file from scratch. Completed files are published through {@link completed} so the browser can
 * refresh its listing without polling.
 */
@Injectable({ providedIn: 'root' })
export class UploadManager {
  private readonly api = inject(ApiService);

  private readonly tasksSignal = signal<UploadTask[]>([]);
  readonly tasks = this.tasksSignal.asReadonly();
  readonly completed = new Subject<NodeDetails>();

  private readonly pending: InternalTask[] = [];
  private readonly active = new Map<string, InternalTask>();
  private running = 0;
  private taskSeq = 0;

  enqueue(parentId: string | null, files: readonly File[]): void {
    if (files.length === 0) {
      return;
    }

    const internals: InternalTask[] = files.map((file) => ({
      task: {
        id: `upload-${++this.taskSeq}`,
        fileName: file.name,
        sizeBytes: file.size,
        parentId,
        status: 'queued',
        progress: 0,
        uploadedBytes: 0,
        error: null,
      },
      file,
      uploadId: null,
      aborts: new Set(),
      canceled: false,
    }));

    this.tasksSignal.update((tasks) => [...tasks, ...internals.map((item) => item.task)]);
    this.pending.push(...internals);
    this.pump();
  }

  cancel(taskId: string): void {
    const internal = this.find(taskId);
    if (!internal || internal.task.status === 'done' || internal.task.status === 'canceled') {
      return;
    }

    internal.canceled = true;
    for (const abort of internal.aborts) {
      abort();
    }
    this.abandon(internal, 'canceled');
  }

  retry(taskId: string): void {
    const internal = this.find(taskId);
    if (!internal || (internal.task.status !== 'error' && internal.task.status !== 'canceled')) {
      return;
    }

    internal.task.status = 'queued';
    internal.task.progress = 0;
    internal.task.uploadedBytes = 0;
    internal.task.error = null;
    internal.uploadId = null;
    internal.aborts.clear();
    internal.canceled = false;

    this.pending.push(internal);
    this.pump();
  }

  /** Removes finished/canceled tasks from the drawer. */
  clearFinished(): void {
    this.tasksSignal.update((tasks) =>
      tasks.filter((task) => task.status !== 'done' && task.status !== 'canceled'),
    );
  }

  dismiss(taskId: string): void {
    this.tasksSignal.update((tasks) => tasks.filter((task) => task.id !== taskId));
  }

  private find(taskId: string): InternalTask | null {
    const all = [...this.pending, ...this.active.values()];
    return all.find((item) => item.task.id === taskId) ?? null;
  }

  private pump(): void {
    while (this.running < MAX_CONCURRENT_FILES && this.pending.length > 0) {
      const next = this.pending.shift()!;
      if (next.canceled) {
        continue;
      }
      this.running++;
      this.active.set(next.task.id, next);
      void this.execute(next).finally(() => {
        this.running--;
        this.active.delete(next.task.id);
        this.pump();
      });
    }
  }

  private async execute(internal: InternalTask): Promise<void> {
    const { task } = internal;

    try {
      this.update(task, (current) => (current.status = 'uploading'));

      const session = await this.api.initiateUpload({
        parentId: task.parentId,
        fileName: task.fileName,
        contentType: internal.file.type || null,
        sizeBytes: task.sizeBytes,
      });
      internal.uploadId = session.uploadId;

      if (internal.canceled) {
        return;
      }

      // Split the file into chunks and PUT them with bounded parallelism.
      const parts = new Array(session.expectedPartCount);
      let uploaded = 0;

      const uploadPart = async (index: number): Promise<void> => {
        const partNumber = session.partNumberMinimum + index;
        const start = index * session.chunkSizeBytes;
        const end = Math.min(start + session.chunkSizeBytes, task.sizeBytes);
        const blob = internal.file.slice(start, end);

        const partUrl = await this.api.requestPartUrl(session.uploadId, partNumber);
        if (internal.canceled) {
          return;
        }

        const eTag = await this.api.putPart(
          partUrl,
          blob,
          (loaded) => {
            this.update(task, (current) => {
              current.uploadedBytes = Math.min(uploaded + loaded, task.sizeBytes);
              current.progress =
                task.sizeBytes > 0
                  ? Math.round((current.uploadedBytes / task.sizeBytes) * 100)
                  : 100;
            });
          },
          (abort) => internal.aborts.add(abort),
        );

        parts[index] = { partNumber, eTag };
        uploaded += end - start;
        this.update(task, (current) => {
          current.uploadedBytes = uploaded;
          current.progress =
            task.sizeBytes > 0 ? Math.round((uploaded / task.sizeBytes) * 100) : 100;
        });
      };

      for (let offset = 0; offset < parts.length; offset += MAX_CONCURRENT_PARTS) {
        if (internal.canceled) {
          return;
        }
        const batch = [];
        for (
          let index = offset;
          index < Math.min(offset + MAX_CONCURRENT_PARTS, parts.length);
          index++
        ) {
          batch.push(uploadPart(index));
        }
        await Promise.all(batch);
      }

      if (internal.canceled) {
        return;
      }

      this.update(task, (current) => (current.status = 'completing'));
      const node = await this.api.completeUpload(session.uploadId, { parts });

      this.update(task, (current) => {
        current.status = 'done';
        current.progress = 100;
      });
      this.completed.next(node);
    } catch (error) {
      if (internal.canceled) {
        return;
      }
      this.update(task, (current) => {
        current.status = 'error';
        current.error = describeError(error);
      });
    }
  }

  private async abandon(internal: InternalTask, status: UploadStatus): Promise<void> {
    this.update(internal.task, (current) => (current.status = status));

    const uploadId = internal.uploadId;
    if (uploadId) {
      try {
        await this.api.abortUpload(uploadId, 'canceled from the web UI');
      } catch {
        //TODO: catch properly
      }
    }
  }

  private update(task: UploadTask, mutate: (task: UploadTask) => void): void {
    // Tasks are mutated in place; a new array identity re-notifies the signal.
    mutate(task);
    this.tasksSignal.update((tasks) => [...tasks]);
  }
}

function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    const conflict = error.fieldError('name') ?? error.fieldError('fileName');
    if (error.code === 'node.name.conflict' || error.status === 409) {
      return conflict ?? 'A file with this name already exists in the destination folder.';
    }
    return error.message;
  }
  if (error instanceof DOMException && error.name === 'AbortError') {
    return 'Upload canceled.';
  }
  return error instanceof Error ? error.message : 'The upload failed unexpectedly.';
}
