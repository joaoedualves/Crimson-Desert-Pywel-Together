import sys,json,pickle,time,bisect,re
sys.path.insert(0,'/home/claude/re')
from match import *
OLD='/mnt/user-data/uploads/CrimsonDesertCoop/DepotDownloader/exe-2976/bin64/CrimsonDesert.exe'
old=Img(OLD); new=Img('/home/claude/bins/new.exe')
nidx=pickle.load(open('/home/claude/port/idx_new.pkl','rb'))
fm=json.load(open('/home/claude/ns_map_funcs.json'))
un=[int(x,16) for x in json.load(open('/home/claude/ns_unres2.json'))['unresolved']]
M={int(k,16):int(v['new'],16) for k,v in fm.items()}
mk=re.compile(r'0x[0-9a-f]+')
def mask(seq):
    def f(m):
        v=int(m.group(0),16); return 'OPC' if 1000<=v<=3300 else m.group(0)
    return [mk.sub(f,x) for x in seq]
nc={}
def N(im,s,e):
    k=(id(im),s)
    if k not in nc: nc[k]=mask(norm(im,s,e))
    return nc[k]
def direct(s,e):
    if s in M: return M[s]
    if e-s>0x20000 or e-s<40: return None
    c=nidx.get(h(norm(old,s,min(e,s+400),24)),[])
    if not c or len(c)>20: return None
    full=N(old,s,e)
    sc=sorted(((ratio(full,N(new,cs,ce)),cs) for cs,ce in c if ce-cs<=0x20000),reverse=True)
    if sc and sc[0][0]>=0.92 and (len(sc)==1 or sc[1][0]<sc[0][0]-0.05):
        M[s]=sc[0][1]; return sc[0][1]
    return None
t0=time.time(); added=0; fail=[]
for rnd in range(3):
    for t in un:
        if hex(t) in fm: continue
        i=bisect.bisect_left(old.fstart,t); lo=hi=None
        for j in range(i-1,max(i-60,0),-1):
            r=direct(int(old.fstart[j]),int(old.fend[j]))
            if r: lo=(int(old.fstart[j]),r); break
        for j in range(i+1,min(i+60,len(old.fstart))):
            r=direct(int(old.fstart[j]),int(old.fend[j]))
            if r: hi=(int(old.fstart[j]),r); break
        if not lo or not hi or not (lo[1]<hi[1]) or hi[1]-lo[1]>0x80000: fail.append((hex(t),'no window',lo and hex(lo[1]),hi and hex(hi[1]))); continue
        a=bisect.bisect_right(new.fstart,lo[1]); b=bisect.bisect_left(new.fstart,hi[1])
        e=int(old.fend[i])
        if e-t>0x20000 or b-a>400 or b<=a: fail.append((hex(t),'window size',b-a)); continue
        full=N(old,t,e)
        sc=sorted(((ratio(full,N(new,int(new.fstart[k]),int(new.fend[k]))),int(new.fstart[k])) for k in range(a,b) if new.fend[k]-new.fstart[k]<=0x20000),reverse=True)
        if sc and sc[0][0]>=0.6 and (len(sc)==1 or sc[1][0]<sc[0][0]-0.05):
            fm[hex(t)]={'new':hex(sc[0][1]),'how':'neighbour window %d funcs, r %.3f'%(b-a,sc[0][0])}; M[t]=sc[0][1]; added+=1
        else: fail.append((hex(t),'no clear best',[(round(r,3),hex(f)) for r,f in sc[:3]],b-a))
    print('round',rnd,'added so far',added,round(time.time()-t0),flush=True)
left=[hex(t) for t in un if hex(t) not in fm]
json.dump(fm,open('/home/claude/ns_map_funcs.json','w'),indent=0); json.dump({'unresolved':left,'fail':fail[-len(left):]},open('/home/claude/ns_unres3.json','w'))
print('resolved',len(fm),'left',len(left)); 
import collections; print(collections.Counter(f[1] for f in fail[-len(left):]))
for f in fail[-12:]: print(f)
