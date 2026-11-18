// Selection / focus encoding (GRAPH_ENGINE.md §9): the selection and its direct neighborhood stay
// visible, everything else is dimmed. Pure view state; nothing is reported to the host.
import type { Core } from 'cytoscape';

export const DIMMED_CLASS = 'dimmed';

/**
 * Dims every element that is neither selected nor directly connected to the selection: for a selected node its
 * edges and adjacent nodes stay visible, for a selected edge its two endpoints. A selected compound node (expanded
 * project or namespace, GRAPH-006) counts together with its content, and the compound boxes around visible nodes
 * stay visible. Without a selection nothing is dimmed.
 */
export function applyFocusDimming(cy: Core): void {
  const selected = cy.elements(':selected');
  cy.batch(() => {
    if (selected.empty()) {
      cy.elements().removeClass(DIMMED_CLASS);
      return;
    }

    const focus = selected.nodes().union(selected.nodes().descendants());
    let visible = selected
      .union(focus)
      .union(focus.connectedEdges())
      .union(focus.neighborhood())
      .union(selected.edges().connectedNodes());
    visible = visible.union(visible.nodes().ancestors());
    visible.removeClass(DIMMED_CLASS);
    cy.elements().difference(visible).addClass(DIMMED_CLASS);
  });
}
