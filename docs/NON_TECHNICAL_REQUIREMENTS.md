# SubVora — Non-Technical (Functional) Requirements

This document describes SubVora's features from a user/product perspective — what the app must do, not how it's built. For engineering details see [TECHNICAL_REQUIREMENTS.md](./TECHNICAL_REQUIREMENTS.md).

## 1. Product Summary

SubVora helps users track every subscription they pay for, understand what it's really costing them, and never get caught by a surprise renewal or forgotten free trial. Available as a native app on both Android and iOS.

## 2. Core Features (v1)

### 2.1 Logo Feature
Each subscription shows a recognizable brand logo (e.g. Netflix, Spotify) so the list is scannable at a glance instead of a wall of text. Unrecognized services fall back to a generic icon.

### 2.2 Category
Every subscription belongs to a category (Entertainment, Productivity, Fitness, Utilities, etc.), auto-suggested when possible and editable by the user. Enables category-level spend breakdowns.

### 2.3 Subscription Type
Users specify the billing cadence when adding a subscription:
- Weekly
- Monthly
- Yearly
- One-time

This drives how the subscription is projected into recurring cost totals.

### 2.4 Purchase & Expiry Dates
Users record the date they started (or will start) a subscription. The app automatically calculates and displays the next billing/expiry date based on the subscription type, and keeps rolling it forward after each renewal.

### 2.5 Alert Preferences
Users choose how far in advance they want to be reminded before a renewal or trial-end date — e.g. 1 day, 3 days, or 7 days before. Reminders arrive as push notifications. Preference can be set globally or per-subscription.

### 2.6 Source of Deduction
Users can note which payment method a subscription is billed to (e.g. "Chase card ending 4021", "PayPal"), so they can quickly see what's being charged where — useful for reconciling statements or catching subscriptions on a card they're closing.

### 2.7 Burn Rate Calculator
The main dashboard is the centerpiece of the app: it automatically aggregates every active recurring subscription and shows, at a glance:
> "You are spending **$X per week**, **$Y per month**, and **$Z per year**."

This updates live as subscriptions are added, removed, or edited — no manual recalculation needed.

### 2.8 Multi-Currency Uniformity
Users can add subscriptions priced in any currency (USD, EUR, INR, etc.) — exactly as they're billed. The app automatically converts everything into the user's chosen home currency for the burn-rate totals and dashboard, so mixed-currency subscriptions still roll up into one honest number.

## 3. Supporting Features

- **Trial tracking:** Free trials are visually flagged so users don't miss the cancel-by window before being charged.
- **Cancellation help:** Quick access to cancellation instructions or a deep link to the provider's cancellation page.
- **Cross-platform consistency:** Same experience and feature set on Android and iOS.
- **Offline access:** Users can view their subscriptions even without an internet connection.

## 4. User Experience Principles

1. **Add a subscription in under 30 seconds.** Smart-match on typed name (e.g. "netflix") should auto-fill logo, category, and suggested price where possible.
2. **Dashboard-first.** The burn rate summary is the first thing a user sees on open — the app's core value has to be visible immediately, not buried in a menu.
3. **No surprise charges.** Alerts must be reliable and timely; this is the app's primary trust promise.
4. **Currency shouldn't require mental math.** Users should never have to manually convert a foreign subscription price to understand their total spend.

## 5. Success Metrics (suggested)

- % of users who set up at least one alert.
- % of trials cancelled before conversion (self-reported or inferred).
- Time-to-add for a new subscription.
- Retention at 30/60/90 days — does the app keep getting opened to check the dashboard?

## 6. Out of Scope (v1)

- Automatic subscription discovery via bank/email scanning.
- In-app cancellation on the user's behalf.
- Budgeting/forecasting beyond subscription spend (e.g. general expense tracking).
- Web or desktop client.
