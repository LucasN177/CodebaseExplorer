// Converts a validated GraphView into Cytoscape element definitions. Names from the analyzed
// repository are untrusted: they only ever go into element *data*, which Cytoscape renders as text
// on a canvas — never into HTML.
import type { ElementDefinition } from 'cytoscape';
import type { GraphView } from './protocol';

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
        ecosystem: node.ecosystem ?? '',
        size: node.metrics.size,
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
        level: edge.maxLevel,
        declared: edge.isDeclared ? 1 : 0,
      },
    });
  }

  return elements;
}
