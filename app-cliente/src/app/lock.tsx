import { useRouter } from 'expo-router'

import { LockScreen } from '../features/lock/LockScreen'
import { useAppLock } from '../kernel/auth/AppLock'

export default function Lock() {
  const router = useRouter()
  const { unlock } = useAppLock()
  return (
    <LockScreen
      onUnlock={() => {
        unlock()
        router.replace('/home')
      }}
    />
  )
}
