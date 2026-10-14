import { api } from './client';
import type {
  AuthTokenResponse, BurnRate, Category, PaymentSource, PaymentSourceType, ResolveResponse,
  Subscription, SubscriptionRequest, UserProfile,
} from './types';

export * from './client';
export * from './types';

const anonymous = { auth: false };

export const authApi = {
  /** 202 whether or not the address was taken - the follow-up login is what tells them apart. */
  register: (email: string, password: string) => api('POST', '/api/v1/auth/register', { email, password }, anonymous),
  login: (email: string, password: string) =>
    api<AuthTokenResponse>('POST', '/api/v1/auth/login', { email, password }, anonymous),
  /** 200 whether or not the address has an account. */
  forgotPassword: (email: string) => api('POST', '/api/v1/auth/forgot-password', { email }, anonymous),
  /** 400 covers wrong, expired, used and over-limit codes alike. */
  resetPassword: (email: string, code: string, newPassword: string) =>
    api('POST', '/api/v1/auth/reset-password', { email, code, newPassword }, anonymous),
  /** Revokes every refresh token on the account, this device's included - store the new pair. */
  changePassword: (currentPassword: string, newPassword: string) =>
    api<AuthTokenResponse>('POST', '/api/v1/auth/change-password', { currentPassword, newPassword }),
  logout: (refreshToken: string) => api('POST', '/api/v1/auth/logout', { refreshToken }),
};

export const subscriptionsApi = {
  list: () => api<Subscription[]>('GET', '/api/v1/subscriptions'),
  get: (id: string) => api<Subscription>('GET', `/api/v1/subscriptions/${id}`),
  create: (request: SubscriptionRequest) => api<Subscription>('POST', '/api/v1/subscriptions', request),
  update: (id: string, request: SubscriptionRequest) => api<Subscription>('PUT', `/api/v1/subscriptions/${id}`, request),
  remove: (id: string) => api('DELETE', `/api/v1/subscriptions/${id}`),
  /** Settles the outstanding charge; the server moves the date one cycle from the date just paid. */
  markPaid: (id: string) => api<Subscription>('POST', `/api/v1/subscriptions/${id}/mark-paid`),
  resolve: (input: string) => api<ResolveResponse>('POST', '/api/v1/subscriptions/resolve', { input }),
};

export const categoriesApi = {
  list: () => api<Category[]>('GET', '/api/v1/categories'),
  create: (name: string) => api<Category>('POST', '/api/v1/categories', { name }),
  rename: (id: string, name: string) => api<Category>('PUT', `/api/v1/categories/${id}`, { name }),
  /** Subscriptions survive; they just lose their grouping. */
  remove: (id: string) => api<{ subscriptionsUncategorized: number }>('DELETE', `/api/v1/categories/${id}`),
};

export const paymentSourcesApi = {
  list: () => api<PaymentSource[]>('GET', '/api/v1/payment-sources'),
  create: (label: string, sourceType: PaymentSourceType) =>
    api<PaymentSource>('POST', '/api/v1/payment-sources', { label, sourceType }),
  /** In place - recreating would detach every subscription that billed to it (FK is SET NULL). */
  update: (id: string, label: string, sourceType: PaymentSourceType) =>
    api<PaymentSource>('PUT', `/api/v1/payment-sources/${id}`, { label, sourceType }),
  remove: (id: string) => api('DELETE', `/api/v1/payment-sources/${id}`),
};

export const dashboardApi = {
  burnRate: () => api<BurnRate>('GET', '/api/v1/dashboard/burn-rate'),
};

export const usersApi = {
  me: () => api<UserProfile>('GET', '/api/v1/users/me'),
  updateMe: (preferredCurrency: string, defaultAlertDaysAdvance: number | null) =>
    api<UserProfile>('PUT', '/api/v1/users/me', { preferredCurrency, defaultAlertDaysAdvance }),
};
