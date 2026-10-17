import { useRouter } from 'expo-router'

import { LegalScreen } from '../features/legal/LegalScreen'
import { setLegalRequired } from '../features/legal/legalState'

export default function Legal() {
  const router = useRouter()
  return (
    <LegalScreen
      onAccepted={() => {
        setLegalRequired(false)
        router.replace('/home')
      }}
    />
  )
}
