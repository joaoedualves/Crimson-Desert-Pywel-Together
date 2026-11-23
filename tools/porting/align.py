import sys,re,difflib,json; sys.path.insert(0,'/home/claude'); sys.path.insert(0,'/home/claude/re')
from nsfind import *
IB=0x140000000
MAPJ=json.load(open('/home/claude/ns_map_all.json'))['funcs']
def ins(im,s,e=None):
    e=e or fend(im,s); return list(md.disasm(im.b(s,e-s),IB+s))
def shape(i):
    op=re.sub(r'0x[0-9a-f]+','N',i.op_str)
    op=re.sub(r'\b(r|e)?(ax|bx|cx|dx|si|di|bp|sp|8|9|1[0-5])[dwb]?\b','R',op)
    op=re.sub(r'\b[re]?(ax|bx|cx|dx|si|di)\b|\b[abcd][lh]\b|\b(sil|dil|bpl|spl)\b|\br\d+[dwb]?\b','R',op)
    op=re.sub(r'\bxmm\d+|ymm\d+','X',op)
    return i.mnemonic+' '+op
def fmap(o):
    v=MAPJ.get(hex(o)); 
    if v and v.get('new'): return int(v['new'],16)
    return direct(o)
def align(o,n=None,show=None):
    n=n or fmap(o); A=ins(old,o); B=ins(new,n)
    sa=[shape(i) for i in A]; sb=[shape(i) for i in B]
    sm=difflib.SequenceMatcher(None,sa,sb,autojunk=False)
    m={}
    for a,b,k in sm.get_matching_blocks():
        for t in range(k): m[A[a+t].address-IB]=B[b+t]
    return A,B,m,sm.ratio()
def site(o_func,o_site,ctx=4,n=None):
    A,B,m,r=align(o_func,n)
    ia=[i.address-IB for i in A].index(o_site) if o_site in [i.address-IB for i in A] else None
    print('== old %x in %x -> new func %x (r %.2f)'%(o_site,o_func,(n or fmap(o_func)),r))
    if ia is None: print('  site not an instruction start'); return None
    for i in A[max(0,ia-ctx):ia+ctx+1]:
        j=m.get(i.address-IB)
        print('  %s %x %-34s | %s'%('>' if i.address-IB==o_site else ' ',i.address-IB,(i.mnemonic+' '+i.op_str)[:34], ('%x %s %s'%(j.address-IB,j.mnemonic,j.op_str)) if j else '--'))
    j=m.get(o_site); return j.address-IB if j else None
