import NetInfo from '@react-native-community/netinfo';
import { onlineManager } from '@tanstack/react-query';

export function initOnlineManager(): () => void {
  return onlineManager.setEventListener((setOnline) => {
    const unsubscribe = NetInfo.addEventListener((state) => {
      setOnline(Boolean(state.isConnected) && state.isInternetReachable !== false);
    });
    return unsubscribe;
  });
}
