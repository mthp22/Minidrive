import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideClientHydration } from '@angular/platform-browser';
import { provideAuth0 } from '@auth0/auth0-angular';
import { routes } from './app.routes';
import { apiInterceptor } from './core/api.interceptor';
import { AuthService } from './core/auth.service';
import { environment } from '../environments/environment';

// Auth0 is registered only when configured; otherwise the app runs the local dev-token flow.
const auth0Providers = environment.auth0
  ? [
      provideAuth0({
        domain: environment.auth0.domain,
        clientId: environment.auth0.clientId,
        authorizationParams: {
          redirect_uri:
            environment.auth0.redirectUri ??
            (typeof window !== 'undefined' ? window.location.origin : '/'),
          ...(environment.auth0.audience ? { audience: environment.auth0.audience } : {}),
        },
      }),
    ]
  : [];

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideClientHydration(),
    provideHttpClient(withInterceptors([apiInterceptor])),
    ...auth0Providers,
    // Bootstrap authentication before the first API call: mints the dev token, or (with Auth0)
    // subscribes to the authentication state after the SDK has handled any redirect callback.
    // No-op during SSR.
    provideAppInitializer(() => inject(AuthService).init()),
  ],
};
