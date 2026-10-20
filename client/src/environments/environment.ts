/**
 * Build-time environment settings.
 *
 * Auth0 is optional. Leave `auth0` as `undefined` to run the local development flow (a shared
 * `dev-user` token is minted at bootstrap, no login screen). To enable real Auth0 login, fill in
 * the three values from your Auth0 dashboard (a "Single Page Application" client plus an "API"
 * whose identifier is the audience):
 *
 *   auth0: {
 *     domain: 'dev-xxxxx.us.auth0.com',
 *     clientId: 'AbCdEf...',        // SPA client ID
 *     audience: 'https://minidrive-api', // API identifier (audience)
 *   },
 *
 * The API must be given the same domain and audience
 * (Authentication__Auth0Domain / Authentication__Auth0Audience) so it validates the same tokens.
 */
export const environment = {
  production: false,

  /** Auth0 settings, or undefined to use the local dev-token flow. */
  auth0: undefined as
    | {
        domain: string;
        clientId: string;
        audience: string;
        /** Defaults to the current origin. */
        redirectUri?: string;
      }
    | undefined,
};
