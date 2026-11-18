const assert = require('node:assert');
const path = require('path');
const { start } = require('../lib');

assert.ok(start, path.sep);
