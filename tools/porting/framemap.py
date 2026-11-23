import sys,re,collections,difflib; sys.path.insert(0,'/home/claude')
from align import *
def fdisp(i,base):
    m=re.search(r'\[%s(?: ([+-]) (0x[0-9a-f]+))?\]'%base,i.op_str)
    if not m: return None
    return 0 if not m.group(1) else int(m.group(2),16)*(1 if m.group(1)=='+' else -1)
def framemap(o,n,base='rbp'):
    A=ins(old,o); B=ins(new,n)
    sm=difflib.SequenceMatcher(None,[shape(i) for i in A],[shape(i) for i in B],autojunk=False)
    V=collections.defaultdict(collections.Counter)
    for a,b,k in sm.get_matching_blocks():
        for t in range(k):
            da,db=fdisp(A[a+t],base),fdisp(B[b+t],base)
            if da is not None and db is not None: V[da][db]+=1
    return {k:c.most_common(1)[0][0] for k,c in V.items()}, V, sm.ratio()
def fm_of_site(old_site,new_site=None,base='rbp'):
    o=old.func_at(old_site)[0]; n=new.func_at(new_site)[0] if new_site else fmap(o)
    return framemap(o,n,base)+(o,n)
