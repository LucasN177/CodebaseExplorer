// Top-level side effect: runs only if this module is loaded.
require('fs').writeFileSync(require('path').join(__dirname, 'MARKER-module-load.txt'), 'executed');

module.exports = function addon() {};
