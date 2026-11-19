import { Category, Preset, Rule, RuleRequest } from './models';

/** What the rule form holds. Lists are typed as comma separated text because that is how people type them. */
export interface RuleFormModel {
  name: string;
  category: Category;
  minSeverity: number;
  cooldownMinutes: number;
  isEnabled: boolean;
  channelIds: string[];
  // news
  includeKeywords: string;
  excludeKeywords: string;
  sourceKeys: string;
  // disasters
  minMagnitude: number | null;
  region: string;
  eventTypes: string;
  // markets
  tickers: string;
  minPercentMove: number | null;
  direction: 'Either' | 'Up' | 'Down';
}

export function emptyForm(): RuleFormModel {
  return {
    name: '', category: 'Disasters', minSeverity: 1, cooldownMinutes: 0, isEnabled: true, channelIds: [],
    includeKeywords: '', excludeKeywords: '', sourceKeys: '',
    minMagnitude: null, region: '', eventTypes: '',
    tickers: '', minPercentMove: null, direction: 'Either',
  };
}

/** "a, b\nc" becomes ["a","b","c"]. Blanks are dropped and repeats (ignoring case) are removed. */
export function parseList(text: string | null | undefined): string[] {
  const seen = new Set<string>();
  const result: string[] = [];
  for (const part of (text ?? '').split(/[,\n;]/)) {
    const item = part.trim();
    if (item && !seen.has(item.toLowerCase())) { seen.add(item.toLowerCase()); result.push(item); }
  }
  return result;
}

/**
 * Only the fields of the chosen category are sent. The API rejects filters that do not belong to the
 * category, so leftovers from a previously selected category must never leak into the request.
 */
export function buildFilters(m: RuleFormModel): Record<string, unknown> {
  switch (m.category) {
    case 'News':
      return { includeKeywords: parseList(m.includeKeywords), excludeKeywords: parseList(m.excludeKeywords), sourceKeys: parseList(m.sourceKeys) };
    case 'Disasters': {
      const f: Record<string, unknown> = { eventTypes: parseList(m.eventTypes) };
      if (m.minMagnitude !== null && !Number.isNaN(m.minMagnitude)) f['minMagnitude'] = m.minMagnitude;
      if (m.region.trim()) f['region'] = m.region.trim();
      return f;
    }
    case 'Markets':
      return { tickers: parseList(m.tickers).map((t) => t.toUpperCase()), minPercentMove: m.minPercentMove ?? 0, direction: m.direction };
  }
}

export function toRequest(m: RuleFormModel): RuleRequest {
  return {
    name: m.name.trim(),
    category: m.category,
    minSeverity: m.minSeverity,
    filters: buildFilters(m),
    cooldownSeconds: Math.round((m.cooldownMinutes || 0) * 60),
    isEnabled: m.isEnabled,
    channelIds: [...m.channelIds],
  };
}

type Loose = Record<string, unknown>;
const list = (v: unknown): string => (Array.isArray(v) ? v.join(', ') : '');
const num = (v: unknown): number | null => (typeof v === 'number' ? v : null);
const str = (v: unknown): string => (typeof v === 'string' ? v : '');

function applyFilters(m: RuleFormModel, filters: unknown): RuleFormModel {
  const f = (filters ?? {}) as Loose;
  switch (m.category) {
    case 'News':
      return { ...m, includeKeywords: list(f['includeKeywords']), excludeKeywords: list(f['excludeKeywords']), sourceKeys: list(f['sourceKeys']) };
    case 'Disasters':
      return { ...m, minMagnitude: num(f['minMagnitude']), region: str(f['region']), eventTypes: list(f['eventTypes']) };
    case 'Markets': {
      const d = f['direction'];
      return { ...m, tickers: list(f['tickers']), minPercentMove: num(f['minPercentMove']), direction: d === 'Up' || d === 'Down' ? d : 'Either' };
    }
  }
}

export function fromRule(r: Rule): RuleFormModel {
  return applyFilters({
    ...emptyForm(), name: r.name, category: r.category, minSeverity: r.minSeverity,
    cooldownMinutes: r.cooldownSeconds / 60, isEnabled: r.isEnabled, channelIds: [...r.channelIds],
  }, r.filters);
}

/** A preset fills in everything except the name and the channels the user picks. */
export function applyPreset(current: RuleFormModel, p: Preset): RuleFormModel {
  const t = p.template;
  return applyFilters({
    ...emptyForm(), name: current.name || p.name, category: t.category, minSeverity: t.minSeverity,
    cooldownMinutes: t.cooldownSeconds / 60, isEnabled: current.isEnabled, channelIds: current.channelIds,
  }, t.filters);
}

/** Quick checks to help the user before a round trip. The server still decides. */
export function check(m: RuleFormModel, requireChannels: boolean): string[] {
  const problems: string[] = [];
  if (!m.name.trim()) problems.push('Give the rule a name.');
  if (m.category === 'Markets' && parseList(m.tickers).length === 0) problems.push('Enter at least one ticker, for example BTC.');
  if (requireChannels && m.channelIds.length === 0) problems.push('Choose at least one channel to notify.');
  if (m.minSeverity < 1 || m.minSeverity > 5) problems.push('Minimum severity must be between 1 and 5.');
  if (m.cooldownMinutes < 0) problems.push('Cooldown cannot be negative.');
  return problems;
}
