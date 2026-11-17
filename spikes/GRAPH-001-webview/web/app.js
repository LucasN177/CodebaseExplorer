// Spike GRAPH-001: page side of the .NET <-> web view protocol. Labels are set as data (text), never as HTML.
(function () {
  const post = typeof invokeCSharpAction === 'function'
    ? (m) => invokeCSharpAction(m)
    : (window.chrome && window.chrome.webview) ? (m) => window.chrome.webview.postMessage(m) : null;
  const bridge = typeof invokeCSharpAction === 'function' ? 'invokeCSharpAction'
    : (window.chrome && window.chrome.webview) ? 'chrome.webview.postMessage' : 'none';

  const report = (type, payload) => { if (post) post(JSON.stringify({ type, version: 1, payload })); };
  const violations = [];
  document.addEventListener('securitypolicyviolation', (e) => violations.push(e.violatedDirective + ' ' + e.blockedURI));

  let cy = null;

  window.receive = (json) => {
    const message = JSON.parse(json);
    if (message.type === 'graph/load') {
      const t0 = performance.now();
      const elements = [];
      for (const n of message.payload.nodes) elements.push({ data: { id: n.id, label: n.label } });
      for (const e of message.payload.edges) elements.push({ data: { id: e.id, source: e.source, target: e.target } });
      cy = cytoscape({
        container: document.getElementById('cy'),
        elements,
        layout: { name: 'grid' },
        style: [
          { selector: 'node', style: { label: 'data(label)', 'font-size': 6, width: 8, height: 8 } },
          { selector: 'edge', style: { width: 0.5 } },
        ],
      });
      window.cyInstance = cy; // explicit: a global `cy` would be the <div id="cy"> element
      cy.on('select', 'node', () => report('selection/changed', { nodeIds: cy.$('node:selected').map((n) => n.id()), edgeIds: [] }));
      report('graph/rendered', { nodes: cy.nodes().length, edges: cy.edges().length, ms: Math.round(performance.now() - t0) });
    } else if (message.type === 'view/layout') {
      const t0 = performance.now();
      cy.layout({ name: message.payload.name, animate: false }).run();
      report('layout/done', { name: message.payload.name, ms: Math.round(performance.now() - t0) });
    }
    return 'ack:' + message.type;
  };

  window.runSecurityProbes = () => {
    fetch('https://example.com/').then(() => report('probe/fetch', { result: 'allowed' }), () => report('probe/fetch', { result: 'blocked' }));
    const img = new Image();
    img.src = 'https://example.com/pixel.png';
    setTimeout(() => report('probe/csp', { violations }), 800);
    return 'probes-started';
  };

  window.tryOpen = () => { window.open('https://example.com/'); return 'open-called'; };
  window.tryNavigate = () => { window.location.href = 'https://example.com/'; return 'navigate-called'; };

  report('ready', { protocolVersion: 1, bridge, cytoscape: typeof cytoscape === 'function' ? cytoscape.version : null, url: location.href });
})();
