const express = require('express');
const { createRouter } = require('./routes');
const config = require('./config.json');

function start() {
  return express().use(createRouter(config));
}

module.exports = { start };
