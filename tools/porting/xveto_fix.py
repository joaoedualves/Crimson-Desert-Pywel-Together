import sys,re,difflib; sys.path.insert(0,'/home/claude')
from align import *
from vtfind import VM,q
def fnv1a(b):
    h=0xCBF29CE484222325
    for x in b: h=((h^x)*0x100000001B3)&0xFFFFFFFFFFFFFFFF
    return h
def arr(text,name):
    m=re.search(r'constexpr uint8_t %s\[\] = \{([^}]*)\}'%name,text); return bytes(int(x,16) for x in re.findall(r'0x([0-9A-Fa-f]{2})',m.group(1)))
def patchlen(a):
    t=0
    for i in md.disasm(new.b(a,32),IB+a):
        t+=i.size
        if t>=5: return t
PAT=r'\{Fn::(\w+), 0x([0-9A-F]+), (\d+), (k\w+), ProcAt::(\w+)(?:,\s*0x([0-9A-F]+), (k\w+), 0x([0-9A-F]+), (k\w+),\s*0x([0-9A-F]+),\s*0x([0-9A-F]+))?\}'
def fix(cur,src,report=True):
    vt=0x558C080; nv=VM[vt]
    slots={'InputBlock':0x150,'UIControl':0x158,'ChangePhasePanelTag':0x160,'UIShowMinimap':0x168,'UIFindPath':0x170,'UIStageIcon':0x1F8,'UIStageGuide':0x238,'UIStageBackgroundGuide':0x240,'UIHint':0x248,'UIHintEnd':0x250,'DeactiveSequencerCamera':0x1B8,'DeactiveSequencerCameraEnd':0x1C0,'SequencerCamera':0x1C8,'DeactivateSequencerCameraFadeIn':0x1D0,'DeactivateSequencerCameraFadeInEnd':0x1D8,'CameraPreset':0x1E0,'CameraPresetEnd':0x1E8,'ForceLockOn':0x1F0,'LetterBox':0x208,'FadeInOut':0x210,'LookAt':0x220,'LookAtEnd':0x228,'PostProcess':0x260}
    O={m.group(1):m for m in re.finditer(PAT,src)}
    out=cur
    for m in list(re.finditer(PAT,cur)):
        k=m.group(1)
        if k not in slots: continue
        om=O[k]; ohook=int(om.group(2),16)
        oentry=q(old,vt+slots[k]); nentry=q(new,nv+slots[k]+8); nhook=nentry+(ohook-oentry)
        name=m.group(4); ob=arr(src,om.group(4))
        nb=new.b(nhook,len(ob)); pl=patchlen(nhook) if ohook==oentry else int(m.group(3))
        rep='{Fn::%s, 0x%X, %d, %s, ProcAt::%s'%(k,nhook,pl,name,m.group(5))
        if m.group(6):
            res_o=int(om.group(8),16); obo=arr(src,om.group(9)); nres=nentry+(res_o-oentry)
            # find the resume in new by aligning the way-out bytes near the relative spot
            seg=bytes(new.b(nentry,0x3000)); idx=[i.start() for i in re.finditer(re.escape(arr(cur,m.group(9))),seg)]
            if idx: nres=nentry+min(idx,key=lambda x:abs(x-(res_o-oentry)))
            A=[shape(i) for i in md.disasm(old.b(ohook,res_o-ohook),IB+ohook)]; B=[shape(i) for i in md.disasm(new.b(nhook,nres-nhook),IB+nhook)]
            r=difflib.SequenceMatcher(None,A,B,autojunk=False).ratio()
            ln=nres-nhook+len(arr(cur,m.group(9)))
            h=fnv1a(new.b(nhook,ln)) if r>=0.97 else int(m.group(11),16)
            rep+=', 0x%X, %s, 0x%X, %s,\n     0x%X, 0x%016X'%(nentry,m.group(7),nres,m.group(9),ln,h)
            if report: print('  %s veto span ratio %.3f -> %s'%(k,r,'hash recomputed' if r>=0.97 else 'stays a probe'))
        rep+='}'
        out=out.replace(m.group(0),rep)
        out=re.sub(r'(constexpr uint8_t %s\[\] = \{)[^}]*\}'%name,lambda mm: mm.group(1)+', '.join('0x%02X'%b for b in nb)+'}',out,count=1)
        if report and m.group(0)!=rep: print('  %-34s %x -> %x patch %d'%(k,int(m.group(2),16),nhook,pl))
    return out
