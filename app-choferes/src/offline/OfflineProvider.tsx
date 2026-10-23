import { useEffect } from 'react';
import { AppState } from 'react-native';
import { onlineManager, type QueryClient } from '@tanstack/react-query';
import { PersistQueryClientProvider } from '@tanstack/react-query-persist-client';
import { sqlitePersister } from './persister';
import { initOnlineManager } from './netStatus';
import { drainOutbox } from './syncEngine';
import { storage } from './db';

const ONE_DAY = 1000 * 60 * 60 * 24;

// Persisted react-query cache (offline reads) + NetInfo wiring + outbox drain
// on startup, connectivity restored, and app foreground.
export function OfflineProvider({
  client,
  children,
}: {
  client: QueryClient;
  children: React.ReactNode;
}) {
  useEffect(() => {
    void storage.init();

    // The ONLY place that reacts to "connectivity/foreground came back": send
    // whatever's queued FIRST, and only THEN ask the server for fresh data.
    // Automatic per-query refetching (react-query's refetchOnReconnect) is
    // disabled globally (see _layout.tsx) specifically so this is the sole
    // path — two independent "reconnect" handlers racing each other is what
    // let a stale server response overwrite a just-completed local delivery.
    const catchUp = async () => {
      await drainOutbox();
      void client.invalidateQueries();
    };

    const stopNetInfo = initOnlineManager();
    const stopOnline = onlineManager.subscribe((online) => {
      if (online) void catchUp();
    });
    const appStateSub = AppState.addEventListener('change', (s) => {
      if (s === 'active') void catchUp();
    });

    void catchUp();

    return () => {
      stopNetInfo?.();
      stopOnline();
      appStateSub.remove();
    };
  }, [client]);

  return (
    <PersistQueryClientProvider
      client={client}
      persistOptions={{ persister: sqlitePersister, maxAge: ONE_DAY }}
    >
      {children}
    </PersistQueryClientProvider>
  );
}
