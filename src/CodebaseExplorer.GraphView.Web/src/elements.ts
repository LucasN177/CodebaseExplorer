// Converts a validated GraphView into Cytoscape element definitions. Names from the analyzed
// repository are untrusted: they only ever go into element *data*, which Cytoscape renders as text
// on a canvas — never into HTML.
import type { ElementDefinition } from 'cytoscape';
import type { GraphView } from './protocol';

/** Edge width in px: 1.5 for a single relationship, growing with log2(count), at most 7 (GRAPH_ENGINE.md §9). */
export function edgeWidth(count: number): number {
  return Math.min(7, 1.5 + Math.log2(Math.max(1, count)));
}

/** Node scale factor from its size metric: 1 for empty elements, growing with log10(size), at most 2. */
export function nodeScale(size: number): number {
  return Math.min(2, 1 + Math.log10(1 + Math.max(0, size)) / 3);
}

/**
 * Ecosystem family used for the node color. Projects use `dotnet`/`node`/`python`/`other`, packages
 * `nuget`/`npm`/`pypi`/`dotnet-framework`/`unknown` (Domain KindTokens); both map to one palette entry.
 */
export function ecosystemFamily(ecosystem: string | undefined): 'dotnet' | 'node' | 'python' | 'other' {
  switch (ecosystem) {
    case 'dotnet':
    case 'nuget':
    case 'dotnet-framework':
      return 'dotnet';
    case 'node':
    case 'npm':
      return 'node';
    case 'python':
    case 'pypi':
      return 'python';
    default:
      return 'other';
  }
}

export function toElements(view: GraphView): ElementDefinition[] {
  const elements: ElementDefinition[] = [];

  for (const node of view.nodes) {
    elements.push({
      group: 'nodes',
      data: {
        id: node.id,
        parent: node.parentId,
        label: node.label,
        kind: node.kind,
        ecosystem: ecosystemFamily(node.ecosystem),
        size: node.metrics.size,
        scale: nodeScale(node.metrics.size),
        missing: node.badges.includes('missing') ? 1 : 0,
      },
    });
  }

  for (const edge of view.edges) {
    elements.push({
      group: 'edges',
      data: {
        id: edge.id,
        source: edge.source,
        target: edge.target,
        count: edge.count,
        width: edgeWidth(edge.count),
        level: edge.maxLevel,
        declared: edge.isDeclared ? 1 : 0,
      },
    });
  }

  return elements;
}
