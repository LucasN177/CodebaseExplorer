// Visual encoding (GRAPH_ENGINE.md §9) for light and dark themes. Evidence level is encoded by line
// style as well as color, so color is never the only carrier of meaning.
import type { StylesheetJson } from 'cytoscape';
import type { ThemeMode } from './protocol';

interface Palette {
  background: string;
  text: string;
  node: string;
  external: string;
  missing: string;
  edge: string;
  selection: string;
}

const palettes: Record<ThemeMode, Palette> = {
  light: { background: '#ffffff', text: '#1f2328', node: '#3b6fd8', external: '#8a63d2', missing: '#b35900', edge: '#8c959f', selection: '#d4a72c' },
  dark: { background: '#1e1e1e', text: '#e6edf3', node: '#6a9cff', external: '#b392f0', missing: '#ff9e3d', edge: '#6e7681', selection: '#e3b341' },
};

export function backgroundOf(mode: ThemeMode): string {
  return palettes[mode].background;
}

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
        'background-color': p.node,
        shape: 'round-rectangle',
        width: 28,
        height: 18,
      },
    },
    { selector: 'node[kind = "external-dependency"]', style: { 'background-color': p.external, shape: 'diamond', width: 18, height: 18 } },
    { selector: 'node[missing = 1]', style: { 'background-color': p.missing, 'border-width': 2, 'border-style': 'dashed', 'border-color': p.missing, 'background-opacity': 0.4 } },
    {
      selector: 'edge',
      style: {
        width: 1.5,
        'line-color': p.edge,
        'target-arrow-color': p.edge,
        'target-arrow-shape': 'triangle',
        'curve-style': 'bezier',
      },
    },
    { selector: 'edge[level = "syntactic"]', style: { 'line-style': 'dashed' } },
    { selector: 'edge[level = "heuristic"]', style: { 'line-style': 'dotted' } },
    { selector: 'edge[declared = 0]', style: { 'target-arrow-shape': 'vee' } },
    { selector: ':selected', style: { 'border-width': 3, 'border-color': p.selection, 'line-color': p.selection, 'target-arrow-color': p.selection } },
  ];
}
