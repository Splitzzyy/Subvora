import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { ApiError, authApi, setSessionExpiredHandler, tokens, type AuthTokenResponse } from '@/api';
import { clearCache } from './cache';
import { cancelReminders } from './reminders';

type Status = 'loading' | 'signedIn' | 'signedOut';

interface Session {
  status: Status;
  signIn: (tokens: AuthTokenResponse) => Promise<void>;
  signOut: () => Promise<void>;
  /**
   * Bumped by every write. Screens reload on focus when it has moved since their last load - the
   * MAUI app's SubscriptionsChangedMessage, without a messenger.
   */
  dataVersion: number;
  dataChanged: () => void;
}

const SessionContext = createContext<Session | null>(null);

export function SessionProvider({ children }: { children: React.ReactNode }) {
  const [status, setStatus] = useState<Status>('loading');
  const [dataVersion, setDataVersion] = useState(0);

  /** Ends the local session exactly like an explicit sign-out: tokens, mirror and reminders go. */
  const endLocally = useCallback(async () => {
    await tokens.clear();
    await clearCache();
    await cancelReminders();
    setStatus('signedOut');
  }, []);

  useEffect(() => {
    setSessionExpiredHandler(() => { void endLocally(); });
    tokens.load().then((token) => setStatus(token ? 'signedIn' : 'signedOut'));
  }, [endLocally]);

  const value = useMemo<Session>(() => ({
    status,
    dataVersion,
    dataChanged: () => setDataVersion((v) => v + 1),
    signIn: async (t) => {
      await tokens.save(t);
      setStatus('signedIn');
    },
    signOut: async () => {
      const refreshToken = await tokens.refreshToken();
      if (refreshToken) {
        // Best-effort server revoke; signing out with the API unreachable must still end the
        // local session.
        try { await authApi.logout(refreshToken); } catch (e) { if (!(e instanceof ApiError)) throw e; }
      }
      await endLocally();
    },
  }), [status, dataVersion, endLocally]);

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession() {
  const session = useContext(SessionContext);
  if (!session) throw new Error('useSession outside SessionProvider');
  return session;
}
