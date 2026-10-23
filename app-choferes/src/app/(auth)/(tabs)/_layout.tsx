import { Tabs } from 'expo-router';
import { AntDesign, FontAwesome, MaterialCommunityIcons } from '@expo/vector-icons';
import LoggedHeader from '@components/LoggedHeader';
import LogoutButton from '@components/LogoutButton';
import { colors } from '@constants/theme';

export default function TabLayout() {
  return (
    <Tabs screenOptions={{ tabBarActiveTintColor: colors.primary }}>
      <Tabs.Screen
        name="index"
        options={{
          title: 'Home',
          tabBarShowLabel: false,
          headerTitle: '',
          headerStyle: { height: 110 },
          headerLeft: () => <LoggedHeader />,
          headerRight: () => <LogoutButton />,
          tabBarIcon: ({ color }) => (
            <FontAwesome name="home" color={color} size={28} style={{ marginBottom: -3 }} />
          ),
        }}
      />
      <Tabs.Screen
        name="manifests"
        options={{
          title: 'Manifests',
          tabBarShowLabel: false,
          headerShown: false,
          tabBarIcon: ({ color }) => (
            <MaterialCommunityIcons name="truck-fast-outline" color={color} size={28} style={{ marginBottom: -3 }} />
          ),
        }}
      />
      <Tabs.Screen
        name="profile"
        options={{
          title: 'Profile',
          tabBarShowLabel: false,
          headerStyle: { height: 110 },
          headerTitle: '',
          tabBarIcon: ({ color }) => (
            <AntDesign name="user" color={color} size={28} style={{ marginBottom: -3 }} />
          ),
        }}
      />
    </Tabs>
  );
}
