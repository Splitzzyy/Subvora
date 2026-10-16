// Mirrors the API's JSON contract (camelCase, enums as strings, DateOnly as "yyyy-MM-dd") by
// convention - there is no shared contract package, so an API change means editing this file.
import type { BillingCycle } from '@/lib/logic';

export type { BillingCycle };

export interface AuthTokenResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}

export interface Category {
  id: string;
  name: string;
  isSystemDefault: boolean;
}

export type PaymentSourceType = 'Card' | 'BankAccount' | 'Wallet' | 'Other';

export interface PaymentSource {
  id: string;
  label: string;
  sourceType: PaymentSourceType;
}

export interface Subscription {
  id: string;
  customName: string;
  costAmount: number;
  currency: string;
  cycleCadence: BillingCycle;
  purchaseDate: string;
  nextBillingDate: string;
  lastPaidDate: string | null;
  alertDaysAdvance: number;
  /** Sent back on update so the server can answer 409 if the record moved on meanwhile. */
  version: number;
  categoryId: string | null;
  categoryName: string | null;
  paymentSourceId: string | null;
  paymentSourceLabel: string | null;
  paymentSourceType?: PaymentSourceType | null;
  catalogId: string | null;
  catalogLogoUrl: string | null;
  isFreeTrial: boolean;
  isActive: boolean;
  createdAt: string;
}

export interface SubscriptionRequest {
  /** Chosen once per create form and resent on retry, so a lost response cannot create a duplicate. Ignored on update. */
  id?: string;
  customName: string;
  costAmount: number;
  currency: string;
  cycleCadence: BillingCycle;
  purchaseDate: string;
  nextBillingDate: string;
  /** Null: "use my default" on create, "leave it alone" on update. */
  alertDaysAdvance: number | null;
  categoryId: string | null;
  paymentSourceId: string | null;
  catalogId: string | null;
  isFreeTrial: boolean;
  isActive?: boolean | null;
  /** Null on create. */
  version: number | null;
}

export type MatchConfidenceTier = 'AutoFill' | 'SuggestConfirm' | 'Manual';

export interface CatalogMatchCandidate {
  catalogId: string;
  providerName: string;
  categoryId: string | null;
  logoUrl: string | null;
  score: number;
}

export interface ResolveResponse {
  tier: MatchConfidenceTier;
  suggestions: CatalogMatchCandidate[];
}

export interface BurnRate {
  weekly: number;
  monthly: number;
  yearly: number;
  oneTimeThisYear: number;
  homeCurrency: string;
  unresolvedSubscriptionIds: string[];
  oldestRateFetchedAt: string | null;
  byCategory: { categoryId: string | null; categoryName: string; monthlyAmount: number }[];
  /** paymentSourceType is null for the unassigned bucket, and absent from an older API or cache. */
  byPaymentSource: { paymentSourceId: string | null; paymentSourceLabel: string; paymentSourceType?: PaymentSourceType | null; monthlyAmount: number }[];
}

export interface UserProfile {
  id: string;
  email: string;
  preferredCurrency: string;
  defaultAlertDaysAdvance: number | null;
}
