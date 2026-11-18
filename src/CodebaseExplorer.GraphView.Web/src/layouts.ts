// Layouts of the graph web view (GRAPH_ENGINE.md §8). Measurements behind these choices are recorded
// in GRAPH_ENGINE.md §8.1 (GRAPH-005): Cytoscape's built-in `cose` needs 12.5–13.9 s for 500 nodes /
// 2,000 edges in WebView2, so no layout here uses it.
import type { LayoutOptions } from 'cytoscape';
import type { LayoutName } from './protocol';

/** Layout used until the host sends `view/layout` (the host's default, see GraphLayout in Desktop). */
export const DEFAULT_LAYOUT: LayoutName = 'force';

/**
 * Up to this many elements (nodes + edges) the force layout runs fcose at "default" quality
 * (spectral layout refined by force iterations); above it only the spectral "draft" step runs.
 * Measured in headless Edge 157: "default" takes ~0.15 s at 100 nodes / 300 edges and grows to
 * 3–6 s at 500 / 2,000, while "draft" stays below 0.5 s there.
 */
export const FORCE_REFINEMENT_LIMIT = 800;

const PADDING = 30;

/** Cytoscape options for a layout of a graph with the given number of elements. */
export function layoutOptions(name: LayoutName, elementCount: number): LayoutOptions {
  switch (name) {
    case 'hierarchical':
      // cytoscape-dagre (MIT, bundles @dagrejs/dagre). The tight-tree ranker was about twice as fast
      // as the default network-simplex ranker in the GRAPH-005 measurements.
      return { name: 'dagre', rankDir: 'TB', ranker: 'tight-tree', animate: false, fit: true, padding: PADDING } as LayoutOptions;
    case 'force':
      // cytoscape-fcose (MIT) with its default forces (as measured). Random start positions are
      // required by the spectral step.
      return {
        name: 'fcose',
        quality: elementCount <= FORCE_REFINEMENT_LIMIT ? 'default' : 'draft',
        randomize: true,
        animate: false,
        fit: true,
        padding: PADDING,
      } as LayoutOptions;
    case 'concentric':
      // Built in: most connected nodes in the center.
      return {
        name: 'concentric',
        concentric: (node) => node.degree(false),
        levelWidth: () => 2,
        minNodeSpacing: 20,
        animate: false,
        fit: true,
        padding: PADDING,
      };
  }
}
