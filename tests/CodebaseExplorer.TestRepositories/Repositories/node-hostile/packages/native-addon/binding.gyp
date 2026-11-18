{
  "targets": [
    {
      "target_name": "addon",
      "actions": [
        {
          "action_name": "marker",
          "inputs": [],
          "outputs": ["MARKER-node-gyp.txt"],
          "action": ["node", "-e", "require('fs').writeFileSync('MARKER-node-gyp.txt', 'executed')"]
        }
      ]
    }
  ]
}
