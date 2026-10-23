import { useSyncExternalStore } from 'react';
import { onlineManager } from '@tanstack/react-query';

// Live connectivity flag. onlineManager is fed by NetInfo (offline/netStatus.ts),
// so every consumer of this hook sees the exact same signal the sync engine uses.
export function useIsOnline(): boolean {
  return useSyncExternalStore(
    (onChange) => onlineManager.subscribe(onChange),
    () => onlineManager.isOnline(),
    () => true,
  );
}
