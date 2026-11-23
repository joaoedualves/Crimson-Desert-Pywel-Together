import re,os,json,csv,sys,shutil,collections
sys.path.insert(0,'/home/claude/re')
from pe import Img
SRC='/home/claude/ns/src'; DST='/home/claude/nsport/src'
m=json.load(open('/home/claude/ns_map_all.json')); d=json.load(open('/home/claude/ns_addrs.json'))
amap={}
for k in ('funcs','mid','data'):
    for h,v in m[k].items():
        if v['new']: amap[int(h,16)]=int(v['new'],16)
known=set(d['kinds']['func_start'])|set(d['kinds']['func_mid'])|set(d['kinds']['data'])
# manual overrides by constant name (hand-verified port of the first mod)
def consts(p):
    s=open(p,errors='replace').read(); return dict((a,int(b,16)) for a,b in re.findall(r'constexpr\s+\w+\s+(k\w+)\s*=\s*0x([0-9A-Fa-f]+)',s))
man_a=consts(SRC+'/mod/game/offsets.h'); man_b=consts('/home/claude/src/b2663/src/mod/game/offsets.h')
named={k:man_b[k] for k in man_a if k in man_b and man_a[k]!=man_b[k]}
for k,v in named.items():
    if man_a[k]>=0x10000: amap[man_a[k]]=v
# opcodes
oldop={}; 
for row in csv.DictReader(open('/home/claude/ns/docs/research/data/troc_messages_2976.csv')): oldop[int(row['id'])]=row['name']
newop={k:v['id'] for k,v in json.load(open('/home/claude/port/opmaps_2663.json'))['m2663'].items()}
opmap={o:newop[n] for o,n in oldop.items() if n in newop}
print('addr map',len(amap),'named overrides',len(named),'opcode map',len(opmap),'old opcodes without new',len(oldop)-len(opmap))
old=Img('/mnt/user-data/uploads/CrimsonDesertCoop/DepotDownloader/exe-2976/bin64/CrimsonDesert.exe'); new=Img('/home/claude/bins/new.exe')
rep=collections.defaultdict(lambda: collections.Counter()); unm=collections.defaultdict(set); arr_rep=[]
hexre=re.compile(r'\b0[xX]([0-9A-Fa-f]{5,8})\b'); numre=re.compile(r'(?<![\w.])(\d{4})(?![\w.])')
opline=re.compile(r'kOp|[Oo]pcode|\bop\b|\bops\b|case\s+\d{4}\s*:|uint16_t\s+k\w+\s*=\s*\d{4}')
def fix_code(code,f,in_named):
    def hx(mm):
        v=int(mm.group(1),16)
        if v in amap:
            n=amap[v]; rep[f]['addr']+=1 if n!=v else 0
            s=mm.group(1); out=('%X' if s.upper()==s else '%x')%n
            return mm.group(0)[:2]+out
        if v in known: unm[f].add(v)
        return mm.group(0)
    code=hexre.sub(hx,code)
    if opline.search(code) or f.endswith('owner_fanout_writers.h'):
        def op(mm):
            v=int(mm.group(1))
            if v in opmap and opmap[v]!=v: rep[f]['opcode']+=1; return str(opmap[v])
            if v in oldop and v not in opmap: unm[f].add(-v)
            return mm.group(0)
        code=numre.sub(op,code)
    return code
arrre=re.compile(r'((?:constexpr|const)\s+(?:std::)?uint8_t\s+(k\w+)\s*\[\s*\d*\s*\]\s*=\s*\{)([^}]*)(\})',re.S)
arr2=re.compile(r'(\{)((?:\s*0x[0-9A-Fa-f]{2}\s*,){4,}\s*0x[0-9A-Fa-f]{2}\s*,?\s*)(\})')
oldcode=[(va,bytes(old.mem[va:va+vs])) for n,va,vs,ch in old.secs if ch&0x20000000]
for root,_,fs in os.walk(SRC):
    for fn in fs:
        p=os.path.join(root,fn); rel=os.path.relpath(p,SRC); q=os.path.join(DST,rel); os.makedirs(os.path.dirname(q),exist_ok=True)
        if not fn.endswith(('.cpp','.h')) or fn=='eerrno_names.h': shutil.copy(p,q); continue
        raw=open(p,'rb').read(); crlf=b'\r\n' in raw; text=raw.decode('utf-8',errors='surrogateescape').replace('\r\n','\n')
        addrs_here=[int(h,16) for h,files in d['files'].items() if rel in files]
        # byte arrays first (on original text)
        def arr(mm):
            body=mm.group(3); toks=re.findall(r'0[xX][0-9A-Fa-f]{1,2}\b|\b\d{1,3}\b',re.sub(r'//.*','',body))
            try: b=bytes(int(t,0) for t in toks)
            except ValueError: return mm.group(0)
            if len(b)<5: return mm.group(0)
            hits=[a for a in addrs_here if old.b(a,len(b))==b] or [a for a in known if old.b(a,len(b))==b][:8]
            if not hits and len(b)>=6:
                for va,buf in oldcode:
                    p_=buf.find(b)
                    while p_!=-1 and len(hits)<3: hits.append(va+p_); p_=buf.find(b,p_+1)
                if len(hits)>1 and any(a in amap for a in hits): hits=[a for a in hits if a in amap]
            if not hits: arr_rep.append((rel,mm.group(2),'NO SITE (%d bytes)'%len(b))); return mm.group(0)
            news={new.b(amap[a],len(b)) for a in hits if a in amap}
            if not news: arr_rep.append((rel,mm.group(2),'site %s unmapped'%hex(hits[0]))); return mm.group(0)
            if len(news)>1: arr_rep.append((rel,mm.group(2),'ambiguous new bytes')); return mm.group(0)
            nb=news.pop()
            if nb==b: arr_rep.append((rel,mm.group(2),'same')); return mm.group(0)
            arr_rep.append((rel,mm.group(2),'UPDATED %d of %d bytes differ'%(sum(x!=y for x,y in zip(b,nb)),len(b))))
            return mm.group(1)+', '.join('0x%02X'%x for x in nb)+mm.group(4)
        text=arrre.sub(arr,text)
        def arr_anon(mm):
            class M:
                def group(self,i): return {0:mm.group(0),1:mm.group(1),2:'<anon>',3:mm.group(2),4:mm.group(3)}[i]
            return arr(M())
        text=arr2.sub(arr_anon,text)
        out=[]
        for line in text.split('\n'):
            i=line.find('//'); code,cm=(line,'') if i<0 else (line[:i],line[i:])
            out.append(fix_code(code,rel,False)+cm)
        text='\n'.join(out)
        if rel=='mod/game/offsets.h':
            for k,v in named.items():
                text=re.sub(r'(constexpr\s+\w+\s+'+k+r'\s*=\s*)0x[0-9A-Fa-f]+',lambda mm: mm.group(1)+('0x%X'%v),text)
        open(q,'wb').write((text.replace('\n','\r\n') if crlf else text).encode('utf-8',errors='surrogateescape'))
tot=collections.Counter()
for f,c in rep.items(): tot.update(c)
print('replaced',dict(tot),'files touched',len(rep))
print('byte arrays',collections.Counter(x[2].split(' ')[0] for x in arr_rep))
un_addr=sum(1 for f in unm for v in unm[f] if v>0); un_op=sorted({-v for f in unm for v in unm[f] if v<0})
print('unmapped address uses',un_addr,'in',sum(1 for f in unm if any(v>0 for v in unm[f])),'files; old opcodes with no new id:',[(o,oldop[o]) for o in un_op][:20])
json.dump({'unmapped':{f:sorted(hex(v) if v>0 else str(v) for v in s) for f,s in unm.items()},'arrays':arr_rep,'named':{k:hex(v) for k,v in named.items()}},open('/home/claude/ns_apply_report.json','w'),indent=0)

# ---- owner_fanout_writers.h: rebuild the table (must stay sorted by ret, no duplicates)
p=SRC+'/mod/session2/owner_fanout_writers.h'; q=DST+'/mod/session2/owner_fanout_writers.h'
raw=open(p,'rb').read(); crlf=b'\r\n' in raw; t=raw.decode().replace('\r\n','\n')
rows=[]; dropped=[]
for mm in re.finditer(r'\{0x([0-9A-Fa-f]+), 0x([0-9A-Fa-f]+), (\d+), (\d+), "(\w+)"\},',t):
    ret,wr,op,fi,name=int(mm.group(1),16),int(mm.group(2),16),int(mm.group(3)),mm.group(4),mm.group(5)
    if ret in amap and wr in amap and name in newop: rows.append((amap[ret],amap[wr],newop[name],fi,name))
    else: dropped.append(name)
rows.sort(); rows=[r for i,r in enumerate(rows) if i==0 or r[0]!=rows[i-1][0]]
a=t.index('inline constexpr WriterRow kWriters[] = {'); b=t.index('};',a)
t=t[:a]+'inline constexpr WriterRow kWriters[] = {\n'+''.join('    {0x%X, 0x%X, %d, %s, "%s"},\n'%r for r in rows)+t[b:]
open(q,'wb').write((t.replace('\n','\r\n') if crlf else t).encode())
print('writers table rows',len(rows),'dropped',len(dropped),dropped[:12])

# ---- build 3663: the game's section names changed (.rsrc -> .reloc, .xtls -> .data1, bounds 0x1000..0x5613000)
import glob
fixes=[(r'\.rsrc\\0\\0\\0',r'.reloc\\0\\0'),(r'"\.rsrc"','".reloc"'),(r'"\.xtls"','".data1"'),(r'0x51EB000','0x5613000'),(r'0x51eb000','0x5613000')]
nfix=collections.Counter()
for p in glob.glob(DST+'/**/*.cpp',recursive=True)+glob.glob(DST+'/**/*.h',recursive=True):
    raw=open(p,'rb').read(); t=raw.decode('utf-8',errors='surrogateescape'); t0=t
    for a,b in fixes:
        t,n=re.subn(a,b,t); nfix[a]+=n
    if t!=t0: open(p,'wb').write(t.encode('utf-8',errors='surrogateescape'))
print('section fixes',dict(nfix))
