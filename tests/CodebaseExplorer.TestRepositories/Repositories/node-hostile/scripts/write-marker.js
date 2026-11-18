const fs = require('fs');
const path = require('path');

const stage = process.argv[2] || 'unknown';
fs.writeFileSync(path.join(__dirname, '..', `MARKER-script-${stage}.txt`), 'executed');
