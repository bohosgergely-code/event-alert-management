import type { components } from './api-schema';

// Friendly names for the generated API types. The generated file is never edited by hand:
// run `npm run generate:api` after the API changes.
type S = components['schemas'];

export type Category = S['EventCategory'];
export type NotificationStatus = S['NotificationStatus'];
export type Channel = S['ChannelResponse'];
export type Rule = S['RuleResponse'];
export type RuleRequest = S['RuleRequest'];
export type Preset = S['PresetResponse'];
export type Preview = S['PreviewResponse'];
export type EventSummary = S['EventSummary'];
export type Notification = S['NotificationResponse'];
export type AdminNotification = S['AdminNotificationResponse'];
export type Attempt = S['AttemptResponse'];
export type Source = S['SourceResponse'];
export type ChannelType = S['ChannelTypeResponse'];
export type Overview = S['OverviewResponse'];
export type InjectRequest = S['InjectEventRequest'];
export type InjectResult = S['InjectEventResponse'];
export type TokenResponse = S['TokenResponse'];
export type AdminUser = S['AdminUserResponse'];
export type AdminRule = S['AdminRuleResponse'];
export type TestResult = S['TestResultResponse'];
export type PollResult = S['PollResponse'];
export type Paged<T> = { items: T[]; total: number; page: number; pageSize: number };

export const CATEGORIES: Category[] = ['News', 'Markets', 'Disasters'];
export const STATUSES: NotificationStatus[] = ['Pending', 'Sending', 'Retrying', 'Sent', 'Failed', 'Suppressed'];
