import axios from 'axios';
import { env, auth0Audience } from '@config/env';
import type { StoredSession } from './tokenStorage';

// Password Realm (Resource Owner Password Grant) against Auth0, done as a plain
// HTTPS call to the token endpoint. This matches the OLD app's login (embedded
// username/password form) and works on iOS, Android AND web — unlike
// react-native-auth0, which has no web support.
//
// No redirect_uri, so NO callback URLs and NO Auth0 dashboard changes are needed.
// The "password" grant is already enabled on this tenant (the old app used it).

const TOKEN_URL = `https://${env.auth0Domain}/oauth/token`;
const USERINFO_URL = `https://${env.auth0Domain}/userinfo`;
const REALM = 'Username-Password-Authentication';
const SCOPE = 'openid profile email offline_access';

interface Auth0TokenResponse {
  access_token: string;
  refresh_token?: string;
  id_token?: string;
  expires_in?: number;
  token_type: string;
}

export interface AuthUser {
  sub: string;
  name?: string;
  email?: string;
  picture?: string;
}

function toSession(res: Auth0TokenResponse, previousRefreshToken?: string): StoredSession {
  return {
    accessToken: res.access_token,
    refreshToken: res.refresh_token ?? previousRefreshToken,
    expiresAt: Date.now() + (res.expires_in ?? 3600) * 1000,
  };
}

// Log in with username + password (Password Realm grant).
export async function passwordRealmLogin(
  username: string,
  password: string,
): Promise<StoredSession> {
  const { data } = await axios.post<Auth0TokenResponse>(TOKEN_URL, {
    grant_type: 'http://auth0.com/oauth/grant-type/password-realm',
    realm: REALM,
    username,
    password,
    audience: auth0Audience,
    scope: SCOPE,
    client_id: env.auth0ClientId,
  });
  return toSession(data);
}

// Exchange a refresh token for a fresh access token.
export async function refreshSession(refreshToken: string): Promise<StoredSession> {
  const { data } = await axios.post<Auth0TokenResponse>(TOKEN_URL, {
    grant_type: 'refresh_token',
    client_id: env.auth0ClientId,
    refresh_token: refreshToken,
  });
  return toSession(data, refreshToken);
}

// Fetch the user's profile (name/email) for display. Non-critical.
export async function fetchProfile(accessToken: string): Promise<AuthUser | null> {
  try {
    const { data } = await axios.get<AuthUser>(USERINFO_URL, {
      headers: { Authorization: `Bearer ${accessToken}` },
    });
    return { sub: data.sub, name: data.name, email: data.email, picture: data.picture };
  } catch {
    return null;
  }
}
