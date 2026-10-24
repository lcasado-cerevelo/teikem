import { Redirect } from 'expo-router';
import { useAuth } from '@hooks/AuthContext';

export default function Index() {
  const { isLoggedIn } = useAuth();
  return <Redirect href={isLoggedIn ? '/(auth)' : '/(no-auth)/Login'} />;
}
