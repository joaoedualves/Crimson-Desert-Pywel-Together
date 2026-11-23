import sys,json,pickle,bisect,collections,os,time
sys.path.insert(0,'/home/claude/re')
import numpy as np
from match import *
OLD='/mnt/user-data/uploads/CrimsonDesertCoop/DepotDownloader/exe-2976/bin64/CrimsonDesert.exe'
old=Img(OLD); new=Img('/home/claude/bins/new.exe'); IB=0x140000000
nidx=pickle.load(open('/home/claude/port/idx_new.pkl','rb'))
M={}
try:
    for k,v in json.load(open('/home/claude/ns_map_all.json'))['funcs'].items(): M[int(k,16)]=int(v['new'],16)
except Exception: pass
def fend(im,s):
    i=bisect.bisect_left(im.fstart,s); return int(im.fend[i])
def isfs(im,v):
    i=bisect.bisect_left(im.fstart,v); return i<len(im.fstart) and im.fstart[i]==v
def callgraph(im,path):
    if os.path.exists(path): return pickle.load(open(path,'rb'))
    callers=collections.defaultdict(list); callees=collections.defaultdict(list)
    for n,va,vs,ch in im.secs:
        if not ch&0x20000000: continue
        a=np.frombuffer(bytes(im.mem[va:va+vs]),dtype=np.uint8)
        pos=np.flatnonzero(a[:-5]==0xE8)
        rel=(a[pos+1].astype(np.int64)|(a[pos+2].astype(np.int64)<<8)|(a[pos+3].astype(np.int64)<<16)|(a[pos+4].astype(np.int64)<<24)); rel=np.where(rel>=2**31,rel-2**32,rel)
        tgt=va+pos+5+rel; fs=np.array(im.fstart,dtype=np.int64)
        ok=np.isin(tgt,fs)
        src=va+pos[ok]; tg=tgt[ok]
        idx=np.searchsorted(fs,src,side='right')-1
        for i,t in zip(idx.tolist(),tg.tolist()):
            f=int(fs[i]); callers[t].append(f); callees[f].append(t)
    g=(dict(callers),dict(callees)); pickle.dump(g,open(path,'wb')); return g
NCALLERS,NCALLEES=callgraph(new,'/home/claude/cg_new.pkl')
OCALLERS,OCALLEES=callgraph(old,'/home/claude/cg_old.pkl')
nc={}
def N(im,s):
    k=(id(im),s)
    if k not in nc: nc[k]=norm(im,s,fend(im,s))
    return nc[k]
def direct(s,thr=0.85,gap=0.04):
    if s in M: return M[s]
    e=fend(old,s)
    if e-s>0x20000: return None
    c=nidx.get(h(norm(old,s,min(e,s+400),24)),[])
    if not c or len(c)>60: return None
    full=N(old,s)
    sc=sorted(((ratio(full,N(new,cs)),cs) for cs,ce in c if ce-cs<=0x20000),reverse=True)
    if sc and sc[0][0]>=thr and (len(sc)==1 or sc[1][0]<sc[0][0]-gap): M[s]=sc[0][1]; return sc[0][1]
    return None
def by_callees(s,verbose=True):
    oc=[c for c in OCALLEES.get(s,[])]
    mapped={c:direct(c) for c in set(oc)}
    need=set(v for v in mapped.values() if v)
    if verbose: print('callees',len(set(oc)),'mapped',len(need))
    if not need: return []
    cnt=collections.Counter()
    for t in need:
        for f in set(NCALLERS.get(t,[])): cnt[f]+=1
    full=N(old,s)
    out=[]
    for f,c in cnt.most_common(30):
        if fend(new,f)-f>0x20000: continue
        out.append((c,round(ratio(full,N(new,f)),3),f,fend(new,f)-f))
    out.sort(reverse=True)
    return out
if __name__=='__main__':
    s=int(sys.argv[1],16); e=fend(old,s); print('old',hex(s),'size',e-s)
    for r in by_callees(s)[:8]: print(r[0],r[1],hex(r[2]),r[3])

def xrefs_to(im,targets,extras=(4,5,8)):
    """code locations whose rip-relative disp (followed by 0/1/4 imm bytes) hits one of targets -> {target:[func starts]}"""
    tset=np.array(sorted(set(targets)),dtype=np.int64); out=collections.defaultdict(list)
    for n,va,vs,ch in im.secs:
        if not ch&0x20000000: continue
        CH=1<<23
        for off in range(0,vs,CH):
            a=np.frombuffer(bytes(im.mem[va+off:va+min(vs,off+CH)+8]),dtype=np.uint8); n_=len(a)-8
            if n_<=0: continue
            v=a[:n_].astype(np.int64)|(a[1:n_+1].astype(np.int64)<<8)|(a[2:n_+2].astype(np.int64)<<16)|(a[3:n_+3].astype(np.int64)<<24)
            v=np.where(v>=2**31,v-2**32,v); pos=va+off+np.arange(n_,dtype=np.int64)
            for extra in extras:
                tg=pos+extra+v; ok=np.isin(tg,tset)
                for p,g in zip(pos[ok].tolist(),tg[ok].tolist()):
                    f=im.func_at(p)
                    if f: out[g].append(f[0])
    return out
