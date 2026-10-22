import Storage from 'expo-sqlite/kv-store';
import { useEffect, useState } from 'react';
import { View, type ColorValue } from 'react-native';
import { SvgXml } from 'react-native-svg';
import { initialOf } from '@/lib/logic';
import { Tile, TILE_CORNER } from '.';

/**
 * Brand marks for catalog subscriptions (#278). The API sends `catalogLogoUrl`, a Simple Icons SVG
 * on jsDelivr; it is fetched at runtime and used nominatively, never bundled into the app (#145).
 * Each mark is kept on the device after the first load, so the offline mirror still shows it. The
 * icons are single-colour paths, drawn here in the category's ink on its tile, like the monogram.
 */
const memory = new Map<string, string | null>();
const inflight = new Map<string, Promise<string | null>>();
const MAX_SVG_LENGTH = 32_000;
const storageKey = (url: string) => `logo:${url}`;

async function fetchSvg(url: string): Promise<string | null> {
  try {
    const stored = await Storage.getItemAsync(storageKey(url));
    if (stored) return stored;
    const response = await fetch(url);
    if (!response.ok) return null;
    const text = await response.text();
    if (!text.trimStart().startsWith('<svg') || text.length > MAX_SVG_LENGTH) return null;
    // <title> is for browsers; the label lives on the row.
    const svg = text.replace(/<title>[\s\S]*?<\/title>/g, '');
    await Storage.setItemAsync(storageKey(url), svg).catch(() => undefined);
    return svg;
  } catch {
    return null;
  }
}

function loadSvg(url: string): Promise<string | null> {
  if (memory.has(url)) return Promise.resolve(memory.get(url) ?? null);
  let pending = inflight.get(url);
  if (!pending) {
    pending = fetchSvg(url).then((svg) => { memory.set(url, svg); inflight.delete(url); return svg; });
    inflight.set(url, pending);
  }
  return pending;
}

/** The provider's mark when there is one, else the letter tile - also while it loads and when it fails. */
export function Logo({ url, name, color, background, size = 44 }: {
  url?: string | null; name: string; color: ColorValue; background: ColorValue; size?: number;
}) {
  // Keyed by url, so a recycled row never shows the previous provider's mark while its own loads.
  const [loaded, setLoaded] = useState<{ url: string; svg: string | null } | null>(null);
  useEffect(() => {
    if (!url) return;
    let live = true;
    void loadSvg(url).then((svg) => { if (live) setLoaded({ url, svg }); });
    return () => { live = false; };
  }, [url]);
  const svg = !url ? null : loaded?.url === url ? loaded.svg : memory.get(url) ?? null;

  if (!svg) return <Tile letter={initialOf(name)} color={color} background={background} size={size} />;
  return (
    <View accessible={false} style={{ width: size, height: size, borderRadius: size * TILE_CORNER, backgroundColor: background, alignItems: 'center', justifyContent: 'center' }}>
      <SvgXml xml={svg} width={size * 0.52} height={size * 0.52} fill={color} />
    </View>
  );
}
