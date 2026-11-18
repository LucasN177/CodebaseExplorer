import { describe, expect, it } from 'vitest';
import { palettes, stylesheet } from '../src/styles';
import type { ThemeMode } from '../src/protocol';

// WCAG 2.x relative luminance and contrast ratio.
function luminance(hex: string): number {
  const channel = (i: number): number => {
    const c = parseInt(hex.slice(1 + 2 * i, 3 + 2 * i), 16) / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(0) + 0.7152 * channel(1) + 0.0722 * channel(2);
}

function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number];
  return (hi + 0.05) / (lo + 0.05);
}

const modes: ThemeMode[] = ['light', 'dark'];

describe('palettes', () => {
  it.each(modes)('%s: node, missing and selection colors have at least 3:1 contrast to the background', (mode) => {
    const p = palettes[mode];
    for (const color of [...Object.values(p.ecosystems), p.missing, p.selection]) {
      expect(contrast(color, p.background), color).toBeGreaterThanOrEqual(3);
    }
    expect(contrast(p.text, p.background)).toBeGreaterThanOrEqual(4.5);
  });

  it.each(modes)('%s: ecosystem colors are pairwise distinct', (mode) => {
    const colors = Object.values(palettes[mode].ecosystems);
    expect(new Set(colors).size).toBe(colors.length);
  });
});

describe('stylesheet', () => {
  const selectors = stylesheet('light').map((rule) => ('selector' in rule ? rule.selector : ''));

  it('encodes evidence level by line style and declared dependencies by arrow head', () => {
    expect(selectors).toEqual(expect.arrayContaining(['edge[level = "syntactic"]', 'edge[level = "heuristic"]', 'edge[declared = 0]']));
  });

  it('encodes node kind by shape, ecosystem by color, and dims non-neighbors of the selection', () => {
    expect(selectors).toEqual(expect.arrayContaining([
      'node[kind = "external-dependency"]',
      'node[ecosystem = "dotnet"]',
      'node[ecosystem = "node"]',
      'node[ecosystem = "python"]',
      '.dimmed',
    ]));
  });
});
