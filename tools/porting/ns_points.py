import sys,json,pickle,time,bisect,collections,difflib,struct,csv
sys.path.insert(0,'/home/claude/re')
import numpy as np
from match import *
OLD='/mnt/user-data/uploads/CrimsonDesertCoop/DepotDownloader/exe-2976/bin64/CrimsonDesert.exe'
old=Img(OLD); new=Img('/home/claude/bins/new.exe'); IB=0x140000000
fm=json.load(open('/home/claude/ns_map_funcs.json')); u3=json.load(open('/home/claude/ns_unres3.json'))
# accept low-confidence but clearly-best window matches, and ordered clone pairs
low=0
for f in u3['fail']:
    if f[1]=='no clear best' and f[2]:
        sc=f[2]
        if sc[0][0]>=0.35 and (len(sc)==1 or sc[1][0]*1.8<sc[0][0]) and f[3]<=40:
            fm[f[0]]={'new':sc[0][1],'how':'LOW window %d, r %.3f (function changed)'%(f[3],sc[0][0])}; low+=1
clones=collections.defaultdict(list)
for f in u3['fail']:
    if f[1]=='no clear best' and f[2] and len(f[2])>=2 and f[2][0][0]>=0.95 and f[2][0][0]==f[2][1][0] and f[0] not in fm:
        clones[tuple(sorted(x[1] for x in f[2] if x[0]==f[2][0][0]))].append(f[0])
for cands,olds in clones.items():
    if len(cands)==len(olds):
        for o,n in zip(sorted(olds,key=lambda x:int(x,16)),sorted(cands,key=lambda x:int(x,16))): fm[o]={'new':n,'how':'clone pair by order'}; low+=1
# near-tie families: choose by the Troc instance the function references
import csv as _csv
_newt={t[1]:t for t in json.load(open('/home/claude/port/troc_2663.json'))}; _D={}
for row in _csv.DictReader(open('/home/claude/ns/docs/research/data/troc_messages_2976.csv')):
    if row['name'] in _newt and row['static_instance']: _D[int(row['static_instance'],16)]=_newt[row['name']][0]
def _refs(im,s,e):
    out=set()
    for i in md.disasm(im.b(s,e-s),0x140000000+s):
        if i.mnemonic=='.byte': continue
        for o in i.operands:
            if o.type==X86_OP_MEM and o.mem.base==X86_REG_RIP: out.add(i.address+i.size+o.mem.disp-0x140000000)
    return out
def _fe(im,s):
    i=bisect.bisect_left(im.fstart,s); return int(im.fend[i])
nidx_=pickle.load(open('/home/claude/port/idx_new.pkl','rb')); tro=dom=0
_oldop={int(r_['id']):r_['name'] for r_ in _csv.DictReader(open('/home/claude/ns/docs/research/data/troc_messages_2976.csv'))}
_newop={k:v['id'] for k,v in json.load(open('/home/claude/port/opmaps_2663.json'))['m2663'].items()}
_op={o:_newop[n] for o,n in _oldop.items() if n in _newop and _newop[n]!=o and o>2005}
for f in u3['fail']:
    t=f[0]
    if t in fm: continue
    ti=int(t,16); e=_fe(old,ti)
    want={_D[a] for a in _refs(old,ti,e) if a in _D}
    if want:
        cands=nidx_.get(h(norm(old,ti,min(e,ti+400),24)),[])
        if f[1]=='no clear best' and f[2]: cands=list(cands)+[(int(x[1],16),_fe(new,int(x[1],16))) for x in f[2]]
        full=norm(old,ti,e); good=[]
        for cs,ce in set(cands):
            if ce-cs>0x20000: continue
            if want<=_refs(new,cs,ce):
                r=ratio(full,norm(new,cs,ce))
                if r>=0.85: good.append((r,cs))
        if len(good)==1: fm[t]={'new':hex(good[0][1]),'how':'troc instance refs, r %.3f'%good[0][0]}; tro+=1; continue
    # writers: the opcode immediate decides between near-identical candidates
    import re as _re
    def _imms(seq): return {int(x,16) for l in seq for x in _re.findall(r'0x[0-9a-f]+',l)}
    full=norm(old,ti,e); oi={_op[v] for v in _imms(full) if v in _op}
    if oi:
        cands=set(nidx_.get(h(norm(old,ti,min(e,ti+400),24)),[]))
        if f[1]=='no clear best' and f[2]: cands|={(int(x[1],16),_fe(new,int(x[1],16))) for x in f[2]}
        good=[]
        for cs,ce in cands:
            if ce-cs>0x20000: continue
            nn=norm(new,cs,ce)
            if oi<=_imms(nn):
                r=ratio(full,nn)
                if r>=0.85: good.append((r,cs))
        good.sort(reverse=True)
        if good and (len(good)==1 or good[1][0]<good[0][0]-0.02):
            fm[t]={'new':hex(good[0][1]),'how':'opcode immediate %s, r %.3f'%(sorted(oi),good[0][0])}; tro+=1; continue
    if f[1]=='no clear best' and f[2] and len(f[2])>=2 and f[2][0][0]>=0.14 and f[2][0][0]>=2.5*f[2][1][0]:
        fm[t]={'new':f[2][0][1],'how':'LOW dominant in window, r %.3f vs %.3f (function heavily changed)'%(f[2][0][0],f[2][1][0])}; dom+=1
print('functions mapped',len(fm),'(added low/clone',low,'troc',tro,'dominant',dom,')',flush=True)
M={int(k,16):int(v['new'],16) for k,v in fm.items()}
d=json.load(open('/home/claude/ns_addrs.json'))
def fend(im,s):
    i=bisect.bisect_left(im.fstart,s); return int(im.fend[i])
def ins(im,s,e):
    A=[];Nn=[];T=[]
    for i in md.disasm(im.b(s,e-s),IB+s):
        A.append(i.address-IB); m=i.mnemonic; t=None
        if m=='.byte': Nn.append('DB'); T.append(None); continue
        if m in BR:
            if i.operands and i.operands[0].type==X86_OP_IMM:
                Nn.append(m+' REL'); t=(i.operands[0].imm&0xFFFFFFFFFFFFFFFF)-IB
                if s<=t<e and m!='call': t=None
            else: Nn.append(m+' '+i.op_str)
        else:
            ops=[]
            for o in i.operands:
                if o.type==X86_OP_MEM:
                    if o.mem.base==X86_REG_RIP: ops.append('[RIP]'); t=i.address+i.size+o.mem.disp-IB
                    else: ops.append('[%d+%d*%d+%#x]'%(o.mem.base,o.mem.index,o.mem.scale,o.mem.disp))
                elif o.type==X86_OP_IMM:
                    v=o.imm&0xFFFFFFFFFFFFFFFF; ops.append('ADDR' if IB<=v<IB+0x20000000 else hex(v))
                else: ops.append(i.reg_name(o.reg))
            Nn.append(m+' '+','.join(ops))
        T.append(t)
    return A,Nn,T
ic={}
def I(im,s):
    k=(id(im),s)
    if k not in ic: ic[k]=ins(im,s,fend(im,s))
    return ic[k]
ac={}
def amap(s):
    """old instr addr -> (new instr addr, exact?) for function s"""
    if s in ac: return ac[s]
    n=M[s]; A,Na,Ta=I(old,s); B,Nb,Tb=I(new,n); mp={}
    sm=difflib.SequenceMatcher(None,Na,Nb,autojunk=False)
    for i,j,k in sm.get_matching_blocks():
        for x in range(k): mp[A[i+x]]=(B[j+x],True)
    ac[s]=(mp,A,B); return ac[s]
# ---- mid-function points
mid={}; stats=collections.Counter()
for p in d['kinds']['func_mid']:
    s=old.func_at(p)[0]
    if s not in M or fend(old,s)-s>0x20000 or fend(new,M[s])-M[s]>0x20000: stats['func unmapped']+=1; continue
    mp,A,B=amap(s)
    if p in mp: mid[hex(p)]={'new':hex(mp[p][0]),'how':'aligned','func':hex(s)}; stats['exact']+=1
    elif p not in A: stats['not an instruction boundary']+=1; mid[hex(p)]={'new':None,'how':'not instr boundary','func':hex(s)}
    else:
        k=A.index(p); prev=next((A[j] for j in range(k-1,-1,-1) if A[j] in mp),None); nxt=next((A[j] for j in range(k+1,len(A)) if A[j] in mp),None)
        done=False
        if prev is not None and nxt is not None:
            ka,kb=A.index(prev),A.index(nxt); ja,jb=B.index(mp[prev][0]),B.index(mp[nxt][0])
            if kb-ka==jb-ja and jb>ja:
                nb=B[ja+(k-ka)]; _,Na,_=I(old,s); _,Nb,_=I(new,M[s])
                oa,ob=Na[k],Nb[ja+(k-ka)]
                if oa.split(' ')[0]==ob.split(' ')[0]:
                    mid[hex(p)]={'new':hex(nb),'how':'positional (operand changed: %s -> %s)'%(oa,ob),'func':hex(s)}; stats['positional']+=1; done=True
        if not done:
            mid[hex(p)]={'new':None,'how':'instr changed; prev %s next %s'%(prev and hex(mp[prev][0]),nxt and hex(mp[nxt][0])),'func':hex(s)}; stats['changed']+=1
print('mid points',dict(stats),flush=True)
# ---- data
R=pickle.load(open('/home/claude/ns_rtti.pkl','rb')); D={}
newt={t[1]:t for t in json.load(open('/home/claude/port/troc_2663.json'))}
for row in csv.DictReader(open('/home/claude/ns/docs/research/data/troc_messages_2976.csv')):
    if row['name'] in newt and row['static_instance']: D[int(row['static_instance'],16)]=newt[row['name']][0]
ovt=sorted(R['old'])
data={}; dst=collections.Counter(); need=[]
for a in d['kinds']['data']:
    if a in R['vmap']: data[hex(a)]={'new':hex(R['vmap'][a]),'how':'rtti vtable'}; dst['vtable']+=1; continue
    if a in D: data[hex(a)]={'new':hex(D[a]),'how':'troc instance'}; dst['troc']+=1; continue
    i=bisect.bisect_right(ovt,a)-1
    if i>=0 and a-ovt[i]<0x800 and (a-ovt[i])%8==0 and ovt[i] in R['vmap'] and (i+1==len(ovt) or a<ovt[i+1]):
        data[hex(a)]={'new':hex(R['vmap'][ovt[i]]+a-ovt[i]),'how':'vtable slot +%#x'%(a-ovt[i])}; dst['vtslot']+=1; continue
    need.append(a)
stubs=[a for a in need if any(va<=a<va+vs and ch&0x20000000 for n,va,vs,ch in old.secs)]
if stubs:
    sv=np.array(sorted(stubs),dtype=np.uint64)+IB; ptr=collections.defaultdict(list)
    for n,va,vs,ch in old.secs:
        if ch&0x20000000 or vs<64: continue
        q=np.frombuffer(bytes(old.mem[va:va+(vs&~7)]),dtype='<u8')
        for i in np.flatnonzero(np.isin(q,sv)).tolist(): ptr[int(q[i])-IB].append(va+8*i)
    newcode=[(va,bytes(new.mem[va:va+vs])) for n,va,vs,ch in new.secs if ch&0x20000000]
    for a in stubs:
        votes=collections.Counter()
        for loc in ptr.get(a,[]):
            i=bisect.bisect_right(ovt,loc)-1
            if i>=0 and loc-ovt[i]<0x2000 and ovt[i] in R['vmap']:
                votes[struct.unpack_from('<Q',new.mem,R['vmap'][ovt[i]]+loc-ovt[i])[0]-IB]+=1
        if votes:
            (n1,c1),*rest=votes.most_common(2)
            if (not rest or rest[0][1]*3<=c1) and new.b(n1,6)[:3]==old.b(a,3):
                data[hex(a)]={'new':hex(n1),'how':'code stub via vtable slots %d/%d'%(c1,sum(votes.values()))}; dst['stub-vt']+=1; continue
        sig=old.b(a,24); hits=[]
        for va,buf in newcode:
            p_=buf.find(sig)
            while p_!=-1 and len(hits)<2: hits.append(va+p_); p_=buf.find(sig,p_+1)
        if len(hits)==1: data[hex(a)]={'new':hex(hits[0]),'how':'code stub unique 24-byte signature'}; dst['stub-sig']+=1
    need=[a for a in need if hex(a) not in data]
tset=np.array(sorted(need),dtype=np.int64); xr=collections.defaultdict(list)
for n,va,vs,ch in old.secs:
    if not (ch&0x20000000): continue
    CH=1<<24
    for off in range(0,vs,CH):
        a=np.frombuffer(bytes(old.mem[va+off:va+min(vs,off+CH)+8]),dtype=np.uint8); n_=len(a)-8
        if n_<=0: continue
        v=a[:n_].astype(np.int64)|(a[1:n_+1].astype(np.int64)<<8)|(a[2:n_+2].astype(np.int64)<<16)|(a[3:n_+3].astype(np.int64)<<24)
        v=np.where(v>=2**31,v-2**32,v); pos=va+off+np.arange(n_,dtype=np.int64)
        for extra in (4,5,8):
            tg=pos+extra+v; ok=np.isin(tg,tset)
            for p,g in zip(pos[ok].tolist(),tg[ok].tolist()): xr[g].append(p)
print('data xref scan done; need',len(need),'with xrefs',sum(1 for a in need if xr[a]),flush=True)
nidx=pickle.load(open('/home/claude/port/idx_new.pkl','rb'))
def direct(s):
    if s in M: return M[s]
    e=fend(old,s)
    if e-s>0x20000: return None
    c=nidx.get(h(norm(old,s,min(e,s+400),24)),[])
    if not c or len(c)>20: return None
    full=I(old,s)[1]
    sc=sorted(((ratio(full,I(new,cs)[1]),cs) for cs,ce in c if ce-cs<=0x20000),reverse=True)
    if sc and sc[0][0]>=0.75 and (len(sc)==1 or sc[1][0]<sc[0][0]-0.05): M[s]=sc[0][1]; return sc[0][1]
    return None
for a in need:
    votes=collections.Counter()
    for p in xr[a][:60]:
        f=old.func_at(p)
        if not f or direct(f[0]) is None: continue
        mp,A,B=amap(f[0])
        k=bisect.bisect_right(A,p)-1
        if k<0 or A[k] not in mp or I(old,f[0])[2][k]!=a: continue
        nb=mp[A[k]][0]; Bn,_,Tb=I(new,M[f[0]])
        t=Tb[Bn.index(nb)]
        if t is not None: votes[t]+=1
    if votes:
        (n1,c1),*rest=votes.most_common(2)
        if not rest or rest[0][1]*3<=c1: data[hex(a)]={'new':hex(n1),'how':'xref votes %d/%d'%(c1,sum(votes.values()))}; dst['xref']+=1; continue
        data[hex(a)]={'new':None,'how':'xref conflict %s'%[(hex(x),c) for x,c in votes.most_common(3)]}; dst['conflict']+=1
    else: data[hex(a)]={'new':None,'how':'no usable xref (%d refs)'%len(xr[a])}; dst['none']+=1
print('data',dict(dst))
json.dump({'funcs':fm,'mid':mid,'data':data},open('/home/claude/ns_map_all.json','w'),indent=0)
fs=set(d['kinds']['func_start']); print('func_start mapped',sum(1 for f in fs if hex(f) in fm),'of',len(fs))
