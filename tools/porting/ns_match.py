import sys,json,pickle,time
sys.path.insert(0,'/home/claude/re')
from match import *
old=Img('/mnt/user-data/uploads/CrimsonDesertCoop/DepotDownloader/exe-2976/bin64/CrimsonDesert.exe'); new=Img('/home/claude/bins/new.exe')
nidx=pickle.load(open('/home/claude/port/idx_new.pkl','rb'))
d=json.load(open('/home/claude/ns_addrs.json'))
todo=sorted(set(d['kinds']['func_start'])|set(old.func_at(v)[0] for v in d['kinds']['func_mid']))
lim=int(sys.argv[1]) if len(sys.argv)>1 else len(todo)
res={}; t0=time.time()
try: res=json.load(open('/home/claude/ns_res.json'))
except Exception: pass
for i,t in enumerate(todo[:lim]):
    if hex(t) in res: continue
    s,e=old.func_at(t); full=norm(old,s,e)
    c=nidx.get(h(norm(old,s,min(e,s+400),24)),[])
    best=sorted(((ratio(full,norm(new,cs,ce)),cs) for cs,ce in c[:40]),reverse=True)[:2]
    res[hex(t)]={'size':e-s,'best':[(round(r,3),cs) for r,cs in best],'ncand':len(c)}
    if i%50==0: json.dump(res,open('/home/claude/ns_res.json','w')); print(i,len(todo),round(time.time()-t0),flush=True)
json.dump(res,open('/home/claude/ns_res.json','w')); print('done',len(res),round(time.time()-t0))
