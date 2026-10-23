import { storage } from './db';
import { sqlitePersister } from './persister';

// Wipe every piece of DURABLE local state belonging to the driver who is
// logging out: the outbox queue and the persisted react-query cache. Without
// this, the next driver to log in on the same phone inherits the previous
// driver's shipments/manifests from the restored cache (and the static
// ['me'] query key happily serves the old identity).
//
// In-memory state (the live QueryClient, the zustand bridge store) is the
// caller's responsibility — it lives in React land and is cleared in
// AuthContext.logout.
export async function clearOfflineData(): Promise<void> {
  await storage.clearOutbox();
  await sqlitePersister.removeClient();
}
