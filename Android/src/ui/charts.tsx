import { View, type ColorValue } from 'react-native';
import Svg, { Circle, G } from 'react-native-svg';
import { useTheme } from '@/theme';
import { Text } from '.';

export interface RingSegment { key: string; share: number; color: ColorValue }

/**
 * Spend by category as one ring (#284): a segment per category, in its identity colour, largest
 * first from 12 o'clock, with a small gap between segments. Replaces the single-arc brand ring,
 * whose decorative orb read as a data point. `center` sits in the hole.
 */
export function SegmentRing({ segments, size, center }: { segments: RingSegment[]; size: number; center?: React.ReactNode }) {
  const { colors } = useTheme();
  const stroke = size * 0.13;
  const r = (size - stroke) / 2;
  const circumference = 2 * Math.PI * r;
  // No gap with a single segment: a full ring with a notch would read as "almost all".
  const gap = segments.length > 1 ? Math.min(circumference * 0.012, 6) : 0;
  // Where each segment starts along the circle: the shares before it.
  const starts = segments.map((_, i) => segments.slice(0, i).reduce((sum, s) => sum + s.share * circumference, 0));
  return (
    <View style={{ width: size, height: size }}>
      <Svg width={size} height={size}>
        <Circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke={colors.surfaceContainerHighest} strokeWidth={stroke} />
        <G rotation={-90} origin={`${size / 2}, ${size / 2}`}>
          {segments.map((segment, i) => {
            const dash = Math.max(0.5, segment.share * circumference - gap);
            return (
              <Circle
                key={segment.key} cx={size / 2} cy={size / 2} r={r} fill="none" stroke={segment.color} strokeWidth={stroke}
                strokeDasharray={`${dash} ${circumference - dash}`} strokeDashoffset={-starts[i]}
              />
            );
          })}
        </G>
      </Svg>
      {center ? (
        <View style={{ position: 'absolute', top: 0, left: 0, right: 0, bottom: 0, alignItems: 'center', justifyContent: 'center', padding: stroke }}>
          {center}
        </View>
      ) : null}
    </View>
  );
}

/** One legend row: swatch, name, share. */
export function LegendRow({ color, label, value }: { color: ColorValue; label: string; value: string }) {
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8 }}>
      <View style={{ width: 10, height: 10, borderRadius: 3, backgroundColor: color }} />
      <Text variant="bodyMedium" numberOfLines={1} style={{ flex: 1 }}>{label}</Text>
      <Text variant="amountLabel">{value}</Text>
    </View>
  );
}
