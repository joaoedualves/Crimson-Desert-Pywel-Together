import sys,json,pickle,collections
sys.path.insert(0,'/home/claude/re')
import numpy as np
from pe import Img
IB=0x140000000
def rtti(path):
    im=Img(path); mem=im.mem; out={}  # vtable rva -> (name, offset)
    cols={}
    for n,va,vs,ch in im.secs:
        if ch&0x20000000 or vs<64: continue
        a=np.frombuffer(bytes(mem[va:va+(vs&~3)]),dtype='<u4')
        idx=np.flatnonzero((a[:-5]==1)&(a[5:]==(va+4*np.arange(len(a)-5))))
        for i in idx.tolist():
            col=va+4*i; td=int(a[i+3]); off=int(a[i+1])
            if not (0<td<len(mem)-64): continue
            nm=bytes(mem[td+16:td+16+200]).split(b'\0')[0]
            if nm.startswith(b'.?A'): cols[col]=(nm.decode(errors='replace'),off)
    colv=np.array(sorted(cols),dtype=np.uint64)+IB
    for n,va,vs,ch in im.secs:
        if ch&0x20000000 or vs<64: continue
        q=np.frombuffer(bytes(mem[va:va+(vs&~7)]),dtype='<u8')
        hit=np.flatnonzero(np.isin(q,colv))
        for i in hit.tolist(): out[va+8*i+8]=cols[int(q[i])-IB]
    return im,out
res={}
for tag,p in (('old','/mnt/user-data/uploads/CrimsonDesertCoop/DepotDownloader/exe-2976/bin64/CrimsonDesert.exe'),('new','/home/claude/bins/new.exe')):
    im,vt=rtti(p); res[tag]=vt; print(tag,len(vt),flush=True)
def uniq(vt):
    c=collections.Counter(vt.values()); return {k:a for a,k in vt.items() if c[k]==1}
uo,un=uniq(res['old']),uniq(res['new'])
vmap={uo[k]:un[k] for k in uo if k in un}
print('vtables mapped',len(vmap),'old uniq',len(uo),'new uniq',len(un))
pickle.dump({'old':res['old'],'new':res['new'],'vmap':vmap},open('/home/claude/ns_rtti.pkl','wb'))
