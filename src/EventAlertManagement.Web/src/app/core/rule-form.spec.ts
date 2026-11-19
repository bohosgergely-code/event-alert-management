import { applyPreset, buildFilters, check, emptyForm, fromRule, parseList, RuleFormModel, toRequest } from './rule-form';
import { Preset, Rule } from './models';

const base = (patch: Partial<RuleFormModel>): RuleFormModel => ({ ...emptyForm(), name: 'r', channelIds: ['c1'], ...patch });

describe('parseList', () => {
  it('splits on commas, semicolons and new lines and trims', () => {
    expect(parseList(' a, b ;c\nd ')).toEqual(['a', 'b', 'c', 'd']);
  });
  it('drops blanks and repeats ignoring case but keeps the first spelling', () => {
    expect(parseList('Storm,, storm , ,STORM,flood')).toEqual(['Storm', 'flood']);
  });
  it('copes with empty and missing input', () => {
    expect(parseList('')).toEqual([]);
    expect(parseList(null)).toEqual([]);
    expect(parseList(undefined)).toEqual([]);
  });
});

describe('buildFilters sends only the fields of the chosen category', () => {
  const dirty = base({
    includeKeywords: 'storm', excludeKeywords: 'sport', sourceKeys: 'bbc',
    minMagnitude: 6, region: ' Japan ', eventTypes: 'earthquake',
    tickers: 'btc, eth', minPercentMove: 5, direction: 'Down',
  });

  it('news', () => {
    expect(buildFilters({ ...dirty, category: 'News' })).toEqual({ includeKeywords: ['storm'], excludeKeywords: ['sport'], sourceKeys: ['bbc'] });
  });
  it('disasters', () => {
    expect(buildFilters({ ...dirty, category: 'Disasters' })).toEqual({ eventTypes: ['earthquake'], minMagnitude: 6, region: 'Japan' });
  });
  it('markets upper-cases tickers', () => {
    expect(buildFilters({ ...dirty, category: 'Markets' })).toEqual({ tickers: ['BTC', 'ETH'], minPercentMove: 5, direction: 'Down' });
  });
  it('leaves out an empty magnitude and an empty region instead of sending null or blank', () => {
    const f = buildFilters(base({ category: 'Disasters', minMagnitude: null, region: '   ' }));
    expect(f).toEqual({ eventTypes: [] });
    expect('minMagnitude' in f).toBe(false);
  });
  it('keeps a magnitude of zero (zero is a value, not "empty")', () => {
    expect(buildFilters(base({ category: 'Disasters', minMagnitude: 0 }))['minMagnitude']).toBe(0);
  });
});

describe('toRequest', () => {
  it('converts minutes to seconds and trims the name', () => {
    const r = toRequest(base({ name: '  My rule ', cooldownMinutes: 1.5 }));
    expect(r.cooldownSeconds).toBe(90);
    expect(r.name).toBe('My rule');
  });
  it('treats an empty cooldown as none', () => {
    expect(toRequest(base({ cooldownMinutes: NaN })).cooldownSeconds).toBe(0);
  });
  it('copies the channel list rather than sharing it', () => {
    const m = base({});
    const r = toRequest(m);
    r.channelIds.push('other');
    expect(m.channelIds).toEqual(['c1']);
  });
});

describe('fromRule round trip', () => {
  const rule = (category: Rule['category'], filters: unknown): Rule => ({
    id: 'id', name: 'n', category, minSeverity: 3, filters, cooldownSeconds: 1800, isEnabled: false, channelIds: ['a', 'b'],
  });

  it('news', () => {
    const m = fromRule(rule('News', { includeKeywords: ['x', 'y'], excludeKeywords: [], sourceKeys: ['bbc'], version: 1 }));
    expect(m.includeKeywords).toBe('x, y');
    expect(toRequest(m).filters).toEqual({ includeKeywords: ['x', 'y'], excludeKeywords: [], sourceKeys: ['bbc'] });
  });
  it('disasters', () => {
    const m = fromRule(rule('Disasters', { minMagnitude: 6.5, region: 'Chile', eventTypes: ['earthquake'], version: 1 }));
    expect(m.minMagnitude).toBe(6.5);
    expect(toRequest(m).filters).toEqual({ minMagnitude: 6.5, region: 'Chile', eventTypes: ['earthquake'] });
  });
  it('markets', () => {
    const m = fromRule(rule('Markets', { tickers: ['BTC'], minPercentMove: 4.5, direction: 'Up', version: 1 }));
    expect(m.direction).toBe('Up');
    expect(toRequest(m).filters).toEqual({ tickers: ['BTC'], minPercentMove: 4.5, direction: 'Up' });
  });
  it('carries over the general settings', () => {
    const m = fromRule(rule('News', {}));
    expect(m).toMatchObject({ minSeverity: 3, cooldownMinutes: 30, isEnabled: false, channelIds: ['a', 'b'] });
  });
  it('survives unexpected filter content without throwing', () => {
    expect(() => fromRule(rule('Markets', { tickers: 'not-a-list', direction: 7 }))).not.toThrow();
    expect(fromRule(rule('Markets', null)).direction).toBe('Either');
  });
});

describe('applyPreset', () => {
  const preset: Preset = {
    name: 'Major earthquakes', description: 'd',
    template: { category: 'Disasters', minSeverity: 1, filters: { minMagnitude: 6, eventTypes: [] }, cooldownSeconds: 600 },
  };

  it('fills in filters and keeps what the user already chose', () => {
    const m = applyPreset(base({ name: 'Mine', channelIds: ['c9'], category: 'News', includeKeywords: 'leftover' }), preset);
    expect(m).toMatchObject({ name: 'Mine', category: 'Disasters', minMagnitude: 6, cooldownMinutes: 10, channelIds: ['c9'] });
    expect(m.includeKeywords).toBe(''); // leftovers from the earlier category are gone
  });
  it('uses the preset name when the name is still empty', () => {
    expect(applyPreset(base({ name: '' }), preset).name).toBe('Major earthquakes');
  });
});

describe('check', () => {
  it('accepts a complete rule', () => expect(check(base({ category: 'Disasters' }), true)).toEqual([]));
  it('asks for a name', () => expect(check(base({ name: '  ' }), true)).toHaveLength(1));
  it('asks for a ticker on market rules', () => expect(check(base({ category: 'Markets', tickers: ' , ' }), true)).toHaveLength(1));
  it('asks for a channel except when only previewing', () => {
    expect(check(base({ channelIds: [] }), true)).toHaveLength(1);
    expect(check(base({ channelIds: [] }), false)).toEqual([]);
  });
  it('rejects out of range severity and negative cooldown', () => {
    expect(check(base({ minSeverity: 0 }), true)).toHaveLength(1);
    expect(check(base({ minSeverity: 6 }), true)).toHaveLength(1);
    expect(check(base({ cooldownMinutes: -1 }), true)).toHaveLength(1);
  });
});
