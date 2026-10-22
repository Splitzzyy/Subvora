import { router, useLocalSearchParams } from 'expo-router';
import { useCallback, useState } from 'react';
import { View } from 'react-native';
import { subscriptionsApi } from '@/api';
import { useLoader } from '@/lib/hooks';
import { asyncState } from '@/lib/loading';
import {
  CYCLE_LABEL, daysUntil, describeRelative, formatDayDate, formatLongDate, formatMoney, isOverdue, reminderDate, todayDateOnly, type BillingCycle,
} from '@/lib/logic';
import { useSubscriptionActions } from '@/lib/subscriptionActions';
import { shape, space, useTheme } from '@/theme';
import { Banner, Button, Card, Icon, IconButton, ListItem, Pill, positionOf, Text, type IconName } from '@/ui';
import { Screen } from '@/ui/layout';
import { Logo } from '@/ui/logo';
import { ActionSheet } from '@/ui/sheets';
import { AsyncView, FormSkeleton } from '@/ui/states';

const PER: Record<BillingCycle, string> = { Weekly: '/ week', Monthly: '/ month', Quarterly: '/ quarter', Yearly: '/ year', OneTime: 'one-time' };

/**
 * Read-only details (#276): what it costs here and in the home currency, when it charges and
 * reminds, how it is paid. Editing is a deliberate step (the pencil), so checking a date can never
 * change a price; Delete sits in the menu.
 */
export default function SubscriptionDetails() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { colors, category } = useTheme();
  const fetcher = useCallback(() => subscriptionsApi.get(id), [id]);
  const { data: sub, loading, error, errorStatus, reload } = useLoader(fetcher);
  const actions = useSubscriptionActions();
  const [menu, setMenu] = useState(false);

  const today = todayDateOnly();
  const overdue = sub ? isOverdue(sub, today) : false;
  const identity = category(sub?.categoryName);
  const edit = () => router.push(`/subscription/edit/${id}`);

  const facts: { icon: IconName; label: string; value: string; tone?: 'warning' }[] = sub ? [
    {
      icon: 'calendar-refresh-outline',
      label: sub.cycleCadence === 'OneTime' ? 'Due' : sub.isFreeTrial ? 'Trial ends' : 'Next charge',
      value: overdue ? `Overdue · was due ${formatLongDate(sub.nextBillingDate)}`
        // Past a fortnight describeRelative is the date itself; say it once.
        : daysUntil(sub.nextBillingDate, today) <= 14 ? `${formatDayDate(sub.nextBillingDate, today)} · ${describeRelative(sub.nextBillingDate, today)}`
        : formatDayDate(sub.nextBillingDate, today),
      tone: overdue ? 'warning' : undefined,
    },
    {
      icon: 'bell-outline',
      label: 'Reminder',
      value: (() => {
        const on = reminderDate(sub.nextBillingDate, sub.alertDaysAdvance);
        const ahead = sub.alertDaysAdvance === 0 ? 'On the day' : `${sub.alertDaysAdvance} ${sub.alertDaysAdvance === 1 ? 'day' : 'days'} before`;
        return on >= today ? `${ahead} · ${formatDayDate(on, today)}` : `${ahead} · already passed for this charge`;
      })(),
    },
    { icon: 'credit-card-outline', label: 'Paid with', value: sub.paymentSourceLabel ?? 'Not set' },
    { icon: 'calendar-start', label: 'Started on', value: formatLongDate(sub.purchaseDate) },
    { icon: 'check-circle-outline', label: 'Last paid', value: sub.lastPaidDate ? formatLongDate(sub.lastPaidDate) : 'Not marked yet' },
  ] : [];

  // In the home currency, converted by the server (#292); a same-currency yearly figure is still useful.
  const foreign = !!sub?.homeCurrency && sub.homeCurrency.toUpperCase() !== sub.currency.toUpperCase();
  const homeLine = !sub ? null
    : sub.homeCurrencyMonthly != null && sub.homeCurrencyYearly != null
      ? foreign
        ? `≈ ${formatMoney(sub.homeCurrencyMonthly, sub.homeCurrency)} a month · ≈ ${formatMoney(sub.homeCurrencyYearly, sub.homeCurrency)} a year`
        : `${formatMoney(sub.homeCurrencyYearly, sub.homeCurrency)} a year`
      : foreign && sub.homeCurrencyAmount != null ? `≈ ${formatMoney(sub.homeCurrencyAmount, sub.homeCurrency)}` : null;

  // Paying early is fine for anything recurring; a one-time purchase only while it is outstanding.
  const canPay = !!sub && sub.isActive && (overdue || sub.cycleCadence !== 'OneTime');

  return (
    <Screen
      refreshing={loading && !!sub}
      onRefresh={reload}
      header={(
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xs, paddingVertical: space.sm, paddingHorizontal: space.sm }}>
          <IconButton icon="arrow-left" label="Back" onPress={() => router.back()} />
          <View style={{ flex: 1 }} />
          <IconButton icon="pencil-outline" label="Edit" onPress={edit} disabled={!sub} />
          <IconButton icon="dots-vertical" label="More options" onPress={() => setMenu(true)} disabled={!sub} />
        </View>
      )}
      overlay={canPay && sub ? (
        <View style={{ position: 'absolute', left: space.lg, right: space.lg, bottom: space.lg }}>
          <Button label="Mark as paid" icon="check" variant="tonal" size="lg" onPress={() => void actions.markPaid(sub)}
            loading={actions.busy === `pay:${sub.id}`} disabled={!actions.canWrite} />
        </View>
      ) : undefined}
    >
      {error && sub ? <Banner tone="error" text={error} actionLabel="Retry" onAction={reload} /> : null}
      <AsyncView
        state={asyncState({ hasData: !!sub, error, errorStatus })}
        skeleton={<FormSkeleton cards={2} />}
        error={error}
        errorStatus={errorStatus}
        onRetry={reload}
      >
        {sub ? (
          <View style={{ gap: space.md }}>
            <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.lg, paddingVertical: space.sm }}>
              <Logo url={sub.catalogLogoUrl} name={sub.customName} color={identity.ink} background={identity.soft} size={64} />
              <View style={{ flex: 1, gap: space.xs, alignItems: 'flex-start' }}>
                <Text variant="headlineSmall" accessibilityRole="header" numberOfLines={2}>{sub.customName}</Text>
                <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: space.xs }}>
                  <Pill label={sub.categoryName ?? 'Uncategorised'} icon={identity.icon} />
                  {sub.isFreeTrial ? <Pill label="Trial" tone="tertiary" /> : null}
                  {!sub.isActive ? <Pill label="Inactive" /> : null}
                </View>
              </View>
            </View>

            <Card tone="default" style={{ borderRadius: shape.large, gap: space.xs }}>
              <View style={{ flexDirection: 'row', alignItems: 'baseline', gap: space.sm, flexWrap: 'wrap' }}>
                <Text variant="amountHeadline">{formatMoney(sub.costAmount, sub.currency)}</Text>
                <Text variant="titleMedium" color={colors.onSurfaceVariant}>{PER[sub.cycleCadence]}</Text>
              </View>
              <Text variant="bodyMedium" color={colors.onSurfaceVariant}>{homeLine ?? `${CYCLE_LABEL[sub.cycleCadence]} in ${sub.currency}`}</Text>
            </Card>

            <View>
              {facts.map((fact, i) => (
                <ListItem
                  key={fact.label}
                  position={positionOf(i, facts.length)}
                  leading={<IconTile icon={fact.icon} />}
                  title={<Text variant="labelLarge" color={colors.onSurfaceVariant}>{fact.label}</Text>}
                  subtitle={<Text variant="bodyLarge" color={fact.tone === 'warning' ? colors.renewalWarning : colors.onSurface}>{fact.value}</Text>}
                />
              ))}
            </View>
          </View>
        ) : null}
      </AsyncView>

      {actions.confirmDialog}
      <ActionSheet
        title={menu && sub ? sub.customName : null}
        onClose={() => setMenu(false)}
        actions={sub ? [
          { label: 'Edit', icon: 'pencil-outline', onPress: edit },
          ...(actions.canWrite ? [{ label: 'Delete', icon: 'delete-outline' as const, destructive: true, onPress: () => actions.remove(sub, () => router.back()) }] : []),
        ] : []}
      />
    </Screen>
  );
}

function IconTile({ icon }: { icon: IconName }) {
  const { colors } = useTheme();
  return (
    <View style={{ width: 40, height: 40, borderRadius: shape.small, backgroundColor: colors.surfaceContainerHighest, alignItems: 'center', justifyContent: 'center' }}>
      <Icon name={icon} size={22} color={colors.onSurfaceVariant} />
    </View>
  );
}
