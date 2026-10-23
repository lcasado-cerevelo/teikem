import { Redirect, Stack } from 'expo-router';
import { useAuth } from '@hooks/AuthContext';

export default function NoAuthLayout() {
  const { isLoggedIn } = useAuth();
  if (isLoggedIn) return <Redirect href="/(auth)" />;
  return <Stack screenOptions={{ headerShown: false }} />;
}
