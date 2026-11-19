import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AdminNotification, AdminRule, AdminUser, Category, Channel, ChannelType, EventSummary, InjectRequest, InjectResult,
  Notification, NotificationStatus, Overview, Paged, PollResult, Preset, Preview, Rule, RuleRequest, Source, TestResult,
} from './models';

/** Every call to the API goes through here, so paths and shapes live in exactly one place. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  // ---- channels ----
  channels(): Observable<Channel[]> { return this.http.get<Channel[]>('/api/channels'); }
  channelTypes(): Observable<string[]> { return this.http.get<string[]>('/api/channels/types'); }
  createChannel(body: { type: string; name: string; settings: Record<string, unknown> }): Observable<Channel> {
    return this.http.post<Channel>('/api/channels', body);
  }
  updateChannel(id: string, body: { name?: string; isEnabled?: boolean; settings?: Record<string, unknown> }): Observable<Channel> {
    return this.http.put<Channel>(`/api/channels/${id}`, body);
  }
  deleteChannel(id: string): Observable<void> { return this.http.delete<void>(`/api/channels/${id}`); }
  testChannel(id: string): Observable<TestResult> { return this.http.post<TestResult>(`/api/channels/${id}/test`, {}); }

  // ---- rules ----
  rules(): Observable<Rule[]> { return this.http.get<Rule[]>('/api/rules'); }
  rule(id: string): Observable<Rule> { return this.http.get<Rule>(`/api/rules/${id}`); }
  presets(): Observable<Preset[]> { return this.http.get<Preset[]>('/api/rules/presets'); }
  createRule(body: RuleRequest): Observable<Rule> { return this.http.post<Rule>('/api/rules', body); }
  updateRule(id: string, body: RuleRequest): Observable<Rule> { return this.http.put<Rule>(`/api/rules/${id}`, body); }
  deleteRule(id: string): Observable<void> { return this.http.delete<void>(`/api/rules/${id}`); }
  previewRule(body: RuleRequest): Observable<Preview> { return this.http.post<Preview>('/api/rules/preview', body); }

  // ---- history ----
  notifications(status: NotificationStatus | '', page: number, pageSize = 20): Observable<Paged<Notification>> {
    return this.http.get<Paged<Notification>>('/api/notifications', { params: query({ status, page, pageSize }) });
  }

  // ---- admin ----
  overview(): Observable<Overview> { return this.http.get<Overview>('/api/admin/overview'); }
  adminEvents(f: { category: Category | ''; simulated: '' | 'true' | 'false'; page: number }): Observable<Paged<EventSummary>> {
    return this.http.get<Paged<EventSummary>>('/api/admin/events', { params: query({ ...f, pageSize: 20 }) });
  }
  inject(body: InjectRequest): Observable<InjectResult> { return this.http.post<InjectResult>('/api/admin/events/inject', body); }
  adminNotifications(f: { status: NotificationStatus | ''; userEmail: string; channelType: string; page: number }): Observable<Paged<AdminNotification>> {
    return this.http.get<Paged<AdminNotification>>('/api/admin/notifications', { params: query({ ...f, pageSize: 20 }) });
  }
  retry(id: string): Observable<void> { return this.http.post<void>(`/api/admin/notifications/${id}/retry`, {}); }
  adminRules(): Observable<AdminRule[]> { return this.http.get<AdminRule[]>('/api/admin/rules'); }
  adminUsers(): Observable<AdminUser[]> { return this.http.get<AdminUser[]>('/api/admin/users'); }
  sources(): Observable<Source[]> { return this.http.get<Source[]>('/api/admin/sources'); }
  updateSource(id: string, body: { isEnabled?: boolean; pollIntervalSeconds?: number }): Observable<Source> {
    return this.http.put<Source>(`/api/admin/sources/${id}`, body);
  }
  pollSource(id: string): Observable<PollResult> { return this.http.post<PollResult>(`/api/admin/sources/${id}/poll`, {}); }
  adminChannelTypes(): Observable<ChannelType[]> { return this.http.get<ChannelType[]>('/api/admin/channel-types'); }
  setChannelType(type: string, isEnabled: boolean): Observable<ChannelType> {
    return this.http.put<ChannelType>(`/api/admin/channel-types/${encodeURIComponent(type)}`, { isEnabled });
  }
}

/** Builds query parameters and leaves out empty filters. */
export function query(values: Record<string, string | number | boolean | null | undefined>): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(values)) {
    if (value !== '' && value !== null && value !== undefined) params = params.set(key, String(value));
  }
  return params;
}
