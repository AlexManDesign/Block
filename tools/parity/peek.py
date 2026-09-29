import sys, json, numpy as np
H=384
names={v:k for k,v in json.load(open('source_block_ids.json')).items()}
d=sys.argv[1]; x,y,z=map(int,sys.argv[2:5]); r=int(sys.argv[5]) if len(sys.argv)>5 else 1
def cell(wx,wy,wz):
    cx,cz=wx//16,wz//16; b=open(f"{d}/js_{cx}_{cz}.bin",'rb').read(); n=16*16*H
    o=((wx%16)*16+(wz%16))*H+(wy+64)
    return names[b[2*o]|(b[2*o+1]<<8)], b[2*n+o], b[3*n+o]
for dy in range(r,-r-1,-1):
    print(f"y={y+dy}")
    for dz in range(-r,r+1):
        print('   '+'  '.join(f"{cell(x+dx,y+dy,z+dz)[0][:14]:>14}/{cell(x+dx,y+dy,z+dz)[2]:3d}" for dx in range(-r,r+1)))
