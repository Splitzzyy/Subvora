import { currencySymbol } from './logic';

/** The fallback the API applies too. */
export const DEFAULT_CURRENCY = 'INR';

/**
 * Common ISO-4217 codes. Hermes has no Intl.DisplayNames to derive this at runtime the way the
 * MAUI app did, so the picker also accepts any typed three-letter code - the API is the one that
 * validates it.
 */
const NAMES: Record<string, string> = {
  AED: 'UAE Dirham', ARS: 'Argentine Peso', AUD: 'Australian Dollar', BDT: 'Bangladeshi Taka',
  BRL: 'Brazilian Real', CAD: 'Canadian Dollar', CHF: 'Swiss Franc', CLP: 'Chilean Peso',
  CNY: 'Chinese Yuan', COP: 'Colombian Peso', CZK: 'Czech Koruna', DKK: 'Danish Krone',
  EGP: 'Egyptian Pound', EUR: 'Euro', GBP: 'British Pound', HKD: 'Hong Kong Dollar',
  HUF: 'Hungarian Forint', IDR: 'Indonesian Rupiah', ILS: 'Israeli New Shekel', INR: 'Indian Rupee',
  JPY: 'Japanese Yen', KES: 'Kenyan Shilling', KRW: 'South Korean Won', KWD: 'Kuwaiti Dinar',
  KZT: 'Kazakhstani Tenge', LKR: 'Sri Lankan Rupee', MAD: 'Moroccan Dirham', MXN: 'Mexican Peso',
  MYR: 'Malaysian Ringgit', NGN: 'Nigerian Naira', NOK: 'Norwegian Krone', NPR: 'Nepalese Rupee',
  NZD: 'New Zealand Dollar', PEN: 'Peruvian Sol', PHP: 'Philippine Peso', PKR: 'Pakistani Rupee',
  PLN: 'Polish Zloty', QAR: 'Qatari Riyal', RON: 'Romanian Leu', RUB: 'Russian Ruble',
  SAR: 'Saudi Riyal', SEK: 'Swedish Krona', SGD: 'Singapore Dollar', THB: 'Thai Baht',
  TRY: 'Turkish Lira', TWD: 'New Taiwan Dollar', UAH: 'Ukrainian Hryvnia', USD: 'US Dollar',
  VND: 'Vietnamese Dong', ZAR: 'South African Rand',
};

export interface CurrencyOption { value: string; label: string; caption: string }

export const currencyOption = (code: string): CurrencyOption => ({
  value: code,
  label: `${code} — ${NAMES[code] ?? 'Other currency'}`,
  caption: currencySymbol(code),
});

export const CURRENCIES = Object.keys(NAMES).map(currencyOption);
