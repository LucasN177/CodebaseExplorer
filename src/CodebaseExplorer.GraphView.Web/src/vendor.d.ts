// cytoscape-fcose 2.2.0 ships no type declarations; it is a regular Cytoscape extension.
declare module 'cytoscape-fcose' {
  import type { Ext } from 'cytoscape';

  const fcose: Ext;
  export default fcose;
}
