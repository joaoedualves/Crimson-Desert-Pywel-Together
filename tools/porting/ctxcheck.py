import sys,re; sys.path.insert(0,'/home/claude')
from align import *
def arrays(s):
    return {m.group(1):bytes(int(x,16) for x in re.findall(r'0x([0-9A-Fa-f]{2})',m.group(2))) for m in re.finditer(r'constexpr uint8_t (k\w+)\[\d*\] = \{([^}]*)\}',s)}
def check(path):
    s=open(path).read(); A=arrays(s)
    consts={m.group(1):int(m.group(2),16) for m in re.finditer(r'constexpr uintptr_t (k\w+) = 0x([0-9A-Fa-f]+)',s)}
    bad=[]
    for m in re.finditer(r'\{(0x[0-9A-Fa-f]+|k\w+), (k\w+)\}',s):
        a=m.group(1); a=int(a,16) if a.startswith('0x') else consts.get(a)
        b=A.get(m.group(2))
        if a is None or b is None: continue
        ok=new.b(a,len(b))==b
        if not ok: bad.append((hex(a),m.group(2)))
    return bad
if __name__=='__main__':
    for p in sys.argv[1:]: print(p.split('/')[-1],check(p))
