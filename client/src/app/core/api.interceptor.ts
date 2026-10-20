import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { ApiError, ApiProblem } from './models';

/**
 * Attaches the bearer token to every API call and normalizes error responses into
 * {@link ApiError} so callers can branch on the API's stable `code` values.
 *
 * Token resolution is asynchronous because Auth0 refreshes access tokens silently; in dev mode the
 * already-minted token is emitted immediately.
 */
export const apiInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);

  return auth.getAuthToken().pipe(
    switchMap((token) => {
      const withToken = token
        ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
        : request;
      return next(withToken);
    }),
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) {
        return throwError(() => new ApiError(error.status, parseProblem(error)));
      }
      return throwError(() => error);
    }),
  );
};

function parseProblem(error: HttpErrorResponse): ApiProblem | null {
  const body = error.error;
  if (body && typeof body === 'object' && ('title' in body || 'code' in body || 'errors' in body)) {
    return body as ApiProblem;
  }
  return null;
}
