import { randomUUID } from 'expo-crypto';
import { router, useLocalSearchParams, useNavigation } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { View } from 'react-native';
import {
  ApiError, categoriesApi, errorMessage, isStatus, paymentSourcesApi, subscriptionsApi, usersApi, writeErrorMessage,
  type CatalogMatchCandidate, type Category, type MatchConfidenceTier, type PaymentSource, type Subscription, type SubscriptionRequest,
} from '@/api';
import { CURRENCIES, currencyOption, DEFAULT_CURRENCY } from '@/lib/currencies';
import * as haptics from '@/lib/haptics';
import { useDebounced, useOffline } from '@/lib/hooks';
import { asyncState } from '@/lib/loading';
import { CYCLE_LABEL, CYCLES_ORDERED, nextBillingDate, todayDateOnly, type BillingCycle } from '@/lib/logic';
import { useSession } from '@/lib/session';
import { shape, space, useTheme } from '@/theme';
import { Banner, Button, Card, Chip, Icon, IconButton, OfflineBanner, ProgressBar, SelectField, SwitchRow, Tap, Text, TextField, Tile } from '@/ui';
import { Screen } from '@/ui/layout';
import { DateField, PickerSheet, useConfirm, type PickerOption } from '@/ui/sheets';
import { useSnackbar } from '@/ui/snackbar';
import { AsyncView, FormSkeleton } from '@/ui/states';

const MIN_RESOLVE_LENGTH = 3;
const NONE = '__none__';
const customCurrency = (q: string) => (/^[A-Za-z]{3}$/.test(q) ? currencyOption(q.toUpperCase()) : null);

/** The editable values, as one comparable key: the form is dirty when it differs from the loaded one. */
const formKey = (...values: unknown[]) => JSON.stringify(values);

export default function SubscriptionDetail() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const editId = id && id !== 'new' ? id : null;
  const { colors, category: categoryIdentity, payment } = useTheme();
  const { dataChanged } = useSession();
  const offline = useOffline();
  const snackbar = useSnackbar();
  const navigation = useNavigation();
  const [confirmDialog, confirm] = useConfirm();
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
  /** The record itself failed to load: nothing to edit, so the whole form gives way to an error state. */
  const [loadFailure, setLoadFailure] = useState<{ message: string; status: number | null } | null>(null);
  const [picker, setPicker] = useState<'category' | 'payment' | 'currency' | null>(null);
  /** Shown once Save is pressed with these missing; Save stays enabled so it can say what is wrong. */
  const [fieldErrors, setFieldErrors] = useState<{ name?: string; cost?: string }>({});
  /** Set before leaving on purpose (saved, deleted, discarded) so the unsaved-changes guard lets go. */
  const leaving = useRef(false);
  // The user's home currency, for a new subscription, unless they have picked one themselves.
  const currencyTouched = useRef(false);

  // Not state: nothing renders them. The catalog link and the version this edit started from.
  const catalogId = useRef<string | null>(null);
  const version = useRef<number | null>(null);
  // Fixed for the life of a create form, so tapping Save again after a timeout replays the create
  // the server may already have made rather than adding a second copy.
  const [createId] = useState(randomUUID);
  const [pristine, setPristine] = useState(() => formKey('', '', DEFAULT_CURRENCY, 'Monthly', today, nextBillingDate(today, 'Monthly'), '', false, null, null));
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
    setPristine(formKey(s.customName, String(s.costAmount), s.currency, s.cycleCadence, s.purchaseDate, s.nextBillingDate,
      String(s.alertDaysAdvance), s.isFreeTrial, s.categoryId, s.paymentSourceId));
  };

  /** `initial`: a failure replaces the form (there is nothing to edit); later reloads report in the banner. */
  const fetchRecord = (initial = false) => editId
    ? subscriptionsApi.get(editId).then(apply, (e) => {
      if (!isStatus(e, 404)) {
        const message = errorMessage(e);
        return initial ? setLoadFailure({ message, status: e instanceof ApiError ? e.status : null }) : setError(message);
      }
      // Deleted elsewhere - nothing left to edit.
      dataChanged();
      router.back();
    })
    : Promise.resolve();

  const load = () => {
    // All three GETs are independent, so they go out together; one failing still fills the others.
    const pickers = Promise.allSettled([categoriesApi.list(), paymentSourcesApi.list()]).then(([c, p]) => {
      if (c.status === 'fulfilled') setCategories(c.value);
      if (p.status === 'fulfilled') setPaymentSources(p.value);
      const failure = [c, p].find((r) => r.status === 'rejected') as PromiseRejectedResult | undefined;
      if (failure) setError(errorMessage(failure.reason));
    });
    // A new subscription starts in the user's home currency, not the app-wide default.
    const home = editId ? Promise.resolve() : usersApi.me().then((me) => {
      if (currencyTouched.current || !me.preferredCurrency) return;
      setCurrency(me.preferredCurrency);
      setPristine(formKey('', '', me.preferredCurrency, 'Monthly', today, nextBillingDate(today, 'Monthly'), '', false, null, null));
    }, () => undefined);
    Promise.all([pickers, fetchRecord(true), home]).then(() => setLoading(false));
  };

  const retry = () => {
    setLoading(true);
    setLoadFailure(null);
    setError(null);
    load();
  };

  useEffect(() => {
    load();
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

  // ---- Leaving with unsaved changes ----

  const dirty = formKey(name, cost, currency, cycle, purchaseDate, nextDate, alertDays, isFreeTrial, categoryId, paymentSourceId) !== pristine;

  // Close, Android back and the modal swipe all come through here: confirm before dropping edits.
  useEffect(() => navigation.addListener('beforeRemove', (e) => {
    if (!dirty || leaving.current) return;
    e.preventDefault();
    confirm({
      title: 'Discard changes?', body: "What you've entered here hasn't been saved.", confirmLabel: 'Discard', destructive: true,
      onConfirm: () => { leaving.current = true; navigation.dispatch(e.data.action); },
    });
  }), [navigation, dirty, confirm]);

  // ---- Save / delete ----

  const amount = Number(cost.replace(',', '.'));
  const days = alertDays.trim() === '' ? null : Number(alertDays);
  const canSubmit = !offline && !busy && !loading;

  const save = async () => {
    // Say what is missing rather than greying the button out: a disabled Save explains nothing.
    const problems = {
      name: name.trim() === '' ? 'Enter a name.' : undefined,
      cost: cost.trim() === '' || !Number.isFinite(amount) || amount < 0 ? 'Enter an amount, e.g. 9.99.' : undefined,
    };
    setFieldErrors(problems);
    if (problems.name || problems.cost) {
      haptics.reject();
      return;
    }
    setError(null);
    setBusy(true);
    const request: SubscriptionRequest = {
      customName: name.trim(), costAmount: amount, currency, cycleCadence: cycle, purchaseDate, nextBillingDate: nextDate,
      alertDaysAdvance: days, categoryId, paymentSourceId, catalogId: catalogId.current, isFreeTrial, version: version.current,
    };
    try {
      if (editId) await subscriptionsApi.update(editId, request);
      else await subscriptionsApi.create({ ...request, id: createId });
      haptics.confirm();
      dataChanged();
      snackbar({ text: editId ? 'Changes saved.' : `${request.customName} added.` });
      leaving.current = true;
      router.back();
    } catch (e) {
      haptics.reject();
      if (editId && isStatus(e, 409)) {
        // Changed elsewhere - most likely marked paid on another device. Show the current truth with a
        // fresh version; re-applying the edit is the user's call, not ours.
        await fetchRecord();
        setError((current) => current ?? "This subscription changed somewhere else. We've reloaded it - please check the values and save again.");
      } else if (isStatus(e, 409)) {
        // A create replayed with this form's id after it was changed: the earlier save went through
        // with the old values, and the server will not pass it off as this one.
        dataChanged();
        setError('Your earlier save went through before this change. Go back and edit it from the list.');
      } else {
        setError(writeErrorMessage(e));
      }
    } finally {
      setBusy(false);
    }
  };

  const remove = () => confirm({
    title: 'Delete subscription', body: `Delete ${name.trim() || 'this subscription'}? This cannot be undone.`,
    confirmLabel: 'Delete', destructive: true,
    onConfirm: async () => {
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
      snackbar({ text: `${name.trim() || 'Subscription'} deleted.` });
      leaving.current = true;
      router.back();
    },
  });

  // ---- Pickers ----

  const categoryOptions: PickerOption[] = [
    { value: NONE, label: 'No category', icon: 'minus-circle-outline' },
    ...categories.map((c) => {
      const identity = categoryIdentity(c.name);
      return { value: c.id, label: c.name, icon: identity.icon, iconColor: identity.ink, iconBackground: identity.soft };
    }),
  ];
  const paymentOptions: PickerOption[] = [
    { value: NONE, label: 'No payment source', icon: 'minus-circle-outline' },
    ...paymentSources.map((p) => {
      const identity = payment(p.sourceType);
      return { value: p.id, label: p.label, icon: identity.icon, iconColor: identity.ink, iconBackground: identity.soft };
    }),
  ];
  const currencyOptions = CURRENCIES.some((c) => c.value === currency) ? CURRENCIES : [currencyOption(currency), ...CURRENCIES];
  const selectedCategory = categories.find((c) => c.id === categoryId);
  const selectedSource = paymentSources.find((p) => p.id === paymentSourceId);
  const categoryTile = categoryIdentity(selectedCategory?.name);

  return (
    <Screen header={(
      // M3 full-screen dialog: close and the confirming action stay in reach above the scrolling form.
      <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.sm, paddingVertical: space.sm, paddingHorizontal: space.lg }}>
        <IconButton icon="close" label="Close" onPress={() => router.back()} />
        <Text variant="titleLargeEmphasized" accessibilityRole="header" style={{ flex: 1 }} numberOfLines={1}>{editId ? 'Edit subscription' : 'Add subscription'}</Text>
        <Button label="Save" variant="text" onPress={save} loading={busy} disabled={!canSubmit} />
      </View>
    )}>

      {offline ? <OfflineBanner /> : null}
      {error ? <Banner tone="error" text={error} /> : null}
      {/* A new form is usable at once; only the category and payment pickers are still filling. */}
      {loading && !editId ? <View style={{ paddingBottom: space.md }}><ProgressBar /></View> : null}

      <AsyncView
        state={editId ? asyncState({ hasData: !loading && !loadFailure, error: loadFailure?.message, errorStatus: loadFailure?.status }) : 'content'}
        skeleton={<FormSkeleton />}
        error={loadFailure?.message}
        errorStatus={loadFailure?.status}
        onRetry={retry}
      >
        <View style={{ gap: space.md }}>
          <Card>
            <Text variant="titleMedium" color={colors.primary}>Service</Text>
            <TextField label="Name" value={name} onChangeText={(v) => { onNameChange(v); setFieldErrors((f) => ({ ...f, name: undefined })); }} placeholder="e.g. Netflix" autoCapitalize="words" error={fieldErrors.name} />
            {suggestions.length > 0 ? (
              <View style={{ backgroundColor: colors.surfaceContainerHigh, borderRadius: shape.medium, padding: space.sm, gap: space.xxs }}>
                <Text variant="labelMedium" color={colors.onSurfaceVariant} style={{ paddingHorizontal: space.sm, paddingVertical: space.xs }}>
                  {tier === 'AutoFill' ? 'Looks like one of these' : 'Did you mean'}
                </Text>
                {suggestions.map((s) => {
                  const identity = categoryIdentity(categories.find((c) => c.id === s.categoryId)?.name);
                  return (
                    <Tap key={s.catalogId} radius={shape.small} accessibilityRole="button" accessibilityLabel={`Use ${s.providerName}`} onPress={() => pickSuggestion(s)}
                      style={{ flexDirection: 'row', alignItems: 'center', gap: space.md, padding: space.sm, minHeight: 48 }}>
                      <Tile letter={s.providerName[0]?.toUpperCase()} color={identity.ink} background={identity.soft} size={32} />
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
              leading={<Tile icon={categoryTile.icon} color={categoryTile.ink} background={categoryTile.soft} size={32} />}
            />
          </Card>

          <Card>
            <Text variant="titleMedium" color={colors.primary}>Cost</Text>
            <View style={{ flexDirection: 'row', gap: space.sm }}>
              <TextField label="Amount" value={cost} onChangeText={(v) => { setCost(v); setFieldErrors((f) => ({ ...f, cost: undefined })); }} keyboardType="decimal-pad" placeholder="0.00" style={{ flex: 1 }} error={fieldErrors.cost} />
              <View style={{ width: 128 }}>
                <SelectField label="Currency" placeholder="" value={currency} onPress={() => setPicker('currency')} />
              </View>
            </View>
            <Text variant="labelLarge" color={colors.onSurfaceVariant}>Billing cycle</Text>
            <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: space.sm }} accessibilityRole="radiogroup">
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
            {/* Only while the date is still the derived one: not once picked by hand, or on a record whose date is its own. */}
            {nextDate === nextBillingDate(purchaseDate, cycle) ? (
              <Text variant="bodyMedium" color={colors.onSurfaceVariant}>One billing period after the purchase date. Change it if your provider bills on a different day.</Text>
            ) : null}
            <TextField
              label="Remind me this many days ahead" value={alertDays} keyboardType="number-pad" placeholder="Your default"
              onChangeText={(v) => setAlertDays(v.replace(/\D/g, ''))}
            />
          </Card>

          <Button label={editId ? 'Save changes' : 'Save'} size="lg" icon="check" onPress={save} loading={busy} disabled={!canSubmit} />
          {editId ? <Button label="Delete subscription" icon="delete-outline" variant="danger" onPress={remove} disabled={!canSubmit} /> : null}
        </View>
      </AsyncView>

      <PickerSheet visible={picker === 'category'} title="Category" options={categoryOptions} selected={categoryId ?? NONE}
        onSelect={(v) => setCategoryId(v === NONE ? null : v)} onClose={() => setPicker(null)} />
      <PickerSheet visible={picker === 'payment'} title="Payment source" options={paymentOptions} selected={paymentSourceId ?? NONE}
        onSelect={(v) => setPaymentSourceId(v === NONE ? null : v)} onClose={() => setPicker(null)} />
      <PickerSheet visible={picker === 'currency'} title="Currency" options={currencyOptions} selected={currency}
        onSelect={(v) => { currencyTouched.current = true; setCurrency(v); }} onClose={() => setPicker(null)} searchable custom={customCurrency} />
      {confirmDialog}
    </Screen>
  );
}
