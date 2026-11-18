// Converts a validated GraphView into Cytoscape element definitions. Names from the analyzed
// repository are untrusted: they only ever go into element *data*, which Cytoscape renders as text
// on a canvas — never into HTML.
import type { ElementDefinition } from 'cytoscape';
import type { GraphEdge, GraphNode, GraphView } from './protocol';

/** Edge width in px: 1.5 for a single relationship, growing with log2(count), at most 7 (GRAPH_ENGINE.md §9). */
export function edgeWidth(count: number): number {
  return Math.min(7, 1.5 + Math.log2(Math.max(1, count)));
}

/** Node scale factor from its size metric: 1 for empty elements, growing with log10(size), at most 2. */
export function nodeScale(size: number): number {
  return Math.min(2, 1 + Math.log10(1 + Math.max(0, size)) / 3);
}

/**
 * Strength of the Git-activity overlay (GIT-005) from a node's file changes: -1 without Git data (no halo), otherwise
 * 0 … 1 growing with log10(changes), reaching 1 at 1,000 file changes. Absolute, so values compare across views.
 */
export function activityHeat(changeCount: number | undefined): number {
  return changeCount === undefined ? -1 : Math.min(1, Math.log10(1 + Math.max(0, changeCount)) / 3);
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

/** Cytoscape definition of a node (untrusted names go into data only). */
export function nodeElement(node: GraphNode): ElementDefinition {
  return {
    group: 'nodes',
    data: {
      id: node.id,
      parent: node.parentId,
      label: node.label,
      kind: node.kind,
      ecosystem: ecosystemFamily(node.ecosystem),
      size: node.metrics.size,
      scale: nodeScale(node.metrics.size),
      changes: node.metrics.changeCount ?? -1,
      heat: activityHeat(node.metrics.changeCount),
      missing: node.badges.includes('missing') ? 1 : 0,
      expandable: node.isExpandable ? 1 : 0,
      expanded: node.isExpanded ? 1 : 0,
    },
  };
}

/** Cytoscape definition of an aggregated edge. */
export function edgeElement(edge: GraphEdge): ElementDefinition {
  return {
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
  };
}

export function toElements(view: GraphView): ElementDefinition[] {
  return [...view.nodes.map(nodeElement), ...view.edges.map(edgeElement)];
}
