// Config plugin: gives the native Android theme the brand accent, so the system dialogs the app
// still opens (the date picker) match the palette instead of AppCompat's default teal. Colours are
// read from the generated palette, light and dark, so they cannot drift from the app's own.
const { AndroidConfig, withAndroidColors, withAndroidColorsNight, withAndroidStyles } = require('expo/config-plugins');
const { readFileSync } = require('fs');
const { join } = require('path');

function primaries(projectRoot) {
  const palette = readFileSync(join(projectRoot, 'src', 'theme', 'palette.generated.ts'), 'utf8');
  const block = (name) => palette.slice(palette.indexOf(`export const ${name} =`)).match(/"primary": "(#[0-9A-F]{6})"/)[1];
  return { light: block('light'), dark: block('dark') };
}

// colorPrimary as well: Expo's default (#023c69) shows in the date picker's header.
const setColor = (colors, value) => ['colorAccent', 'colorPrimary']
  .reduce((xml, name) => AndroidConfig.Colors.assignColorValue(xml, { name, value }), colors);

module.exports = function withBrandAccent(config) {
  const { light, dark } = primaries(config._internal?.projectRoot ?? process.cwd());
  config = withAndroidColors(config, (c) => { c.modResults = setColor(c.modResults, light); return c; });
  config = withAndroidColorsNight(config, (c) => { c.modResults = setColor(c.modResults, dark); return c; });
  return withAndroidStyles(config, (c) => {
    c.modResults = AndroidConfig.Styles.assignStylesValue(c.modResults, {
      add: true, parent: AndroidConfig.Styles.getAppThemeGroup(), name: 'colorAccent', value: '@color/colorAccent',
    });
    return c;
  });
};
