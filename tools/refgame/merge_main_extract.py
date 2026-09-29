#!/usr/bin/env python3
# Merges main.js runtime data (extract_main.js output) into reference/data/blocks.json and writes items.json.
import json, sys, os
root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
m = json.load(open(sys.argv[1]))
p = os.path.join(root, 'reference/data/blocks.json')
b = json.load(open(p))
for x, y in zip(b['blocks'], m['blocks']):
    assert x['id'] == y['id'] and x['key'] == y['key']
    x['light'] = y['light']; x['mine'] = y['mine']; x['tab'] = y['tab']; x['rgb'] = y['rgb']
b['source'] = 'reference/source/meshWorker.js render tables + main.js runtime (M faces, Z6 mining, _u light, tabs)'
open(p, 'w').write(json.dumps(b, ensure_ascii=False, separators=(',', ':')).replace('},{"id"', '},\n{"id"'))
it = {'itemIdBase': m['itemIdBase'], 'items': m['items']}
open(os.path.join(root, 'reference/data/items.json'), 'w').write(json.dumps(it, ensure_ascii=False, separators=(',', ':')).replace('},{"key"', '},\n{"key"'))
print('blocks', len(b['blocks']), 'items', len(m['items']))
