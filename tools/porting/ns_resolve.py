import sys,json,pickle,time,difflib,collections,bisect
sys.path.insert(0,'/home/claude/re')
import numpy as np
from match import *
OLD='/mnt/user-data/uploads/CrimsonDesertCoop/DepotDownloader/exe-2976/bin64/CrimsonDesert.exe'
old=Img(OLD); new=Img('/home/claude/bins/new.exe')
nidx=pickle.load(open('/home/claude/port/idx_new.pkl','rb'))
IB=0x140000000
def nt(im,s,e):
    """normalized list + parallel direct-branch targets (rva or None)"""
    code=im.b(s,e-s); out=[]; tg=[]
    for i in md.disasm(code,IB+s):
        m=i.mnemonic
        if m=='.byte': out.append('DB'); tg.append(None); continue
        if m in BR:
            if i.operands and i.operands[0].type==X86_OP_IMM:
                t=(i.operands[0].imm&0xFFFFFFFFFFFFFFFF)-IB
                out.append(m+' REL'); tg.append(t if (m=='call' or not (s<=t<e)) else None)
            else: out.append(m+' '+i.op_str); tg.append(None)
        else:
            ops=[]; t=None
            for o in i.operands:
                if o.type==X86_OP_MEM:
                    if o.mem.base==X86_REG_RIP:
                        ops.append('[RIP]')
                        if m=='lea': t=i.address+i.size+o.mem.disp-IB
                    else: ops.append('[%d+%d*%d+%#x]'%(o.mem.base,o.mem.index,o.mem.scale,o.mem.disp))
                elif o.type==X86_OP_IMM:
                    v=o.imm&0xFFFFFFFFFFFFFFFF; ops.append('ADDR' if IB<=v<IB+0x20000000 else hex(v))
                else: ops.append(i.reg_name(o.reg))
            out.append(m+' '+','.join(ops)); tg.append(t)
    return out,tg
cache={}
def NT(im,s,e):
    k=(id(im),s)
    if k not in cache: cache[k]=nt(im,s,e)
    return cache[k]
def isfs(im,v):
    i=bisect.bisect_left(im.fstart,v); return i<len(im.fstart) and im.fstart[i]==v
def fend(im,s):
    i=bisect.bisect_left(im.fstart,s); return int(im.fend[i])
# old call xrefs (E8/E9 rel32) for all exec sections
def xref_map(im,targets):
    tset=np.array(sorted(targets),dtype=np.int64); res=collections.defaultdict(list)
    for n,va,vs,ch in im.secs:
        if not (ch&0x20000000): continue
        a=np.frombuffer(bytes(im.mem[va:va+vs]),dtype=np.uint8)
        for opc in (0xE8,0xE9):
            pos=np.flatnonzero(a[:-5]==opc)
            rel=(a[pos+1].astype(np.int64)|(a[pos+2].astype(np.int64)<<8)|(a[pos+3].astype(np.int64)<<16)|(a[pos+4].astype(np.int64)<<24))
            rel=np.where(rel>=2**31,rel-2**32,rel); tgt=va+pos+5+rel
            ok=np.isin(tgt,tset)
            for p,t in zip((va+pos[ok]).tolist(),tgt[ok].tolist()): res[t].append(p)
    return res
d=json.load(open('/home/claude/ns_addrs.json')); res=json.load(open('/home/claude/ns_res.json'))
todo=sorted(int(k,16) for k in res)
M={}  # old start -> (new start, how)
for k,v in res.items():
    b=v['best']
    if b and b[0][0]>=0.9 and (len(b)==1 or b[1][0]<b[0][0]-0.03): M[int(k,16)]=(b[0][1],'direct %.3f'%b[0][0])
import struct,csv
R=pickle.load(open('/home/claude/ns_rtti.pkl','rb')); VT={}
for ov,nv in R['vmap'].items():
    for k in range(400):
        a=struct.unpack_from('<Q',old.mem,ov+8*k)[0]-IB; b=struct.unpack_from('<Q',new.mem,nv+8*k)[0]-IB
        if (k and (ov+8*k) in R['old']) or not isfs(old,a) or not isfs(new,b): break
        VT.setdefault(a,collections.Counter())[b]+=1
print('anchors',len(M),'vt slot pairs',len(VT),flush=True)
def direct(s):
    """unique good match of an arbitrary old function, cached in M"""
    if s in M: return M[s][0]
    e=fend(old,s)
    if e-s>0x20000: return None
    full,_=NT(old,s,e)
    if s in VT and len(VT[s])==1:
        cs=next(iter(VT[s])); ce=fend(new,cs)
        if ce-cs<=0x20000:
            r=ratio(full,NT(new,cs,ce)[0])
            if r>=0.6 and (len(full)>=12 or r==1.0):
                M[s]=(cs,'vtable slot %.3f'%r); return cs
    c=nidx.get(h(norm(old,s,min(e,s+400),24)),[])
    if not c or len(c)>60: return None
    sc=sorted(((ratio(full,NT(new,cs,ce)[0]),cs) for cs,ce in c),reverse=True)
    if sc[0][0]>=0.8 and (len(sc)==1 or sc[1][0]<sc[0][0]-0.04):
        M[s]=(sc[0][1],'direct %.3f'%sc[0][0]); return sc[0][1]
    return None
def by_callees(t):
    e=fend(old,t)
    if e-t>0x20000: return None
    full,_=NT(old,t,e)
    c=nidx.get(h(norm(old,t,min(e,t+400),24)),[])
    if not c or len(c)>400: return None
    sc=[]
    for cs,ce in c:
        if ce-cs>0x20000: continue
        r=ratio(full,NT(new,cs,ce)[0]) if len(c)<=80 else (1.0 if NT(new,cs,ce)[0]==full else 0)
        if r<0.9: continue
        ag=dis=0
        for ot,nt_ in align(t,cs):
            if not isfs(old,ot) or ot==t: continue
            d_=direct(ot)
            if d_ is None: continue
            if d_==nt_: ag+=1
            else: dis+=1
        sc.append((ag-3*dis,ag,dis,r,cs))
    sc.sort(reverse=True)
    if sc and sc[0][1]>=1 and sc[0][2]==0 and (len(sc)==1 or sc[1][0]<sc[0][0]):
        return sc[0][4],'callees agree %d (r %.3f, %d cands)'%(sc[0][1],sc[0][3],len(sc))
    return None
def align(os_,ns_):
    """yield (old target, new target) pairs for aligned branch/lea instructions"""
    a,ta=NT(old,os_,fend(old,os_)); b,tb=NT(new,ns_,fend(new,ns_))
    sm=difflib.SequenceMatcher(None,a,b,autojunk=False)
    for i,j,n in sm.get_matching_blocks():
        for k in range(n):
            if ta[i+k] is not None and tb[j+k] is not None: yield ta[i+k],tb[j+k]
t0=time.time()
xr=xref_map(old,todo); print('xrefs',sum(len(v) for v in xr.values()),round(time.time()-t0),flush=True)
votes=collections.defaultdict(collections.Counter)
for rnd in range(6):
    added=0
    # (1) callers of unresolved targets
    for t in todo:
        if t in M: continue
        for p in xr.get(t,[])[:40]:
            f=old.func_at(p)
            if not f: continue
            cs=direct(f[0])
            if cs is None: continue
            for ot,nt_ in align(f[0],cs):
                if ot==t and isfs(new,nt_): votes[t][nt_]+=1
    # (2) callees/refs inside resolved functions (propagate to todo members)
    for s,(n_,how) in list(M.items()):
        for ot,nt_ in align(s,n_):
            if ot in res and ot not in M and isfs(new,nt_): votes[ot][nt_]+=1
    for t in todo:
        if t not in M: direct(t)
    for t in todo:
        if t in M or votes.get(t): continue
        r_=by_callees(t)
        if r_: M[t]=r_; added+=1
    for t,c in votes.items():
        if t in M: continue
        (n1,c1),*rest=c.most_common(2)
        if not rest or rest[0][1]*3<=c1:
            M[t]=(n1,'xref votes %d/%d'%(c1,sum(c.values()))); added+=1
    print('round',rnd,'added',added,'resolved',sum(1 for t in todo if t in M),'of',len(todo),round(time.time()-t0),flush=True)
    if not added: break
out={hex(t):{'new':hex(M[t][0]),'how':M[t][1]} for t in todo if t in M}
json.dump(out,open('/home/claude/ns_map_funcs.json','w'),indent=0)
un=[t for t in todo if t not in M]
json.dump({'unresolved':[hex(t) for t in un],'votes':{hex(t):[(hex(a),b) for a,b in votes[t].most_common(3)] for t in un if votes[t]}},open('/home/claude/ns_unres.json','w'))
print('unresolved',len(un),'with no xrefs',sum(1 for t in un if not xr.get(t)))
