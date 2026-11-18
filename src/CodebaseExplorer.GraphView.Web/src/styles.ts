// Visual encoding (GRAPH_ENGINE.md §9) for light and dark themes. Meaning is never carried by color
// alone: node kind is a shape, evidence level a line style, a declared dependency an arrow head, a
// missing project a dashed outline. Ecosystem colors are based on the Okabe–Ito palette (distinct
// for common color vision deficiencies); every node color has at least 3:1 contrast against the
// background of its theme (checked in test/styles.test.ts).
import type { NodeSingular, StylesheetJson } from 'cytoscape';
import type { ThemeMode } from './protocol';

export interface Palette {
  background: string;
  text: string;
  ecosystems: Record<'dotnet' | 'node' | 'python' | 'other', string>;
  missing: string;
  edge: string;
  selection: string;
}

export const palettes: Record<ThemeMode, Palette> = {
  light: {
    background: '#ffffff',
    text: '#1f2328',
    ecosystems: { dotnet: '#0072b2', node: '#007a5a', python: '#a8508a', other: '#6e7781' },
    missing: '#c4432b',
    edge: '#8c959f',
    selection: '#9a6700',
  },
  dark: {
    background: '#1e1e1e',
    text: '#e6edf3',
    ecosystems: { dotnet: '#56b4e9', node: '#3fcf9f', python: '#e0a3c8', other: '#8b949e' },
    missing: '#ff8a4c',
    edge: '#6e7681',
    selection: '#e3b341',
  },
};

/** Opacity of elements outside the selection and its direct neighbors. */
export const DIMMED_OPACITY = 0.15;

export function backgroundOf(mode: ThemeMode): string {
  return palettes[mode].background;
}

const scaled = (base: number) => (node: NodeSingular): number => base * (Number(node.data('scale')) || 1);

export function stylesheet(mode: ThemeMode): StylesheetJson {
  const p = palettes[mode];
  return [
    {
      selector: 'node',
      style: {
        label: 'data(label)',
        color: p.text,
        'font-size': 11,
        'text-valign': 'bottom',
        'text-margin-y': 4,
        'background-color': p.ecosystems.other,
        shape: 'round-rectangle',
        width: scaled(28),
        height: scaled(18),
      },
    },
    { selector: 'node[ecosystem = "dotnet"]', style: { 'background-color': p.ecosystems.dotnet } },
    { selector: 'node[ecosystem = "node"]', style: { 'background-color': p.ecosystems.node } },
    { selector: 'node[ecosystem = "python"]', style: { 'background-color': p.ecosystems.python } },
    { selector: 'node[kind = "external-dependency"]', style: { shape: 'diamond', width: 18, height: 18 } },
    { selector: 'node[missing = 1]', style: { 'background-color': p.missing, 'border-width': 2, 'border-style': 'dashed', 'border-color': p.missing, 'background-opacity': 0.4 } },
    {
      selector: 'edge',
      style: {
        width: 'data(width)',
        'line-color': p.edge,
        'target-arrow-color': p.edge,
        'target-arrow-shape': 'triangle',
        'arrow-scale': 0.9,
        'curve-style': 'bezier',
      },
    },
    { selector: 'edge[level = "syntactic"]', style: { 'line-style': 'dashed' } },
    { selector: 'edge[level = "heuristic"]', style: { 'line-style': 'dotted' } },
    { selector: 'edge[declared = 0]', style: { 'target-arrow-shape': 'vee' } },
    // GRAPH-006: namespaces are rectangles, types circles; an expandable, collapsed node has a double outline;
    // an expanded node is a translucent compound box labeled at the top.
    { selector: 'node[kind = "module"]', style: { shape: 'rectangle' } },
    { selector: 'node[kind = "entity"]', style: { shape: 'ellipse', width: scaled(16), height: scaled(16) } },
    { selector: 'node[expandable = 1][expanded = 0]', style: { 'border-width': 3, 'border-style': 'double', 'border-color': p.text } },
    {
      selector: 'node:parent',
      style: { 'background-opacity': 0.1, 'border-width': 1, 'border-style': 'solid', 'border-color': p.edge, 'text-valign': 'top', 'text-margin-y': -4, padding: '12px' },
    },
    { selector: '.dimmed', style: { opacity: DIMMED_OPACITY } },
    { selector: 'node:selected', style: { 'border-width': 3, 'border-style': 'solid', 'border-color': p.selection } },
    { selector: 'edge:selected', style: { 'line-color': p.selection, 'target-arrow-color': p.selection } },
  ];
}
