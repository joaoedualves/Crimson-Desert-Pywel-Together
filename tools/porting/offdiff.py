import sys,re,glob,difflib,collections,os; sys.path.insert(0,'/home/claude/re')
from capstone import *
md2=Cs(CS_ARCH_X86,CS_MODE_64)
def arrays(text):
    out={}
    for m in re.finditer(r'constexpr uint8_t (k\w+)\[\d*\] = \{([^}]*)\}',text):
        b=re.findall(r'0x([0-9A-Fa-f]{2})',m.group(2))
        if b: out[m.group(1)]=bytes(int(x,16) for x in b)
    return out
def shape(i):
    op=re.sub(r'0x[0-9a-f]+','N',i.op_str); return i.mnemonic+' '+op
def disp(i):
    if 'rip' in i.op_str: return None
    m=re.search(r'\[(\w+)(?: \+ \w+\*\d)? ([+-]) (0x[0-9a-f]+)\]',i.op_str)
    if m: return (m.group(1), int(m.group(3),16)*(1 if m.group(2)=='+' else -1))
    return None
res=collections.defaultdict(list)
for nf in sorted(glob.glob('/home/claude/nsport/src/mod/**/*.*',recursive=True)):
    rel=nf.split('/src/',1)[1]; of='/home/claude/ns/src/'+rel
    if not os.path.exists(of) or not nf.endswith(('.cpp','.h')): continue
    A=arrays(open(of,errors='replace').read()); B=arrays(open(nf,errors='replace').read())
    for k in A:
        if k not in B or A[k]==B[k]: continue
        ia=list(md2.disasm(A[k],0)); ib=list(md2.disasm(B[k],0))
        sm=difflib.SequenceMatcher(None,[shape(i) for i in ia],[shape(i) for i in ib],autojunk=False)
        for a,b,n in sm.get_matching_blocks():
            for t in range(n):
                da,db=disp(ia[a+t]),disp(ib[b+t])
                if da and db and da[1]!=db[1] and abs(da[1])<0x4000:
                    res[rel].append((k,'%s %s'%(ia[a+t].mnemonic,ia[a+t].op_str),'%s %s'%(ib[b+t].mnemonic,ib[b+t].op_str),da[0],da[1],db[1]))
for f,l in res.items():
    print('==',f)
    for x in l:
        pass
        print('   %-22s %-40s -> %s'%(x[0],x[1],x[2]))
