import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  AbortUploadResponse,
  CompleteUploadRequest,
  DownloadUrlResponse,
  InitiateUploadRequest,
  InitiateUploadResponse,
  NodeDetails,
  NodeSummary,
  PagedResult,
  PartUploadUrlResponse,
  TrashItem,
} from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  listChildren(
    parentId: string | null,
    pageSize = 50,
    cursor?: string,
  ): Promise<PagedResult<NodeSummary>> {
    const params: Record<string, string> = { pageSize: String(pageSize) };
    if (parentId) {
      params['parentId'] = parentId;
    }
    if (cursor) {
      params['cursor'] = cursor;
    }
    return firstValueFrom(this.http.get<PagedResult<NodeSummary>>('/api/v1/nodes', { params }));
  }

  getNode(nodeId: string, includeFolderSize = false): Promise<NodeDetails> {
    const params = includeFolderSize ? { includeFolderSize: 'true' } : undefined;
    return firstValueFrom(
      this.http.get<NodeDetails>(`/api/v1/nodes/${encodeURIComponent(nodeId)}`, { params }),
    );
  }

  createFolder(parentId: string | null, name: string): Promise<NodeDetails> {
    return firstValueFrom(this.http.post<NodeDetails>('/api/v1/folders', { parentId, name }));
  }

  rename(nodeId: string, name: string): Promise<NodeDetails> {
    return firstValueFrom(
      this.http.patch<NodeDetails>(`/api/v1/nodes/${encodeURIComponent(nodeId)}`, { name }),
    );
  }

  move(nodeId: string, parentId: string | null): Promise<NodeDetails> {
    return firstValueFrom(
      this.http.post<NodeDetails>(`/api/v1/nodes/${encodeURIComponent(nodeId)}/move`, { parentId }),
    );
  }

  delete(nodeId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/v1/nodes/${encodeURIComponent(nodeId)}`));
  }

  createDownloadUrl(nodeId: string): Promise<DownloadUrlResponse> {
    return firstValueFrom(
      this.http.get<DownloadUrlResponse>(
        `/api/v1/nodes/${encodeURIComponent(nodeId)}/download-url`,
      ),
    );
  }

  search(query: string, pageSize = 50): Promise<NodeSummary[]> {
    return firstValueFrom(
      this.http.get<NodeSummary[]>('/api/v1/search', {
        params: { q: query, pageSize: String(pageSize) },
      }),
    );
  }

  listTrash(): Promise<TrashItem[]> {
    return firstValueFrom(this.http.get<TrashItem[]>('/api/v1/trash'));
  }

  restoreNode(nodeId: string): Promise<NodeDetails> {
    return firstValueFrom(
      this.http.post<NodeDetails>(`/api/v1/trash/${encodeURIComponent(nodeId)}/restore`, {}),
    );
  }

  purgeNode(nodeId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/v1/trash/${encodeURIComponent(nodeId)}`));
  }

  emptyTrash(): Promise<void> {
    return firstValueFrom(this.http.delete<void>('/api/v1/trash'));
  }

  initiateUpload(request: InitiateUploadRequest): Promise<InitiateUploadResponse> {
    return firstValueFrom(
      this.http.post<InitiateUploadResponse>('/api/v1/uploads/initiate', request),
    );
  }

  requestPartUrl(uploadId: string, partNumber: number): Promise<PartUploadUrlResponse> {
    return firstValueFrom(
      this.http.post<PartUploadUrlResponse>(
        `/api/v1/uploads/${encodeURIComponent(uploadId)}/parts/${partNumber}/url`,
        {},
      ),
    );
  }

  completeUpload(uploadId: string, request: CompleteUploadRequest): Promise<NodeDetails> {
    return firstValueFrom(
      this.http.post<NodeDetails>(
        `/api/v1/uploads/${encodeURIComponent(uploadId)}/complete`,
        request,
      ),
    );
  }

  abortUpload(uploadId: string, reason: string | null): Promise<AbortUploadResponse> {
    return firstValueFrom(
      this.http.post<AbortUploadResponse>(`/api/v1/uploads/${encodeURIComponent(uploadId)}/abort`, {
        reason,
      }),
    );
  }

  putPart(
    part: PartUploadUrlResponse,
    blob: Blob,
    onProgress: (loadedBytes: number) => void,
    registerAbort: (abort: () => void) => void,
  ): Promise<string> {
    return new Promise<string>((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      registerAbort(() => xhr.abort());

      xhr.upload.onprogress = (event) => onProgress(event.loaded);
      xhr.onload = () => {
        if (xhr.status >= 200 && xhr.status < 300) {
          const eTag = xhr.getResponseHeader('ETag');
          if (eTag) {
            resolve(eTag);
          } else {
            reject(new Error('The storage response did not include an ETag header.'));
          }
        } else {
          reject(new Error(`Part upload failed with status ${xhr.status}.`));
        }
      };
      xhr.onerror = () => reject(new Error('The part upload failed (network or CORS error).'));
      xhr.onabort = () => reject(new DOMException('Upload aborted', 'AbortError'));

      xhr.open('PUT', part.url);
      for (const [name, value] of Object.entries(part.headers ?? {})) {
        xhr.setRequestHeader(name, value);
      }
      xhr.send(blob);
    });
  }
}
