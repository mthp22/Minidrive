export type NodeType = 'File' | 'Folder';

export interface NodeSummary {
  id: string;
  parentId: string | null;
  name: string;
  type: NodeType;
  sizeBytes: number;
  contentType: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface NodeDetails extends NodeSummary {
  eTag: string | null;
  folderSizeBytes: number | null;
}

export interface PagedResult<T> {
  items: T[];
  nextCursor: string | null;
  hasMore: boolean;
}

export interface InitiateUploadRequest {
  parentId: string | null;
  fileName: string;
  contentType: string | null;
  sizeBytes: number;
}

export interface InitiateUploadResponse {
  uploadId: string;
  fileId: string;
  fileName: string;
  chunkSizeBytes: number;
  partNumberMinimum: number;
  partNumberMaximum: number;
  expectedPartCount: number;
  expiresAtUtc: string;
  status: string;
}

export interface PartUploadUrlResponse {
  partNumber: number;
  url: string;
  expiresAtUtc: string;
  headers: Record<string, string>;
}

export interface UploadedPart {
  partNumber: number;
  eTag: string;
}

export interface CompleteUploadRequest {
  parts: UploadedPart[];
}

export interface AbortUploadResponse {
  uploadId: string;
  status: string;
}

export interface DownloadUrlResponse {
  fileId: string;
  fileName: string;
  url: string;
  expiresAtUtc: string;
}

export interface IssuedToken {
  accessToken: string;
  tokenType: string;
  expiresAtUtc: string;
}

export interface ApiProblem {
  status?: number;
  title?: string;
  detail?: string;
  type?: string;
  instance?: string;
  code?: string;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: ApiProblem | null,
  ) {
    super(problem?.detail ?? problem?.title ?? `Request failed with status ${status}.`);
    this.name = 'ApiError';
  }

  get code(): string {
    return this.problem?.code ?? `http.${this.status}`;
  }

  fieldError(field: string): string | undefined {
    return this.problem?.errors?.[field]?.[0];
  }
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }

  const units = ['KiB', 'MiB', 'GiB', 'TiB'];
  let value = bytes;
  let unit = 'B';

  for (const next of units) {
    if (value < 1024) {
      break;
    }
    value /= 1024;
    unit = next;
  }

  return `${value >= 100 || Number.isInteger(value) ? Math.round(value) : value.toFixed(1)} ${unit}`;
}


export function formatTimestamp(iso: string, type: NodeType): string {
  const date = new Date(iso);
  return type === 'Folder'
    ? date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
    : date.toLocaleString(undefined, {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
      });
}