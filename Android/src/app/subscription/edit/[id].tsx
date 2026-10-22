import { useLocalSearchParams } from 'expo-router';
import { SubscriptionForm } from '@/screens/SubscriptionForm';

export default function EditSubscription() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <SubscriptionForm editId={id ?? null} />;
}
