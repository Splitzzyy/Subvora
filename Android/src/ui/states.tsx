import { useEffect, useState } from 'react';
import { Animated, View, type DimensionValue } from 'react-native';
import { useDelayedLoading } from '@/lib/hooks';
import { errorDetail, type AsyncState } from '@/lib/loading';
import { formatSavedAt } from '@/lib/logic';
import { useReducedMotion } from '@/lib/motion';
import { motion, shape, space, useTheme } from '@/theme';
import { Banner, Button, Icon, Tap, Text, TILE_CORNER, TOUCH } from '.';
import { RingMark } from './brand';

/**
 * One screen region in one of five states. The skeleton appears only after 150 ms and then stays
 * at least 300 ms (useDelayedLoading); before that the region is blank, so quick loads never flash.
 * Reloads with content on screen never come back here: they keep the content.
 */
export function AsyncView({ state, skeleton, empty, error, errorStatus, onRetry, children }: {
  state: AsyncState;
  skeleton: React.ReactNode;
  empty?: React.ReactNode;
  error?: string | null;
  errorStatus?: number | null;
  onRetry: () => void;
  children: React.ReactNode;
}) {
  const showSkeleton = useDelayedLoading(state === 'loading');
  if (showSkeleton) return <>{skeleton}</>;
  switch (state) {
    case 'loading': return null;
    case 'empty': return <>{empty}</>;
    case 'offline': return <OfflineState onRetry={onRetry} />;
    case 'error': return <ErrorState message={error ?? 'Something went wrong.'} status={errorStatus} onRetry={onRetry} />;
    default: return <>{children}</>;
  }
}

/**
 * Over the offline mirror: "Offline" only when nothing answered (status 0 or no network); a server
 * error or rate limit with the phone online says the refresh failed instead, and offers Retry.
 */
export function CachedBanner({ savedAt, status, offline, onRetry }: { savedAt: string | null; status: number | null; offline: boolean; onRetry: () => void }) {
  const since = savedAt ? `data from ${formatSavedAt(savedAt)}` : 'last synced data';
  return offline || status === 0
    ? <Banner tone="warning" text={`Offline — showing ${since}.`} />
    : <Banner tone="warning" text={`Couldn't refresh — showing ${since}.`} actionLabel="Retry" onAction={onRetry} />;
}

/** Plain-language failure with Retry; the status sits behind "More info", never a raw error. */
export function ErrorState({ message, status, onRetry }: { message: string; status?: number | null; onRetry: () => void }) {
  const { colors } = useTheme();
  const [open, setOpen] = useState(false);
  return (
    <View accessibilityLiveRegion="polite" style={{ alignItems: 'center', paddingVertical: space.huge, paddingHorizontal: space.xxl, gap: space.md }}>
      <RingMark size={104} progress={0.3} center={<Icon name="alert-circle-outline" size={36} color={colors.error} />} />
      <Text variant="titleLargeEmphasized" style={{ textAlign: 'center' }} accessibilityRole="header">Couldn&apos;t load this</Text>
      <Text variant="bodyMedium" color={colors.onSurfaceVariant} style={{ textAlign: 'center' }}>{message}</Text>
      <Button label="Retry" icon="refresh" onPress={onRetry} />
      <Tap radius={shape.medium} accessibilityRole="button" accessibilityState={{ expanded: open }} accessibilityLabel="More info"
        onPress={() => setOpen((o) => !o)} style={{ minHeight: TOUCH, paddingHorizontal: space.md, flexDirection: 'row', alignItems: 'center', gap: space.xs }}>
        <Text variant="labelLarge" color={colors.primary}>More info</Text>
        <Icon name={open ? 'chevron-up' : 'chevron-down'} size={18} color={colors.primary} />
      </Tap>
      {open ? <Text variant="bodySmall" color={colors.onSurfaceVariant} style={{ textAlign: 'center' }}>{errorDetail(status)}</Text> : null}
    </View>
  );
}

/** Nothing cached to show and no connection: say so, offer Retry. */
export function OfflineState({ onRetry }: { onRetry: () => void }) {
  const { colors } = useTheme();
  return (
    <View accessibilityLiveRegion="polite" style={{ alignItems: 'center', paddingVertical: space.huge, paddingHorizontal: space.xxl, gap: space.md }}>
      <RingMark size={104} progress={0.3} center={<Icon name="wifi-off" size={36} color={colors.onSurfaceVariant} />} />
      <Text variant="titleLargeEmphasized" style={{ textAlign: 'center' }} accessibilityRole="header">You&apos;re offline</Text>
      <Text variant="bodyMedium" color={colors.onSurfaceVariant} style={{ textAlign: 'center' }}>
        This needs a connection to load. Retry once you&apos;re back online.
      </Text>
      <Button label="Retry" icon="refresh" variant="tonal" onPress={onRetry} />
    </View>
  );
}

// ---- Skeletons -------------------------------------------------------------------------------
// Shaped like the real layout so nothing shifts when content arrives. The shimmer is a gentle
// opacity pulse on the slow effects spring; still with reduced motion.

function Shimmer({ children }: { children: React.ReactNode }) {
  const reduced = useReducedMotion();
  const [opacity] = useState(() => new Animated.Value(1));
  useEffect(() => {
    if (reduced) return;
    const to = (toValue: number) => Animated.spring(opacity, { toValue, ...motion.effectsSlow, useNativeDriver: true });
    const loop = Animated.loop(Animated.sequence([to(0.45), to(1)]));
    loop.start();
    return () => loop.stop();
  }, [opacity, reduced]);
  return (
    <Animated.View accessible accessibilityRole="progressbar" accessibilityLabel="Loading" style={{ opacity, gap: space.md }}>
      {children}
    </Animated.View>
  );
}

function Bone({ width = '100%', height = 14, radius = space.xs }: { width?: DimensionValue; height?: number; radius?: number }) {
  const { colors } = useTheme();
  return <View style={{ width, height, borderRadius: radius, backgroundColor: colors.surfaceContainerHighest }} />;
}

function RowBone({ position }: { position: 'first' | 'middle' | 'last' | 'single' }) {
  const { colors } = useTheme();
  const top = position === 'first' || position === 'single' ? shape.large : shape.listInner;
  const bottom = position === 'last' || position === 'single' ? shape.large : shape.listInner;
  return (
    <View style={{
      flexDirection: 'row', alignItems: 'center', gap: space.lg, padding: space.lg, minHeight: 64,
      backgroundColor: colors.surfaceContainer, borderTopLeftRadius: top, borderTopRightRadius: top, borderBottomLeftRadius: bottom, borderBottomRightRadius: bottom,
    }}>
      <Bone width={44} height={44} radius={44 * TILE_CORNER} />
      <View style={{ flex: 1, gap: space.sm }}>
        <Bone width="55%" height={16} />
        <Bone width="80%" height={12} />
      </View>
    </View>
  );
}

/** Segmented list rows, optionally under a section header. */
export function ListSkeleton({ rows = 4, header = true }: { rows?: number; header?: boolean }) {
  return (
    <Shimmer>
      {header ? <View style={{ paddingTop: space.xl, paddingHorizontal: space.xs }}><Bone width="40%" height={16} /></View> : null}
      <View style={{ gap: shape.listGap }}>
        {Array.from({ length: rows }, (_, i) => <RowBone key={i} position={rows === 1 ? 'single' : i === 0 ? 'first' : i === rows - 1 ? 'last' : 'middle'} />)}
      </View>
    </Shimmer>
  );
}

function CardBone({ children, tall }: { children: React.ReactNode; tall?: boolean }) {
  const { colors } = useTheme();
  return (
    <View style={{ backgroundColor: colors.surfaceContainerLow, borderRadius: tall ? shape.extraLarge : shape.large, padding: space.xl, gap: space.md }}>
      {children}
    </View>
  );
}

/** Hero total, the ring card and a breakdown card. */
export function DashboardSkeleton() {
  return (
    <Shimmer>
      <CardBone tall>
        <Bone width="30%" />
        <Bone width="70%" height={52} radius={space.sm} />
        <Bone width="50%" />
        <Bone height={48} radius={shape.large} />
      </CardBone>
      <CardBone>
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: space.xl }}>
          <Bone width={112} height={112} radius={shape.full} />
          <View style={{ flex: 1, gap: space.sm }}><Bone width="70%" height={18} /><Bone width="90%" /></View>
        </View>
      </CardBone>
      <CardBone>
        <Bone width="45%" height={20} />
        {[0, 1, 2].map((i) => (
          <View key={i} style={{ flexDirection: 'row', alignItems: 'center', gap: space.md }}>
            <Bone width={40} height={40} radius={40 * TILE_CORNER} />
            <View style={{ flex: 1, gap: space.sm }}><Bone width="60%" /><Bone height={8} /></View>
          </View>
        ))}
      </CardBone>
    </Shimmer>
  );
}

/** Cards of fields, for the edit form and Settings. */
export function FormSkeleton({ cards = 3 }: { cards?: number }) {
  return (
    <Shimmer>
      {Array.from({ length: cards }, (_, i) => (
        <CardBone key={i}>
          <Bone width="35%" height={18} />
          <Bone height={56} radius={shape.medium} />
          <Bone height={56} radius={shape.medium} />
        </CardBone>
      ))}
    </Shimmer>
  );
}
