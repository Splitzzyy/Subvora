import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { Alert, View } from 'react-native';
import {
  categoriesApi, errorMessage, isStatus, paymentSourcesApi, subscriptionsApi, writeErrorMessage,
  type CatalogMatchCandidate, type Category, type MatchConfidenceTier, type PaymentSource, type Subscription, type SubscriptionRequest,
} from '@/api';
import { CURRENCIES, currencyOption, DEFAULT_CURRENCY } from '@/lib/currencies';
import * as haptics from '@/lib/haptics';
import { useDebounced, useOffline } from '@/lib/hooks';
import { CYCLE_LABEL, CYCLES_ORDERED, nextBillingDate, todayDateOnly, type BillingCycle } from '@/lib/logic';
import { useSession } from '@/lib/session';
import { shape, useTheme } from '@/theme';
import { Banner, Button, Card, Chip, Icon, IconButton, OfflineBanner, SelectField, Spinner, SwitchRow, Tap, Text, TextField, Tile } from '@/ui';
import { Screen } from '@/ui/layout';
import { DateField, PickerSheet, type PickerOption } from '@/ui/sheets';

const MIN_RESOLVE_LENGTH = 3;
const NONE = '__none__';
const customCurrency = (q: string) => (/^[A-Za-z]{3}$/.test(q) ? currencyOption(q.toUpperCase()) : null);

export default function SubscriptionDetail() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const editId = id && id !== 'new' ? id : null;
  const { colors, category: categoryIdentity, payment } = useTheme();
  const { dataChanged } = useSession();
  const offline = useOffline();
  const debounce = useDebounced(500);

  const today = todayDateOnly();
  const [name, setName] = useState('');
  const [cost, setCost] = useState('');
  const [currency, setCurrency] = useState(DEFAULT_CURRENCY);
  const [cycle, setCycle] = useState<BillingCycle>('Monthly');
  const [purchaseDate, setPurchaseDate] = useState(today);
  const [nextDate, setNextDate] = useState(() => nextBillingDate(today, 'Monthly'));
  // Blank sends null, which lets the server apply the user's default from Settings.
  const [alertDays, setAlertDays] = useState('');
  const [isFreeTrial, setIsFreeTrial] = useState(false);
  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [paymentSourceId, setPaymentSourceId] = useState<string | null>(null);

  const [categories, setCategories] = useState<Category[]>([]);
  const [paymentSources, setPaymentSources] = useState<PaymentSource[]>([]);
  const [suggestions, setSuggestions] = useState<CatalogMatchCandidate[]>([]);
  const [tier, setTier] = useState<MatchConfidenceTier | null>(null);

  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [picker, setPicker] = useState<'category' | 'payment' | 'currency' | null>(null);

  // Not state: nothing renders them. The catalog link and the version this edit started from.
  const catalogId = useRef<string | null>(null);
  const version = useRef<number | null>(null);
  const nameRef = useRef(name);
  useEffect(() => { nameRef.current = name; }, [name]);

  const apply = (s: Subscription) => {
    setName(s.customName);
    setCost(String(s.costAmount));
    setCurrency(s.currency);
    setCycle(s.cycleCadence);
    setPurchaseDate(s.purchaseDate);
    // The record's own date wins over anything derived from the fields above.
    setNextDate(s.nextBillingDate);
    setAlertDays(String(s.alertDaysAdvance));
    setIsFreeTrial(s.isFreeTrial);
    setCategoryId(s.categoryId);
    setPaymentSourceId(s.paymentSourceId);
    catalogId.current = s.catalogId;
    version.current = s.version;
    setSuggestions([]);
    setTier(null);
  };

  const fetchRecord = () => editId
    ? subscriptionsApi.get(editId).then(apply, (e) => {
      if (!isStatus(e, 404)) return setError(errorMessage(e));
      // Deleted elsewhere - nothing left to edit.
      dataChanged();
      router.back();
    })
    : Promise.resolve();

  useEffect(() => {
    // All three GETs are independent, so they go out together; one failing still fills the others.
    const pickers = Promise.allSettled([categoriesApi.list(), paymentSourcesApi.list()]).then(([c, p]) => {
      if (c.status === 'fulfilled') setCategories(c.value);
      if (p.status === 'fulfilled') setPaymentSources(p.value);
      const failure = [c, p].find((r) => r.status === 'rejected') as PromiseRejectedResult | undefined;
      if (failure) setError(errorMessage(failure.reason));
    });
    Promise.all([pickers, fetchRecord()]).then(() => setLoading(false));
    return debounce.cancel;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ---- Provider matching: offered, never applied until tapped ----

  const onNameChange = (value: string) => {
    setName(value);
    setSuggestions([]);
    setTier(null);
    // Editing away from an applied match invalidates it; a stale catalog link must never be saved.
    catalogId.current = null;
    // Cancel rather than just not scheduling: erasing faster than the debounce must not let the last
    // keystroke's answer put the erased name back.
    if (value.trim().length < MIN_RESOLVE_LENGTH) return debounce.cancel();
    debounce.schedule(() => void resolve(value));
  };

  const resolve = async (input: string) => {
    try {
      const result = await subscriptionsApi.resolve(input);
      // The name moved on while the request was out - this answer is about text no longer on screen.
      if (nameRef.current !== input) return;
      const offered = result.suggestions.filter((s) => s.providerName);
      if (offered.length === 0) return;
      setSuggestions(offered);
      setTier(result.tier);
    } catch (e) {
      // Debouncing is a courtesy, not a guarantee against the rate limit; a 429 degrades silently.
      if (isStatus(e, 429) || nameRef.current !== input) return;
      setError(errorMessage(e));
    }
  };

  const pickSuggestion = (s: CatalogMatchCandidate) => {
    debounce.cancel();
    setName(s.providerName);
    catalogId.current = s.catalogId;
    // Picking the provider is a statement about what this is, so its category wins.
    if (s.categoryId && categories.some((c) => c.id === s.categoryId)) setCategoryId(s.categoryId);
    setSuggestions([]);
    setTier(null);
    haptics.tick();
  };

  // ---- Dates: purchase date and cycle derive the next billing date; a hand-picked one stands ----

  const onPurchaseDate = (value: string) => { setPurchaseDate(value); setNextDate(nextBillingDate(value, cycle)); };
  const onCycle = (value: BillingCycle) => { setCycle(value); setNextDate(nextBillingDate(purchaseDate, value)); };

  // ---- Save / delete ----

  const amount = Number(cost.replace(',', '.'));
  const days = alertDays.trim() === '' ? null : Number(alertDays);
  const canSubmit = !offline && !busy && !loading;
  const valid = name.trim() !== '' && cost.trim() !== '' && Number.isFinite(amount) && amount >= 0;

  const save = async () => {
    setError(null);
    setBusy(true);
    const request: SubscriptionRequest = {
      customName: name.trim(), costAmount: amount, currency, cycleCadence: cycle, purchaseDate, nextBillingDate: nextDate,
      alertDaysAdvance: days, categoryId, paymentSourceId, catalogId: catalogId.current, isFreeTrial, version: version.current,
    };
    try {
      if (editId) await subscriptionsApi.update(editId, request);
      else await subscriptionsApi.create(request);
      haptics.confirm();
      dataChanged();
      router.back();
    } catch (e) {
      haptics.reject();
      if (isStatus(e, 409)) {
        // Changed elsewhere - most likely marked paid on another device. Show the current truth with a
        // fresh version; re-applying the edit is the user's call, not ours.
        await fetchRecord();
        setError((current) => current ?? "This subscription changed somewhere else. We've reloaded it - please check the values and save again.");
      } else {
        setError(writeErrorMessage(e));
      }
    } finally {
      setBusy(false);
    }
  };

  const remove = () => Alert.alert('Delete subscription', `Delete ${name.trim() || 'this subscription'}? This cannot be undone.`, [
    { text: 'Cancel', style: 'cancel' },
    {
      text: 'Delete', style: 'destructive', onPress: async () => {
        setError(null);
        setBusy(true);
        try {
          await subscriptionsApi.remove(editId!);
        } catch (e) {
          // Already gone is the outcome the user asked for.
          if (!isStatus(e, 404)) {
            setError(writeErrorMessage(e));
            setBusy(false);
            return;
          }
        }
        haptics.confirm();
        dataChanged();
        router.back();
      },
    },
  ]);

  // ---- Pickers ----

  const categoryOptions: PickerOption[] = [
    { value: NONE, label: 'No category', icon: 'minus-circle-outline' },
    ...categories.map((c) => {
      const identity = categoryIdentity(c.name);
      return { value: c.id, label: c.name, icon: identity.icon, iconColor: identity.color, iconBackground: identity.soft };
    }),
  ];
  const paymentOptions: PickerOption[] = [
    { value: NONE, label: 'No payment source', icon: 'minus-circle-outline' },
    ...paymentSources.map((p) => {
      const identity = payment(p.sourceType);
      return { value: p.id, label: p.label, icon: identity.icon, iconColor: identity.color, iconBackground: identity.soft };
    }),
  ];
  const currencyOptions = CURRENCIES.some((c) => c.value === currency) ? CURRENCIES : [currencyOption(currency), ...CURRENCIES];
  const selectedCategory = categories.find((c) => c.id === categoryId);
  const selectedSource = paymentSources.find((p) => p.id === paymentSourceId);
  const categoryTile = categoryIdentity(selectedCategory?.name);

  return (
    <Screen>
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8, paddingTop: 8, paddingBottom: 8 }}>
        <IconButton icon="close" label="Close" onPress={() => router.back()} />
        <Text variant="titleLarge" accessibilityRole="header" style={{ flex: 1 }}>{editId ? 'Edit subscription' : 'Add subscription'}</Text>
      </View>

      {offline ? <OfflineBanner /> : null}
      {error ? <Banner tone="error" text={error} /> : null}
      {loading && editId ? <Spinner /> : null}

      <View style={{ gap: 12, display: loading && editId ? 'none' : 'flex' }}>
        <Card>
          <Text variant="titleMedium" color={colors.primary}>Service</Text>
          <TextField label="Name" value={name} onChangeText={onNameChange} placeholder="e.g. Netflix" autoCapitalize="words" />
          {suggestions.length > 0 ? (
            <View style={{ backgroundColor: colors.surfaceContainerHigh, borderRadius: shape.lg, padding: 8, gap: 2 }}>
              <Text variant="labelMedium" color={colors.onSurfaceVariant} style={{ paddingHorizontal: 8, paddingVertical: 4 }}>
                {tier === 'AutoFill' ? 'Looks like one of these' : 'Did you mean'}
              </Text>
              {suggestions.map((s) => {
                const identity = categoryIdentity(categories.find((c) => c.id === s.categoryId)?.name);
                return (
                  <Tap key={s.catalogId} radius={shape.md} accessibilityRole="button" accessibilityLabel={`Use ${s.providerName}`} onPress={() => pickSuggestion(s)}
                    style={{ flexDirection: 'row', alignItems: 'center', gap: 12, padding: 8 }}>
                    <Tile letter={s.providerName[0]?.toUpperCase()} color={identity.color} background={identity.soft} size={32} />
                    <Text variant="bodyLarge" style={{ flex: 1 }}>{s.providerName}</Text>
                    <Icon name="arrow-top-left" size={20} color={colors.onSurfaceVariant} />
                  </Tap>
                );
              })}
            </View>
          ) : null}
          <SelectField
            label="Category" placeholder={loading ? 'Loading categories…' : 'No category'} disabled={loading}
            value={selectedCategory?.name ?? null} onPress={() => setPicker('category')}
            leading={<Tile icon={categoryTile.icon} color={categoryTile.color} background={categoryTile.soft} size={32} />}
          />
        </Card>

        <Card>
          <Text variant="titleMedium" color={colors.primary}>Cost</Text>
          <View style={{ flexDirection: 'row', gap: 8 }}>
            <TextField label="Amount" value={cost} onChangeText={setCost} keyboardType="decimal-pad" placeholder="0.00" style={{ flex: 1 }} />
            <View style={{ width: 128 }}>
              <SelectField label="Currency" placeholder="" value={currency} onPress={() => setPicker('currency')} />
            </View>
          </View>
          <Text variant="labelLarge" color={colors.onSurfaceVariant}>Billing cycle</Text>
          <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: 8 }} accessibilityRole="radiogroup">
            {CYCLES_ORDERED.map((c) => <Chip key={c} label={CYCLE_LABEL[c]} selected={cycle === c} onPress={() => onCycle(c)} />)}
          </View>
          <SelectField
            label="Payment source" placeholder={loading ? 'Loading payment sources…' : 'No payment source'} disabled={loading}
            value={selectedSource?.label ?? null} onPress={() => setPicker('payment')}
            leading={<Icon name={selectedSource ? payment(selectedSource.sourceType).icon : 'credit-card-outline'} size={22} color={colors.onSurfaceVariant} />}
          />
          {!loading && paymentSources.length === 0 ? (
            <Text variant="bodyMedium" color={colors.onSurfaceVariant}>No payment sources yet — add one on the Payment tab. Optional either way.</Text>
          ) : null}
          <SwitchRow label="Free trial" caption="Flag it so you remember to cancel before it converts." value={isFreeTrial} onValueChange={setIsFreeTrial} />
        </Card>

        <Card>
          <Text variant="titleMedium" color={colors.primary}>Dates & reminders</Text>
          <DateField label="Purchase date" value={purchaseDate} onChange={onPurchaseDate} />
          <DateField label="Next billing date" value={nextDate} onChange={setNextDate} />
          <Text variant="bodyMedium" color={colors.onSurfaceVariant}>One billing period after the purchase date. Change it if your provider bills on a different day.</Text>
          <TextField
            label="Remind me this many days ahead" value={alertDays} keyboardType="number-pad" placeholder="Your default"
            onChangeText={(v) => setAlertDays(v.replace(/\D/g, ''))}
          />
        </Card>

        <Button label={editId ? 'Save changes' : 'Save'} size="lg" icon="check" onPress={save} loading={busy} disabled={!canSubmit || !valid} />
        {editId ? <Button label="Delete subscription" icon="delete-outline" variant="danger" onPress={remove} disabled={!canSubmit} /> : null}
      </View>

      <PickerSheet visible={picker === 'category'} title="Category" options={categoryOptions} selected={categoryId ?? NONE}
        onSelect={(v) => setCategoryId(v === NONE ? null : v)} onClose={() => setPicker(null)} />
      <PickerSheet visible={picker === 'payment'} title="Payment source" options={paymentOptions} selected={paymentSourceId ?? NONE}
        onSelect={(v) => setPaymentSourceId(v === NONE ? null : v)} onClose={() => setPicker(null)} />
      <PickerSheet visible={picker === 'currency'} title="Currency" options={currencyOptions} selected={currency}
        onSelect={setCurrency} onClose={() => setPicker(null)} searchable custom={customCurrency} />
    </Screen>
  );
}
