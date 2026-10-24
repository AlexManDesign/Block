import sys, json, base64, os
S, src, dst = sys.argv[1], sys.argv[2], sys.argv[3]
lines = open(src, encoding='utf-8').read().split('\n')
idx = [i for i, l in enumerate(lines) if l.startswith('const ASSETS = ')]
assert len(idx) == 1
i = idx[0]
body = lines[i][len('const ASSETS = '):]
semi = body.endswith(';')
d = json.loads(body.rstrip(';'))
b64 = lambda p: base64.b64encode(open(p, 'rb').read()).decode()
d['atlas'] = b64(S + '/new/atlas.png')
for k in d['entities']:
    d['entities'][k] = b64(f'{S}/new/ent_{k}.png')
lines[i] = 'const ASSETS = ' + json.dumps(d, ensure_ascii=False, separators=(',', ':')) + (';' if semi else '')
open(dst, 'w', encoding='utf-8').write('\n'.join(lines))
print('ok', os.path.getsize(src), '->', os.path.getsize(dst))
