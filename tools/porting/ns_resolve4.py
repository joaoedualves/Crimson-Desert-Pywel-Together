import sys,json,collections,bisect,difflib
sys.path.insert(0,'/home/claude'); sys.path.insert(0,'/home/claude/re')
import nsfind as F
from match import *
IB=0x140000000
d=json.load(open('/home/claude/ns_addrs.json')); fm=json.load(open('/home/claude/ns_map_funcs.json'))
need=set()
for a in d['kinds']['func_start']: need.add(a)
for a in d['kinds']['func_mid']:
    f=F.old.func_at(a)
    if f: need.add(f[0])
un=[a for a in sorted(need) if hex(a) not in fm]
print('functions still unmapped',len(un),flush=True)
def by_callers(s):
    cs=set(F.OCALLERS.get(s,[]))
    votes=collections.Counter()
    for c in list(cs)[:30]:
        n=F.direct(c)
        if not n: continue
        # align caller bodies, find call sites targeting s and their counterpart
        a=[];b=[]
        for i in md.disasm(F.old.b(c,F.fend(F.old,c)-c),IB+c): a.append(i)
        for i in md.disasm(F.new.b(n,F.fend(F.new,n)-n),IB+n): b.append(i)
        na=[x.mnemonic for x in a]; nb=[x.mnemonic for x in b]
        sm=difflib.SequenceMatcher(None,na,nb,autojunk=False)
        for i,j,k in sm.get_matching_blocks():
            for x in range(k):
                ia=a[i+x]; ib=b[j+x]
                if ia.mnemonic=='call' and ia.operands and ia.operands[0].type==X86_OP_IMM and (ia.operands[0].imm&0xFFFFFFFFFFFFFFFF)-IB==s:
                    if ib.operands and ib.operands[0].type==X86_OP_IMM:
                        t=(ib.operands[0].imm&0xFFFFFFFFFFFFFFFF)-IB
                        if F.isfs(F.new,t): votes[t]+=1
    return votes
added=0; out=[]
for s in un:
    e=F.fend(F.old,s)
    r=F.direct(s,thr=0.6,gap=0.05)
    if r: fm[hex(s)]={'new':hex(r),'how':'direct(loose)'}; added+=1; continue
    v=by_callers(s)
    if v:
        (n1,c1),*rest=v.most_common(2)
        if not rest or rest[0][1]*2<=c1:
            full=F.N(F.old,s); rr=ratio(full,F.N(F.new,n1))
            fm[hex(s)]={'new':hex(n1),'how':'callers %d/%d, r %.3f'%(c1,sum(v.values()),rr)}; added+=1; continue
    bc=F.by_callees(s,verbose=False)
    if bc and bc[0][0]>=2 and (len(bc)==1 or bc[1][0]<bc[0][0]) and bc[0][1]>=0.25:
        fm[hex(s)]={'new':hex(bc[0][2]),'how':'callees %d, r %.3f'%(bc[0][0],bc[0][1])}; added+=1; continue
    out.append((hex(s),e-s,v.most_common(2),bc[:2]))
print('added',added,'left',len(out))
for x in out: print(x)
json.dump(fm,open('/home/claude/ns_map_funcs.json','w'),indent=0)
