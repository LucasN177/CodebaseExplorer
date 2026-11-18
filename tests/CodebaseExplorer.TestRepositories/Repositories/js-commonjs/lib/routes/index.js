const Helper = require('../util/helper.js');

function createRouter(config) {
  const name = config.plugin;
  const plugin = require(name);
  return new Helper(plugin);
}

exports.createRouter = createRouter;
