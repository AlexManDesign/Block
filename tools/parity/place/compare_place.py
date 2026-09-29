import json, sys
ref = json.load(open(sys.argv[1])); port = json.load(open(sys.argv[2]))
def norm(v):
    if isinstance(v, dict): return v.get('err'), sorted(map(tuple, v['cells']))
    return None, sorted(map(tuple, v))
bad = 0
for name in ref:
    re_, rc = norm(ref[name]); pe, pc = norm(port.get(name, []))
    if re_ or pe or rc != pc:
        bad += 1
        if bad <= 40:
            print('MISMATCH', name)
            if re_: print('   ref error:', re_)
            if pe: print('   port error:', pe)
            rs, ps = set(rc), set(pc)
            for c in sorted(rs - ps): print('   ref only :', c)
            for c in sorted(ps - rs): print('   port only:', c)
print(f'{len(ref) - bad}/{len(ref)} tests identical')
sys.exit(1 if bad else 0)
