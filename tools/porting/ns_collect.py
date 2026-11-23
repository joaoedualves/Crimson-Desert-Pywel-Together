import re,os,json,sys
sys.path.insert(0,'/home/claude/re')
vals={}
for root,_,fs in os.walk('/home/claude/ns/src'):
    for f in fs:
        if not f.endswith(('.cpp','.h')) or f=='eerrno_names.h': continue
        p=os.path.join(root,f); s=open(p,errors='replace').read()
        s2=re.sub(r'//.*','',s)
        for m in re.finditer(r'\b0x([0-9A-Fa-f]{5,8})\b',s2):
            v=int(m.group(1),16)
            if 0x1000<=v<0x7400000: vals.setdefault(v,set()).add(os.path.relpath(p,'/home/claude/ns/src'))
from match import *
old=Img('/mnt/user-data/uploads/CrimsonDesertCoop/DepotDownloader/exe-2976/bin64/CrimsonDesert.exe')
secs=[(s.VirtualAddress,s.VirtualAddress+s.Misc_VirtualSize,s.Name.rstrip(b'\0').decode(),s.Characteristics) for s in old.pe.sections] if hasattr(old,'pe') else []
print(secs)
kinds={'func_start':[],'func_mid':[],'data':[],'other':[]}
for v in sorted(vals):
    f=old.func_at(v)
    if f and f[0]==v: kinds['func_start'].append(v)
    elif f: kinds['func_mid'].append(v)
    else:
        sec=[n for a,b,n,c in secs if a<=v<b]
        (kinds['data'] if sec and sec[0] not in ('.text',) else kinds['other']).append(v)
print({k:len(x) for k,x in kinds.items()})
json.dump({'kinds':kinds,'files':{hex(k):sorted(v) for k,v in vals.items()}},open('/home/claude/ns_addrs.json','w'))
