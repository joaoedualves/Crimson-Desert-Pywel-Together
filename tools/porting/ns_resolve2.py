import sys,json,pickle,time,collections,bisect,csv,struct,re
sys.path.insert(0,'/home/claude/re')
import numpy as np
from match import *
OLD='/mnt/user-data/uploads/CrimsonDesertCoop/DepotDownloader/exe-2976/bin64/CrimsonDesert.exe'
old=Img(OLD); new=Img('/home/claude/bins/new.exe'); IB=0x140000000
fm=json.load(open('/home/claude/ns_map_funcs.json')); un=[int(x,16) for x in json.load(open('/home/claude/ns_unres.json'))['unresolved']]
R=pickle.load(open('/home/claude/ns_rtti.pkl','rb'))
D=dict(R['vmap'])
newt={t[1]:t for t in json.load(open('/home/claude/port/troc_2663.json'))}
ntroc=0
for row in csv.DictReader(open('/home/claude/ns/docs/research/data/troc_messages_2976.csv')):
    if row['name'] in newt and row['static_instance']:
        D[int(row['static_instance'],16)]=newt[row['name']][0]; ntroc+=1
print('data anchors: vt',len(R['vmap']),'troc',ntroc,flush=True)
def fend(im,s):
    i=bisect.bisect_left(im.fstart,s); return int(im.fend[i])
def refs(im,s,e):
    out=[]
    for i in md.disasm(im.b(s,e-s),IB+s):
        if i.mnemonic=='.byte': continue
        for o in i.operands:
            if o.type==X86_OP_MEM and o.mem.base==X86_REG_RIP: out.append(i.address+i.size+o.mem.disp-IB)
    return out
datasecs=[(va,vs) for n,va,vs,ch in new.secs if not ch&0x20000000]
newdata=[(va,bytes(new.mem[va:va+vs])) for va,vs in datasecs]
scache={}
def strmap(a):
    if a in scache: return scache[a]
    r=None
    if old.sec(a) and not old.func_at(a):
        b=old.b(a,160)
        for enc,term,pat in (('a',b'\0',rb'^[\x20-\x7e]{8,}\x00'),('w',b'\0\0\0',rb'^(?:[\x20-\x7e]\x00){8,}\x00\x00')):
            m=re.match(pat,b)
            if m:
                key=m.group(0); hits=[]
                for va,buf in newdata:
                    p=buf.find(key)
                    while p!=-1 and len(hits)<3:
                        if p==0 or (buf[p-1]==0 if enc=='a' else buf[p-2:p]==b'\0\0'): hits.append(va+p)
                        p=buf.find(key,p+1)
                if len(hits)==1: r=hits[0]
                break
    scache[a]=r; return r
t0=time.time(); want={}  # old func -> set(new data targets)
for t in un:
    e=fend(old,t)
    if e-t>0x20000: continue
    s=set()
    for a in set(refs(old,t,e)):
        n=D.get(a) or strmap(a)
        if n: s.add(n)
    if s: want[t]=s
alln=np.array(sorted(set().union(*want.values())) if want else [],dtype=np.int64)
print('unresolved',len(un),'with mapped data refs',len(want),'targets',len(alln),round(time.time()-t0),flush=True)
# RIP-relative xref scan in new exe
hits=collections.defaultdict(set)
for n,va,vs,ch in new.secs:
    if not (ch&0x20000000): continue
    CH=1<<24
    for off in range(0,vs,CH):
        a=np.frombuffer(bytes(new.mem[va+off:va+min(vs,off+CH)+8]),dtype=np.uint8)
        n_=len(a)-8
        if n_<=0: continue
        v=a[:n_].astype(np.int64)|(a[1:n_+1].astype(np.int64)<<8)|(a[2:n_+2].astype(np.int64)<<16)|(a[3:n_+3].astype(np.int64)<<24)
        v=np.where(v>=2**31,v-2**32,v)
        pos=va+off+np.arange(n_,dtype=np.int64)
        for extra in (4,5,8):   # disp followed by 0, 1 or 4 immediate bytes
            tg=pos+extra+v; ok=np.isin(tg,alln)
            for p,g in zip(pos[ok].tolist(),tg[ok].tolist()):
                f=new.func_at(p)
                if f: hits[g].add(f[0])
print('xref scan done',round(time.time()-t0),flush=True)
ncache={}
def N(im,s):
    k=(id(im),s)
    if k not in ncache: ncache[k]=norm(im,s,fend(im,s))
    return ncache[k]
added=0; amb=[]
for t,s in want.items():
    cnt=collections.Counter()
    for g in s:
        fs=hits.get(g,())
        if len(fs)>300: continue
        for f in fs: cnt[f]+=1
    if not cnt: continue
    full=N(old,t); sc=[]
    for f,c in cnt.most_common(40):
        if fend(new,f)-f>0x20000: continue
        sc.append((ratio(full,N(new,f)),c,f))
    sc.sort(reverse=True)
    if sc and sc[0][0]>=0.55 and (len(sc)==1 or sc[1][0]<sc[0][0]-0.04):
        fm[hex(t)]={'new':hex(sc[0][2]),'how':'data refs %d, r %.3f'%(sc[0][1],sc[0][0])}; added+=1
    else: amb.append((hex(t),[(round(r,3),c,hex(f)) for r,c,f in sc[:3]]))
print('added',added,'ambiguous',len(amb),amb[:6])
json.dump(fm,open('/home/claude/ns_map_funcs.json','w'),indent=0)
left=[hex(t) for t in un if hex(t) not in fm]
json.dump({'unresolved':left},open('/home/claude/ns_unres2.json','w')); print('resolved',len(fm),'left',len(left),round(time.time()-t0))
