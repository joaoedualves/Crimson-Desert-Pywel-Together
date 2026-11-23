# Manual fixes for build 3663 applied on top of ns_apply.py's output (re-runnable).
import re,sys,json,bisect,struct
sys.path.insert(0,'/home/claude/re')
from match import *
DST='/home/claude/nsport/src'
OLD='/mnt/user-data/uploads/CrimsonDesertCoop/DepotDownloader/exe-2976/bin64/CrimsonDesert.exe'
old=Img(OLD); new=Img('/home/claude/bins/new.exe')
m=json.load(open('/home/claude/ns_map_all.json'))
amap={}
for k in ('funcs','mid','data'):
    for h_,v in m[k].items():
        if v['new']: amap[int(h_,16)]=int(v['new'],16)
def fend(im,s):
    i=bisect.bisect_left(im.fstart,s); return int(im.fend[i])
def rw(path,fn):
    raw=open(path,'rb').read(); crlf=b'\r\n' in raw; s=raw.decode('utf-8',errors='surrogateescape').replace('\r\n','\n')
    t=fn(s)
    if t!=s: open(path,'wb').write((t.replace('\n','\r\n') if crlf else t).encode('utf-8',errors='surrogateescape')); print('patched',path.split('/src/')[1])
def fnv1a(b):
    h=0xCBF29CE484222325
    for x in b: h=((h^x)*0x100000001B3)&0xFFFFFFFFFFFFFFFF
    return h
# ---- 1. Body{rva, end, fnv}: rva/end already mapped by ns_apply (end may be stale); recompute from the OLD source
def fix_bodies(rel):
    src=open('/home/claude/ns/src/'+rel,errors='replace').read()
    bodies=re.findall(r'constexpr Body (k\w+)\{0x([0-9A-Fa-f]+), 0x([0-9A-Fa-f]+), 0x([0-9A-Fa-f]+)ull\};',src)
    def fn(s):
        for name,a,b,hh in bodies:
            oa,ob=int(a,16),int(b,16)
            na=amap.get(oa)
            if not na: print('  body',name,'start unmapped; left as is'); continue
            nb=amap.get(ob)
            if not nb or nb<=na: nb=na+(ob-oa)  # same length (pdata chunks make fend unreliable)
            h2=fnv1a(new.b(na,nb-na))
            s=re.sub(r'constexpr Body '+name+r'\{0x[0-9A-Fa-f]+, 0x[0-9A-Fa-f]+, 0x[0-9A-Fa-f]+ull\};[^\n]*',
                     'constexpr Body %s{0x%X, 0x%X, 0x%016Xull};  // build 3663: %d bytes (old %d)'%(name,na,nb,h2,nb-na,ob-oa),s)
        return s
    rw(DST+'/'+rel,fn)
for rel in ('mod/session2/guest_sched_load.cpp','mod/session2/guest_sched_bind.cpp'): fix_bodies(rel)
# ---- 2. clock gate: SyncFieldTimeOnMove is inlined into its caller on 3663
exec(open('/home/claude/ns_manual_clockgate.py').read())
# ---- 3. identity fix: ServerStatusActorComponent vtable +0x50 is a thunk (-> .data1 code) on 3663
def fix_identity(s):
    s=re.sub(r'constexpr uintptr_t kStatusVtable = 0x5F63430;[^\n]*','constexpr uintptr_t kStatusVtable = 0x5F63430;    // ServerStatusActorComponent; +0x50 = 0x2C160B0 (thunk into .data1) on 3663',s)
    s=re.sub(r'constexpr uintptr_t kStatusLoad = 0x[0-9A-Fa-f]+;','constexpr uintptr_t kStatusLoad = 0x2C160B0;',s)
    return s
rw(DST+'/mod/session2/identity_fix.cpp',fix_identity)
# ---- 4. field share: FieldMover::EnterField was restructured (loops over FM+0x68 field list); same role,
#         same (FM, res*, field) args, result u32* checked != 0 by the caller 0x2ECA480 at 0x2ECABD7.
NEWHEAD=', '.join('0x%02X'%b for b in new.b(0x2ECB130,32))
def fix_fieldshare(s):
    s=re.sub(r'constexpr uintptr_t kEnterField = 0x[0-9A-Fa-f]+;','constexpr uintptr_t kEnterField = 0x2ECB130;  // build 3663 (old 0x2BC04F0; restructured, 4th arg gone)',s)
    s=re.sub(r'constexpr uint8_t kEnterFieldHead\[\] = \{[^}]*\};','constexpr uint8_t kEnterFieldHead[] = {%s};'%NEWHEAD,s,flags=re.S)
    return s
rw(DST+'/mod/session2/field_share.cpp',fix_fieldshare)
# ---- 5. the exe's TLS realm byte (0 client / 1 server thread) is +0x1EB on 3663 (ServerRunThread 0x2A0E1B6: mov eax,0x1EB)
import glob
for p in glob.glob(DST+'/mod/session2/*.cpp')+glob.glob(DST+'/mod/session2/*.h'):
    def fn(s):
        s=re.sub(r'\b0x1EC\b(?![0-9A-Fa-f])','0x1EB',s)
        return s
    rw(p,fn)
# ---- 6. jmp thunks (not in pdata, so unmapped by ns_apply): mapped via their .xtls/.data1 targets (xtlsmap.py)
THUNKS={0xA45740:0xB84E80, 0x2808D80:0x2AB0E50, 0x287DA50:0x2B3E530, 0x2822D00:0x2ACDF90, 0x27FBD60:0x2AA1B70}
TBYTES={'kThunkVf53':0xA45740,'kDBytes':0x2808D80,'kLBytes':0x287DA50,'kSBytes':0x2822D00,'kFBytes':0x27FBD60}
def fix_thunks(s):
    for o,n in THUNKS.items():
        s=re.sub(r'\b0x%X\b'%o,'0x%X'%n,s)
    for name,o in TBYTES.items():
        nb=', '.join('0x%02X'%b for b in new.b(THUNKS[o],5))
        s=re.sub(r'(constexpr uint8_t %s\[\] = \{)[^}]*\}'%name,lambda m_: m_.group(1)+nb+'}',s)
    return s
for p in (DST+'/mod/session2/user2_stages.cpp',DST+'/mod/session2/mount_sync.cpp',DST+'/mod/session2/user2_stages_logic.h'): rw(p,fix_thunks)
# ---- 7. the title's Play now reads [WorldSystem+0x88] into rcx and calls a slot helper (0x757F98 in 0x757xxx, old 0x65DC3E)
def fix_title(s):
    nb=', '.join('0x%02X'%b for b in new.b(0x757F98,14))
    s=re.sub(r'inline constexpr uintptr_t kTitlePlayReadsSaveMgr = 0x[0-9A-Fa-f]+;','inline constexpr uintptr_t kTitlePlayReadsSaveMgr = 0x757F98;  // 3663: mov rcx,[exe+0x72451C0]; mov rcx,[rcx+0x88]; call 0x1A19130',s)
    s=re.sub(r'(inline constexpr uint8_t kTitlePlayReadsSaveMgrBytes\[\] = \{)[^}]*\}',lambda m_: m_.group(1)+nb+'}',s,flags=re.S)
    return s
rw(DST+'/mod/session2/offsets_s2.h',fix_title)
# ---- 8. weather: the client chain's first hop is +0x48 (0xAE8E2B: [slot]+0x48 -> +0x38), the scene's weather system
#         is S+0xF70 (0x3E74450), the holder's blocks moved to +0x20/+0x28/+0x30 (0x4143B7D/0x4143BCF/0x4143C1D)
def fix_weather(s):
    s=re.sub(r'inline constexpr uint32_t kClientHop1 = 0x38, kClientHop2 = 0x38;','inline constexpr uint32_t kClientHop1 = 0x48, kClientHop2 = 0x38;  // 3663: 0xAE8E2B',s)
    s=re.sub(r'inline constexpr uint32_t kSceneWeather = 0xF68;','inline constexpr uint32_t kSceneWeather = 0xF70;  // 3663: 0x3E74450',s)
    s=re.sub(r'inline constexpr uint32_t kHolderA = 0x18, kHolderB = 0x20, kHolderC = 0x28;','inline constexpr uint32_t kHolderA = 0x20, kHolderB = 0x28, kHolderC = 0x30;  // 3663: 0x4143B7D..0x4143C1D',s)
    return s
rw(DST+'/mod/session2/weather_sync_logic.h',fix_weather)
# ---- 9. schedule spawn manager fields moved by +0x38 on 3663 (state 0x6A0->0x6D8 at 0xE8AD055/0xE8B3D45/0x2ED02F5,
#         preparing 0x689->0x6C1 and pending 0x68A->0x6C2 at 0x2B3F9DD/0x2B3FA67); the apply-call context of 0x2ED00D0
def fix_mgr(s):
    s=s.replace('kMgr_CellX = 0x680,','kMgr_CellX = 0x6B8,').replace('kMgr_CellZ = 0x684,','kMgr_CellZ = 0x6BC,')
    s=s.replace('kMgr_Preparing = 0x689,','kMgr_Preparing = 0x6C1,').replace('kMgr_PrepPending = 0x68A,','kMgr_PrepPending = 0x6C2,')
    s=s.replace('kMgr_State = 0x6A0;','kMgr_State = 0x6D8;  // 3663: +0x38 on the 2976 layout')
    return s
rw(DST+'/mod/session2/user2_stages_logic.h',fix_mgr)
NB=', '.join('0x%02X'%b for b in new.b(0x2ED02C3,0x39))
def fix_funnels(s):
    s=re.sub(r'constexpr uintptr_t kApplyCallCtx = 0x[0-9A-Fa-f]+;','constexpr uintptr_t kApplyCallCtx = 0x2ED02C3;  // 3663',s)
    s=re.sub(r'(constexpr uint8_t kApplyCallCtxBytes\[\] = \{)[^}]*\}',lambda m_: m_.group(1)+NB+'}',s,flags=re.S)
    s=s.replace('constexpr uint32_t kMgrApplyByte = 0x6A0;','constexpr uint32_t kMgrApplyByte = 0x6D8;  // 3663: 0x2ED02F5')
    return s
rw(DST+'/mod/session2/user2_funnels.cpp',fix_funnels)
# ---- 10. patch lengths at entries whose first instruction is a 5-byte one on 3663
rw(DST+'/mod/session2/user2_stages.cpp',lambda s: s.replace('{0x2AAF800, 7, kNBytes,','{0x2AAF800, 5, kNBytes,'))
rw(DST+'/mod/session2/stage_lookup_logic.h',lambda s: s.replace('kPatchA = 10, kPatchR = 7, kPatchF = 8;','kPatchA = 10, kPatchR = 7, kPatchF = 5;  // 3663: F is mov [rsp+0x70],rdi (5)'))
rw(DST+'/mod/session2/guest_x_veto.cpp',lambda s: s.replace('{Fn::ChangePhasePanelTag, 0x6D25B0, 6, kPhaseTagBytes,','{Fn::ChangePhasePanelTag, 0x6D25B0, 5, kPhaseTagBytes,'))
# ---- 11. client ClientSequencerStageManager (M) layout on 3663 (vf1 0x9C8E870 reset writes, 0x21C5750 map, vf7 0xBDCCA0):
#          +0x108..+0x234 -> +0x30, +0x270..+0x2F0 -> +0x48. Only the client-M constants of the guest modules.
def mshift(v):
    if 0x108<=v<=0x234: return v+0x30
    if 0x270<=v<=0x2F0: return v+0x48
    return v
MFILES={'guest_seq_play.cpp':r'kMgr_\w+','guest_seq_route.cpp':r'kMgr_\w+','guest_stage_diag.cpp':r'kMgr_\w+','guest_scene_diag.cpp':r'kMgr_\w+',
        'guest_reload_wait.cpp':r'kMgr_Ready','guest_sched_bind.cpp':r'kMgr_\w+','p8_probes.cpp':r'kM_\w+','stage_lookup_logic.h':r'kMgr_\w+',
        'guest_seq_route_logic.h':r'kMgr_Map\w+'}
for fn,pat in MFILES.items():
    def f(s,pat=pat):
        def rep(m_):
            v=int(m_.group(2),16); nv=mshift(v)
            return m_.group(1)+('0x%X'%nv)
        return re.sub(r'(\b(?:%s) = )0x([0-9A-Fa-f]+)\b'%pat,rep,s)
    rw(DST+'/mod/session2/'+fn,f)
# kMgr_Ctx 0x08 / kMgr_Actor 0x18 are below the shift; kMgr_Live/NLive 0x30 left as is (not shown to move).
# the stage processor and its data, the interaction functions' context (0x25D0BA0 / 0x25CFA60)
rw(DST+'/mod/session2/guest_x_veto.cpp',lambda s: s.replace('kStage_Data = 0x48, kData_Name = 0x1B8, kCtx_Stage = 0x00, kStage_Proc = 0x58;','kStage_Data = 0x50, kData_Name = 0x1B8, kCtx_Stage = 0x00, kStage_Proc = 0x60;  // 3663'))
# movement controller method byte and the tree-pull timer (kWallUpGateBytes / kPullApplyHead)
rw(DST+'/mod/session2/remote_motion.cpp',lambda s: s.replace('constexpr uint32_t kMcMethod = 0xF0;','constexpr uint32_t kMcMethod = 0x13C;  // 3663'))
rw(DST+'/mod/session2/mount_sync.cpp',lambda s: s.replace('kMcMethod = 0xF0,','kMcMethod = 0x13C,'))
rw(DST+'/mod/session2/rope_probe_logic.h',lambda s: s.replace('inline constexpr uint32_t kMc_Timer = 0x63C;','inline constexpr uint32_t kMc_Timer = 0x6A4;  // 3663'))
# the character controller layer's chart (kChart0Bytes / kRestartLookBytes)
for fn in ('guest_bodyanim_diag.cpp','guest_s3.cpp','p8_probes.cpp'):
    rw(DST+'/mod/session2/'+fn,lambda s: s.replace('kLayer_Chart = 0x110','kLayer_Chart = 0x118'))
# the sequencer's owner key (kOwnerFindBytes), a stage's bindings (kP4Stage), the timeline (guest_seq_present's signatures)
rw(DST+'/mod/session2/observer_scene_copy.cpp',lambda s: s.replace('kSeq_OwnerKey = 0x98,','kSeq_OwnerKey = 0xA0,'))
rw(DST+'/mod/session2/guest_seq_foreign_logic.h',lambda s: s.replace('kStage_Binds = 0x120, kStage_BindCount = 0x128,','kStage_Binds = 0x128, kStage_BindCount = 0x130,'))
def fpres(s):
    s=s.replace('kStage_Proc = 0x58,','kStage_Proc = 0x60,').replace('kStage_Timeline = 0x298;','kStage_Timeline = 0x2E0;')
    s=s.replace('inline constexpr uint32_t kTl_Id = 0x1D8,','inline constexpr uint32_t kTl_Id = 0x1F8,')
    s=s.replace('inline constexpr uint32_t kMgr_InputBlock = 0x230,','inline constexpr uint32_t kMgr_InputBlock = 0x260,')
    return s
rw(DST+'/mod/session2/guest_seq_present_logic.h',fpres)
# ---- 12. GuestXVeto: the processor vtable gained a slot at +0x130 (handlers +8); the dialogue thunks
exec(open('/home/claude/xveto_fix.py').read())
def fxv(s):
    s=fix(s,open('/home/claude/ns/src/mod/session2/guest_x_veto.cpp').read(),report=False)
    for name,o,n in (('SetQuestDialogAICategory',0x204DC80,0x2277DF0),('TimelineEventEnd',0x204C410,0x2276180),('BlockInteraction',0x23698F0,0x25CFF60)):
        s=s.replace('{Fn::%s, 0x%X,'%(name,o),'{Fn::%s, 0x%X,'%(name,n))
    for arrn,a in (('kQuestDlgBytes',0x2277DF0),('kTimelineEndBytes',0x2276180),('kBlockInterBytes',0x25CFF60)):
        s=re.sub(r'(constexpr uint8_t %s\[\] = \{)[^}]*\}'%arrn,lambda mm,a=a: mm.group(1)+', '.join('0x%02X'%b for b in new.b(a,5))+'}',s)
    return s
rw(DST+'/mod/session2/guest_x_veto.cpp',fxv)
# ---- 13. the guest's NPC schedule / sequence routing (fix_route.py)
exec(open('/home/claude/fix_route.py').read())
# ---- 14. host frame slots: the stage lookup's manager slot (0x235EB60 frame map), the 2671 handler (0x2CD7390)
rw(DST+'/mod/session2/stage_lookup_logic.h',lambda s: s.replace('inline constexpr uint32_t kRbpMgrUsed = 0x8E8;','inline constexpr uint32_t kRbpMgrUsed = 0x8F8;  // 3663'))
rw(DST+'/mod/session2/shop_probe.cpp',lambda s: s.replace('constexpr int32_t kC_Requester = 0x70, kC_Npc = 0x78, kC_Interaction = -0x60, kC_Result = -0x58;','constexpr int32_t kC_Requester = 0x80, kC_Npc = 0x88, kC_Interaction = -0x48, kC_Result = -0x80;  // 3663').replace('kSeqComp_Mgr = 0x18, kMgr_State = 0x6A0;','kSeqComp_Mgr = 0x18, kMgr_State = 0x6D8;'))
# ---- 15. GuestSeqPlay (fix_seqplay.py)
exec(open('/home/claude/fix_seqplay.py').read())
# ---- 16. Status: the characterNo pointer is Status+0x90 on 3663 (getter 0x22971E0, old 0x206A890 read +0x70)
rw(DST+'/mod/session2/identity_fix.h',lambda s: s.replace('inline constexpr uint32_t kStatusNumber = 0x70;   // Status -> u64* characterNo (0x206A8A5)','inline constexpr uint32_t kStatusNumber = 0x90;   // Status -> u64* characterNo (3663: 0x22971F5)'))
rw(DST+'/mod/session2/identity_probe.h',lambda s: s.replace('inline constexpr uint32_t kStatusCharacterNo = 0x70; // Status -> u64* characterNo (0x206A8A5)','inline constexpr uint32_t kStatusCharacterNo = 0x90; // Status -> u64* characterNo (3663: 0x22971F5)'))
# ---- 17. TransformSync -> FieldMover is TS+0x508 on 3663 (was +0x4F0; 6 aligned functions agree)
for fn in ('fast_travel.cpp','world_anchor_logic.h','field_share.h','observer_milestone.cpp'):
    rw(DST+'/mod/session2/'+fn,lambda s: re.sub(r'\b0x4F0\b','0x508',s))
rw(DST+'/mod/session2/user2_funnels.cpp',lambda s: s.replace('game::fast_read(ts + 0x500, &field','game::fast_read(ts + 0x518, &field'))
rw(DST+'/mod/session2/server.cpp',lambda s: s.replace('read_ptr(*ts + 0x500)','read_ptr(*ts + 0x518)'))
rw(DST+'/mod/session2/user2_stages_logic.h',lambda s: s.replace('inline constexpr uint32_t kTs_Field = 0x500;','inline constexpr uint32_t kTs_Field = 0x518;'))
# ---- 18. ServerField grew 8 bytes before its user list on 3663 (live dump of the host's field: users ptr +0x2A8,
# count +0x2B0, key +0x2D0 == FM+8, level entry +0x2D8)
def ffield(s):
    for a,b in (('kUsers = 0x2A0;','kUsers = 0x2A8;'),('kUserCount = 0x2A8;','kUserCount = 0x2B0;'),('kUserActors = 0x2B0;','kUserActors = 0x2B8;'),('kKey = 0x2C8;','kKey = 0x2D0;'),('kLevelEntry = 0x2D0;','kLevelEntry = 0x2D8;')):
        assert a in s, a
        s=s.replace(a,b)
    return s
rw(DST+'/mod/session2/field_share.h',ffield)
rw(DST+'/mod/session2/world_anchor_logic.h',lambda s: s.replace('kFieldGen = 0x2C8;','kFieldGen = 0x2D0;'))
for fn in ('host_scene_diag.cpp','observer_stage.cpp','observer_milestone.cpp'):
    rw(DST+'/mod/session2/'+fn,lambda s: s.replace('kField_Key = 0x2C8;','kField_Key = 0x2D0;'))
rw(DST+'/mod/session2/server.cpp',lambda s: s.replace('read<uint32_t>(*field + 0x2A8)','read<uint32_t>(*field + 0x2B0)').replace('hex(read<uint32_t>(*field + 0x2B0)), hex(read<uint32_t>(*field + 0x2C8))','hex(read<uint32_t>(*field + 0x2B8)), hex(read<uint32_t>(*field + 0x2D0))'))
# ---- 19. MoveFieldAck (2364 on 3663) is 21 + 52n: live capture, 73 bytes with one record; the owner is still the u32 +9.
# Without this the owner alias refuses the layout and user 2's client crashes in the host's field (.rsrc 0x93D7EB0).
def fmove(s):
    a='inline constexpr size_t kMoveFieldHead = 17;    // 2174: 17 + 48n'
    assert a in s
    s=s.replace(a,'inline constexpr size_t kMoveFieldHead = 21;    // 3663: 21 + 52n\ninline constexpr size_t kMoveRecord = 52;')
    b='if (opcode == kOpMoveFieldAck) return n >= kMoveFieldHead && (n - kMoveFieldHead) % kRecord == 0;'
    assert b in s
    return s.replace(b,'if (opcode == kOpMoveFieldAck) return n >= kMoveFieldHead && (n - kMoveFieldHead) % kMoveRecord == 0;')
rw(DST+'/mod/session2/travel_probe_logic.h',fmove)
# ---- 20. GuestS3: 3663 has a second unchecked reader of a layer's playing action, 0x258C8C0 (void, rcx = CC; called by
# 0x9770C0 from CharacterControl::Update 0x9729C0): mov rax,[[cc+0x88+8*idx]+0x18] ; mov rax,[rax+0x28] with a null
# action (guest crash 2026-11-14 22:22:49, read of 0x28). Guard: return at once while that action is null.
def fs3w(s):
    a='constexpr uintptr_t kNullRead = '
    assert a in s
    s=s.replace(a,'''constexpr uint8_t kWeightsBytes[] = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x6C,
                                     0x24, 0x10, 0x48, 0x89, 0x74, 0x24, 0x18, 0x48};
constexpr Site kWeights{0x258C8C0, kWeightsBytes, 5, "0x258C8C0 (layer weights, null playing action)"};
'''+a,1)
    b='SafetyHookMid g_h_null, g_h_update, g_h_spawn, g_h_spawn_al, g_h_ack;'
    assert b in s
    s=s.replace(b,'SafetyHookMid g_h_null, g_h_update, g_h_spawn, g_h_spawn_al, g_h_ack, g_h_weights;\nstd::atomic<uint64_t> c_weights_skips{0};',1)
    c='// 0x85FBA0\'s entry: the first Update of each remote actor.'
    assert c in s
    s=s.replace(c,'''// 0x258C8C0's entry (3663): the playing action of the CC's main layer is read unchecked.
void on_weights(safetyhook::Context& ctx) noexcept {
    uintptr_t p = 0, l = 0, cur = 0;
    uint8_t idx = 0;
    if (!fp(ctx.rcx + 0x30, p) || !fp(p + 0x1B, idx)) return;
    if (fp(ctx.rcx + kCc_Layer0 + 8ull * idx, l) && l && fp(l + kLayer_Play, cur) && cur) return;
    if (c_weights_skips.fetch_add(1, std::memory_order_relaxed) < 4) {
        log::warn("GuestS3: 0x258C8C0 skipped for CC 0x{:X} (layer {} has no playing action)", ctx.rcx, idx);
    }
    ctx.rip = reinterpret_cast<uintptr_t>(g_ret);
}

'''+c,1)
    d='                g_null_on = make_mid(kNullConsumer, g_h_null, on_null_consumer);\n            }'
    assert d in s
    s=s.replace(d,d+'\n            log::info("GuestS3: 0x258C8C0 guard {}", make_mid(kWeights, g_h_weights, on_weights) ? "ON" : "OFF");',1)
    return s
rw(DST+'/mod/session2/guest_s3.cpp',fs3w)
# ---- 21. GuestDiag: its install thread waited 2 minutes for signatures that do not match on 3663, so the S34 remote
# update guard (the fix of the crash at 0x9A6391) was never installed before the guest reached the host's field.
def fgad(s):
    a='    for (int i = 0; i < 24000; ++i) {\n        const bool diag = !g_diag ||'
    assert a in s
    return s.replace(a,'    for (int i = 0; i < 1500; ++i) {\n        const bool diag = !g_diag ||',1)
rw(DST+'/mod/session2/guest_actor_diag.cpp',fgad)
# ---- 22. Audit 2026-11-15 (audit/audit_offsets.py + audit2.py: each constant's cited 2976 instruction aligned to 3663).
def rep(path, pairs):
    def f(s):
        for a,b in pairs:
            assert a in s, (path, a)
            s=s.replace(a,b)
        return s
    rw(DST+'/mod/session2/'+path,f)
# The field-change object P of the client's loading state machine (0x7797F0 -> 0x8825B0) moved +0xF0 and the
# function keeps it in r13 (was rdi); its states from 0xB on are numbered one higher (idle 0x18 -> 0x19).
rep('guest_actor_diag.cpp',[('constexpr uint32_t kP_State = 0x15C;','constexpr uint32_t kP_State = 0x24C;'),
    ('constexpr uint32_t kP_Manager = 0xB8;','constexpr uint32_t kP_Manager = 0x1A8;'),
    ('    if (state == 0x12) {','    if (state == 0x13) {'),
    ('expected != ctx.rdi) {','expected != ctx.r13) {'),
    ('if (!fast_peek(ctx.rdi + kP_State, state)) return;','if (!fast_peek(ctx.r13 + kP_State, state)) return;'),
    ('guest_reload_wait_on_pass(ctx.rdi, state, old);','guest_reload_wait_on_pass(ctx.r13, state, old);'),
    ('    e.v[0] = ctx.rdi;\n    e.v[1] = static_cast<uint64_t>(static_cast<int64_t>(old));','    e.v[0] = ctx.r13;\n    e.v[1] = static_cast<uint64_t>(static_cast<int64_t>(old));')])
rep('guest_reload_wait.cpp',[('constexpr uint32_t kP_State = 0x15C, kP_Flags = 0x160, kP_290 = 0x290, kP_Manager = 0xB8;','constexpr uint32_t kP_State = 0x24C, kP_Flags = 0x24F, kP_290 = 0x290, kP_Manager = 0x1A8;')])
rep('guest_reload_wait_logic.h',[('inline constexpr int kStateIdle = 0x18;','inline constexpr int kStateIdle = 0x19;'),
    ('inline constexpr int kStateWait = 0x12;','inline constexpr int kStateWait = 0x13;'),
    ('inline constexpr int kStateOwn2322 = 0x11;','inline constexpr int kStateOwn2322 = 0x12;')])
rep('guest_x_veto_logic.h',[('constexpr uint8_t kStateInWorld = 0x18;','constexpr uint8_t kStateInWorld = 0x19;')])
rep('guest_stage_diag.cpp',[('kP_Q = 0xC0, kP_State = 0x15C;','kP_Q = 0x1B0, kP_State = 0x24C;')])
rep('p8_probes.cpp',[('constexpr uint32_t kP_State = 0x15C, kP_Flag162 = 0x162, kP_Flag163 = 0x163, kP_Pos = 0x150;','constexpr uint32_t kP_State = 0x24C, kP_Flag162 = 0x251, kP_Flag163 = 0x252, kP_Pos = 0x240;')])
rep('guest.cpp',[('game::safe_read(p + 0xC0, &q, sizeof q);','game::safe_read(p + 0x1B0, &q, sizeof q);'),
    ('game::safe_read(p + 0x15C, &state, sizeof state);\n        log::error','game::safe_read(p + 0x24C, &state, sizeof state);\n        log::error'),
    ('                        game::safe_read(p + 0x15C, &state, sizeof state);','                        game::safe_read(p + 0x24C, &state, sizeof state);')])
# Other structures (each from aligned instructions):
rep('guest_seq_foreign_logic.h',[('kStage_B60 = 0x60, kStage_B61 = 0x61,','kStage_B60 = 0x68, kStage_B61 = 0x69,')])
rep('guest_seq_present_logic.h',[('inline constexpr uint32_t kTl_Id = 0x1F8, kTl_Listed = 0x1D1, kTl_Playing = 0xDC;','inline constexpr uint32_t kTl_Id = 0x1F8, kTl_Listed = 0x1F1, kTl_Playing = 0xFC;'),
    ('kNode_Parent = 0x98, kCtx_Flags = 0x5C,','kNode_Parent = 0xB8, kCtx_Flags = 0x64,')])
rep('guest_seq_route.cpp',[('constexpr uint32_t kInst_Stage = 0x58,','constexpr uint32_t kInst_Stage = 0x60,')])
rep('guest_seq_route_logic.h',[('kInfo_NameOff = 0x1B8, kInst_StageOff = 0x58;','kInfo_NameOff = 0x1B8, kInst_StageOff = 0x60;')])
rep('mount_sync_logic.h',[('inline constexpr uint32_t kMcEmptyFlag = 0x3D0;','inline constexpr uint32_t kMcEmptyFlag = 0x430;')])
rep('remote_rope_logic.h',[('inline constexpr uint32_t kPhysics_vfTransform = 0x3B0;','inline constexpr uint32_t kPhysics_vfTransform = 0x3B8;')])
rep('tree_bend_logic.h',[('inline constexpr uint32_t kControl_F6 = 0xF6;','inline constexpr uint32_t kControl_F6 = 0xFA;'),
    ('inline constexpr uint32_t kState_Amount = 0x378;','inline constexpr uint32_t kState_Amount = 0x3C0;'),
    ('inline constexpr uint32_t kState_Dir = 0x380;','inline constexpr uint32_t kState_Dir = 0x3C8;')])
rep('user2_road_logic.h',[('kSpawn_Radius = 0x468;','kSpawn_Radius = 0x498;')])
# ---- 23. The layer's chart is +0x118 on 3663 (guest_seq_restore's kChart0Bytes: mov rax,[rcx+0x118] at 0x9779CD),
# but game/offsets.h (horse_diag's remote update guard, the puppet layer guards) still read +0x110: the guard that
# fixes the crash at 0x9A6391 never saw a layer "without action". And the guest's crashes of 2026-11-14 22:22 / 22:34
# had a remote character whose MAIN layer was empty (no chart either): contained too (layer 0 only; layer 1 keeps the
# chart condition, it is often legitimately idle).
rw(DST+'/mod/game/offsets.h',lambda s: s.replace('inline constexpr uint32_t kLayer_Chart = 0x110;','inline constexpr uint32_t kLayer_Chart = 0x118;  // 3663 (0x9779CD)'))
def fhd(s):
    a='*reinterpret_cast<const uintptr_t*>(l + 0x110)};'
    assert a in s
    s=s.replace(a,'*reinterpret_cast<const uintptr_t*>(l + off::kLayer_Chart)};')
    b='''        if (l0) {
            lacks0 = gad::layer_lacks_action(*reinterpret_cast<const uintptr_t*>(l0 + off::kLayer_Chart),
                                             *reinterpret_cast<const uintptr_t*>(l0 + 0x10),
                                             *reinterpret_cast<const uintptr_t*>(l0 + off::kLayer_Playback));
        }'''
    assert b in s
    s=s.replace(b,'''        if (l0) {
            // 3663: the main layer is read unchecked by 0x258C8C0 / 0x9A6391 even with no chart.
            lacks0 = *reinterpret_cast<const uintptr_t*>(l0 + 0x10) == 0 ||
                     *reinterpret_cast<const uintptr_t*>(l0 + off::kLayer_Playback) == 0;
        }''')
    return s
rw(DST+'/mod/net/horse_diag.cpp',fhd)
# ---- 24. The guest's crash of 2026-11-14 23:55 (exe+0x8250A6, a worker thread): an actor the remote update guard
# held (main layer: chart, no action) is still processed by its component job 0x97D6D0 -> 0x821620 -> 0x824ED0, which
# reads the active layer's action unchecked. 0x821620(rcx = X, [X] = the CC) returns nothing its caller uses
# (0x97DA7A): with the guard, it returns at once for the same actors.
def fjob(s):
    a='SafetyHookMid g_cc_hook, g_post_hook, g_restore_hook, g_pending_hook;'
    assert a in s
    s=s.replace(a,a+'''
constexpr Site kCompJob = {0x821620, {0x48, 0x89, 0x5C, 0x24, 0x10, 0x48, 0x89, 0x6C, 0x24, 0x18, 0x48, 0x89,
                                      0x74, 0x24, 0x20, 0x57}, "component job"};
SafetyHookMid g_job_hook;''',1)
    b='std::atomic<uint64_t> g_remote_skipped{0}, g_remote_skipped_post{0};'
    assert b in s
    s=s.replace(b,'std::atomic<uint64_t> g_remote_skipped{0}, g_remote_skipped_post{0}, g_remote_skipped_job{0};',1)
    c='void on_cc_post(safetyhook::Context& ctx) {'
    assert c in s
    s=s.replace(c,'''// 3663: the component job 0x821620 (rcx = X, [X] = the CC), same actors as the update guard.
void on_comp_job(safetyhook::Context& ctx) {
    if (!g_remote_mode) return;
    uintptr_t cc = 0;
    if (!game::safe_read(ctx.rcx, &cc, sizeof cc) || !cc) return;
    if (contain_remote(cc, g_remote_skipped_job)) ctx.rip = reinterpret_cast<uintptr_t>(g_ret);
}

'''+c,1)
    d='''    if (!hook(kCcPost, g_post_hook, on_cc_post)) {
        g_cc_hook = {};
        g_remote_mode = false;
        return false;
    }'''
    assert d in s
    s=s.replace(d,d+'''
    if (!hook(kCompJob, g_job_hook, on_comp_job)) log::warn("RemoteUpdateGuard: the component job 0x821620 is NOT guarded");''',1)
    e='''                       g_remote_skipped_post.load(std::memory_order_relaxed), g_remote_keys.size(),'''
    assert e in s
    return s
rw(DST+'/mod/net/horse_diag.cpp',fjob)
def fdesc(s):
    a='return std::format(" | remote update guard: update skipped {} post-update {} | actors held {}{} (last 0x{:08X})",'
    assert a in s, 'desc'
    s=s.replace(a,'return std::format(" | remote update guard: update skipped {} post-update {} job {} | actors held {}{} (last 0x{:08X})",',1)
    s=s.replace('''                       g_remote_skipped_post.load(std::memory_order_relaxed), g_remote_keys.size(),''','''                       g_remote_skipped_post.load(std::memory_order_relaxed),
                       g_remote_skipped_job.load(std::memory_order_relaxed), g_remote_keys.size(),''',1)
    return s
rw(DST+'/mod/net/horse_diag.cpp',fdesc)
# ---- 25. Experiment 2026-11-15: the v5 copy of the other player ([coop] enabled=1) together with option D, because
# the other user's real character is hidden on both clients (Status+0x3B8 bit 20). Damage/horse stay off by ini.
def fboth(s):
    a='        bool session2 = in_game && cfg.s2_enabled && !cfg.coop_enabled;\n        if (in_game && cfg.s2_enabled && cfg.coop_enabled) {\n            log::warn("Session2 (option D) needs [coop] enabled=0; staying off");\n        }'
    assert a in s
    s=s.replace(a,'        bool session2 = in_game && cfg.s2_enabled;\n        if (in_game && cfg.s2_enabled && cfg.coop_enabled) {\n            log::warn("Session2 (option D) WITH the copies model ([coop] enabled=1): experiment");\n        }')
    b='const bool p8_guest = in_game && cfg.s2_enabled && !cfg.coop_enabled && cfg.s2_guest;'
    assert b in s
    return s.replace(b,'const bool p8_guest = in_game && cfg.s2_enabled && cfg.s2_guest;')
rw(DST+'/mod/mod.cpp',fboth)
def fhd2(s):
    a='''    if (g_remote_mode) {
        if (contain_remote(ctx.rcx, g_remote_skipped)) ctx.rip = reinterpret_cast<uintptr_t>(g_ret);
        return;
    }'''
    assert a in s
    s=s.replace(a,'''    if (g_remote_mode) {
        if (contain_remote(ctx.rcx, g_remote_skipped)) {
            ctx.rip = reinterpret_cast<uintptr_t>(g_ret);
            return;
        }
        if (!g_coop_too) return;
    }''')
    b='''    if (g_remote_mode) {
        if (contain_remote(ctx.rcx, g_remote_skipped_post)) ctx.rip = reinterpret_cast<uintptr_t>(g_ret);
        return;
    }'''
    assert b in s
    s=s.replace(b,'''    if (g_remote_mode) {
        if (contain_remote(ctx.rcx, g_remote_skipped_post)) {
            ctx.rip = reinterpret_cast<uintptr_t>(g_ret);
            return;
        }
        if (!g_coop_too) return;
    }''')
    c='SafetyHookMid g_job_hook;'
    assert c in s
    s=s.replace(c,c+'\nbool g_coop_too = false;  // the co-op install owns the hooks and the remote guard shares them',1)
    d='''bool horse_diag_install_remote() {
    if (g_cc_hook) return false;  // the co-op's / host's install owns the two hooks'''
    assert d in s
    s=s.replace(d,'''bool horse_diag_install_remote() {
    if (g_cc_hook) {  // the co-op's install owns the hooks: the remote guard runs first in them
        if (!g_post_hook && !hook(kCcPost, g_post_hook, on_cc_post)) return false;
        g_coop_too = true;
        g_remote_mode = true;
        if (!g_job_hook && !hook(kCompJob, g_job_hook, on_comp_job)) log::warn("RemoteUpdateGuard: the component job 0x821620 is NOT guarded");
        return true;
    }''')
    return s
rw(DST+'/mod/net/horse_diag.cpp',fhd2)
# ---- 26. The remote guard held 784 actors (every host NPC: layer 1 has a chart and no action, normal on 3663) and
# froze them on the guest. Only the ACTIVE layer ([[cc+0x30]+0x1B], the one 0x258C8C0 / 0x8250A6 read) counts.
def fact(s):
    a='''        const auto l1 = *reinterpret_cast<const uintptr_t*>(cc + off::kControl_Layer0 + 8);
        if (l1 && !lacks0) {'''
    assert a in s
    s=s.replace(a,'''        const auto sel = *reinterpret_cast<const uintptr_t*>(cc + 0x30);
        const uint8_t active = sel ? *reinterpret_cast<const uint8_t*>(sel + 0x1B) : 0;
        const auto l1 = *reinterpret_cast<const uintptr_t*>(cc + off::kControl_Layer0 + 8);
        if (active == 1 && l1) {
            lacks0 = *reinterpret_cast<const uintptr_t*>(l1 + 0x10) == 0 ||
                     *reinterpret_cast<const uintptr_t*>(l1 + off::kLayer_Playback) == 0;
        }
        if (false) {''')
    return s
rw(DST+'/mod/net/horse_diag.cpp',fact)
# ---- 27. Guest crash near an NPC (exe+0x9976BB, 2026-11-15 00:15 and 00:22): 0x994CD0(rcx = MC = [CC+0xC8], result
# unused by 0xB3F890 / 0xB3FB50) -> 0x997480 reads the active layer's action unchecked. Same guard; actor = [MC+0x68].
def fmc(s):
    a='SafetyHookMid g_job_hook;'
    assert a in s
    s=s.replace(a,a+'''
constexpr Site kMcJob = {0x994CD0, {0x4C, 0x8B, 0xDC, 0x49, 0x89, 0x5B, 0x18, 0x49, 0x89, 0x73, 0x20, 0x57,
                                    0x41, 0x54, 0x41, 0x55}, "movement job"};
SafetyHookMid g_mc_hook;
const uintptr_t g_exe_base0 = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));''',1)
    b='void on_cc_post(safetyhook::Context& ctx) {'
    s=s.replace(b,'''void on_mc_job(safetyhook::Context& ctx) {
    if (!g_remote_mode) return;
    uintptr_t actor = 0, comps = 0, cc = 0;
    if (!game::safe_read(ctx.rcx + 0x68, &actor, sizeof actor) || !actor ||
        !game::safe_read(actor + 0x68, &comps, sizeof comps) || !comps ||
        !game::safe_read(comps + 0x40, &cc, sizeof cc) || !cc) return;
    if (contain_remote(cc, g_remote_skipped_job)) ctx.rip = reinterpret_cast<uintptr_t>(g_ret);
}

'''+b,1)
    c='if (!hook(kCompJob, g_job_hook, on_comp_job)) log::warn("RemoteUpdateGuard: the component job 0x821620 is NOT guarded");'
    assert c in s
    s=s.replace(c,c+'\n    if (!g_mc_hook && !hook(kMcJob, g_mc_hook, on_mc_job)) log::warn("RemoteUpdateGuard: the movement job 0x994CD0 is NOT guarded");')
    return s
rw(DST+'/mod/net/horse_diag.cpp',fmc)
# ---- 28. PAUTA 1: the other user's character is hidden on both clients: Status flag 0x100000 (Status+0x3B8), set by
# the game's flag setter 0x1932980(Status, mask, r8b set, xmm3 fade, [rsp+0x20]) from 0x238FBB8 / 0x2285348. Hook the
# setter: a SET of 0x100000 on a player character (key class A) another user controls becomes a CLEAR.
def funh(s):
    a='void on_cc_post(safetyhook::Context& ctx) {'
    assert a in s
    s=s.replace(a,'''constexpr Site kFlagSet = {0x1932980, {0x48, 0x8B, 0xC4, 0x48, 0x89, 0x58, 0x10, 0x48, 0x89, 0x68, 0x18, 0x48,
                                      0x89, 0x70, 0x20, 0x57}, "status flag setter"};
SafetyHookMid g_flag_hook;
std::atomic<uint64_t> g_unhidden{0};
const uintptr_t g_exe_base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
void on_flag_set(safetyhook::Context& ctx) {
    if (ctx.rdx != 0x100000 || (ctx.r8 & 0xFF) == 0) return;
    __try {
        const auto actor = *reinterpret_cast<const uintptr_t*>(ctx.rcx + 8);
        if (!actor) return;
        const uint32_t key = *reinterpret_cast<const uint32_t*>(actor + off::kActor_Key);
        const uint32_t ctrl = *reinterpret_cast<const uint32_t*>(actor + off::kActor_OwnerUser);
        if ((key >> 28) != 0xA || !ctrl) return;
        const auto ws = *reinterpret_cast<const uintptr_t*>(g_exe_base + off::kWorldSystemSlot);
        const auto mgr = ws ? *reinterpret_cast<const uintptr_t*>(ws + off::kWorldSystem_ActorManager) : 0;
        const auto mine = mgr ? *reinterpret_cast<const uintptr_t*>(mgr + off::kActorManager_ControlledActor) : 0;
        if (!mine || mine == actor) return;
        const uint32_t local = *reinterpret_cast<const uint32_t*>(mine + off::kActor_OwnerUser);
        if (!local || ctrl == local) return;
        ctx.r8 &= ~0xFFull;  // clear instead of set
        g_unhidden.fetch_add(1, std::memory_order_relaxed);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
    }
}

'''+a,1)
    b='void horse_diag_contain_remote(uint32_t local_user_key) {'
    assert b in s
    s=s.replace(b,'''void unhide_install() {
    CloseHandle(CreateThread(nullptr, 0, [](LPVOID) -> DWORD {
        for (int i = 0; i < 240; ++i) {
            if (hook(kFlagSet, g_flag_hook, on_flag_set)) {
                log::info("Unhide ON: the other user's character is never hidden by Status flag 0x100000 (exe+0x1932980)");
                for (;;) {
                    Sleep(30000);
                    log::info("Unhide: {} hide(s) of the other user's character turned into a clear", g_unhidden.load());
                }
            }
            Sleep(1000);
        }
        log::error("Unhide: the status flag setter exe+0x1932980 could NOT be hooked");
        return 0;
    }, nullptr, 0, nullptr));
}

'''+b,1)
    return s
rw(DST+'/mod/net/horse_diag.cpp',funh)
rw(DST+'/mod/net/horse_diag.h',lambda s: s.replace('bool horse_diag_install_remote();','bool horse_diag_install_remote();\n// 3663: the other user\'s character stays visible (Status flag 0x100000). Host and guest.\nvoid unhide_install();',1))
def fmi(s):
    a='    if (init->session2) {\n        session2::install(init->session2_opt);'
    assert a in s
    return s.replace(a,'    if (init->session2) net::unhide_install();\n'+a,1)
rw(DST+'/mod/mod.cpp',fmi)
# ---- 29. The guest's PC turns `session2` off (host side only): the unhide hook goes in there directly.
def fgu(s):
    a='            session2 = false;  // nothing of the host\'s side on the guest\'s PC'
    assert a in s
    return s.replace(a,'            net::unhide_install();  // 3663: the host\'s character stays visible here\n'+a,1)
rw(DST+'/mod/mod.cpp',fgu)
# ---- 30. The HOST crashed too at 0x8250A6 (2026-11-15 01:29, user 2's actors on the host's client): the remote guard
# runs on the host as well, with the host's own user key read from its controlled actor.
def fhost(s):
    a='void unhide_install() {'
    assert a in s
    s=s.replace(a,'''uint32_t controlled_user_key() {
    __try {
        const auto ws = *reinterpret_cast<const uintptr_t*>(g_exe_base + off::kWorldSystemSlot);
        const auto mgr = ws ? *reinterpret_cast<const uintptr_t*>(ws + off::kWorldSystem_ActorManager) : 0;
        const auto mine = mgr ? *reinterpret_cast<const uintptr_t*>(mgr + off::kActorManager_ControlledActor) : 0;
        return mine ? *reinterpret_cast<const uint32_t*>(mine + off::kActor_OwnerUser) : 0;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return 0;
    }
}

void host_guard_install() {
    CloseHandle(CreateThread(nullptr, 0, [](LPVOID) -> DWORD {
        uint32_t key = 0;
        for (int i = 0; i < 600 && !key; ++i) {
            key = controlled_user_key();
            if ((key >> 28) != 0x9) key = 0;
            if (!key) Sleep(1000);
        }
        if (!key) return 0;
        if (!horse_diag_install_remote()) {
            log::error("RemoteUpdateGuard (host): could NOT be installed");
            return 0;
        }
        horse_diag_contain_remote(key);
        log::info("RemoteUpdateGuard ON (host): actors of other users without an action on their active layer are held "
                  "(local user 0x{:08X})", key);
        for (;;) {
            Sleep(1000);
            const uint32_t k = controlled_user_key();
            if ((k >> 28) == 0x9 && k != key) {
                key = k;
                horse_diag_contain_remote(key);
            }
        }
    }, nullptr, 0, nullptr));
}

'''+a,1)
    return s
rw(DST+'/mod/net/horse_diag.cpp',fhost)
rw(DST+'/mod/net/horse_diag.h',lambda s: s.replace('void unhide_install();','void unhide_install();\n// The remote update guard on the host\'s PC too (user 2\'s actors on the host\'s client).\nvoid host_guard_install();',1))
def fmh(s):
    a='    if (init->session2) net::unhide_install();\n'
    assert a in s
    return s.replace(a,'    if (init->session2) {\n        net::unhide_install();\n        net::host_guard_install();\n    }\n',1)
rw(DST+'/mod/mod.cpp',fmh)
# ---- 31. Since r25 the guest's load stops at 0x12 (waits for its own 2322): the movement job guard (0x994CD0) held
# actors the field load waits for. It only acts once the client is in the world ([[WorldSystem]+0x60]+0x24C == 0x19).
def fmcg(s):
    a='void on_mc_job(safetyhook::Context& ctx) {\n    if (!g_remote_mode) return;'
    assert a in s
    return s.replace(a,'''void on_mc_job(safetyhook::Context& ctx) {
    if (!g_remote_mode) return;
    {
        uintptr_t ws = 0, p = 0;
        uint8_t st = 0;
        if (!game::safe_read(g_exe_base0 + off::kWorldSystemSlot, &ws, sizeof ws) || !ws ||
            !game::safe_read(ws + 0x60, &p, sizeof p) || !p || !game::safe_read(p + 0x24C, &st, sizeof st) || st != 0x19)
            return;
    }''',1)
rw(DST+'/mod/net/horse_diag.cpp',fmcg)
# ---- 32. Since the unhide hook (r26) the guest's load stops at 0x12 (own 2322 never comes): the setter is shared by the
# host's in-process SERVER, whose user-2 character the hook also cleared. Only CLIENT actors (found in the client's
# actor manager under the same key) are touched.
def fcli(s):
    a='        if (!local || ctrl == local) return;\n        ctx.r8 &= ~0xFFull;  // clear instead of set'
    assert a in s
    s=s.replace(a,'''        if (!local || ctrl == local) return;
        const auto client = game::find_actor(key);
        if (!client || *client != actor) return;  // the in-process server's actor: left alone
        ctx.r8 &= ~0xFFull;  // clear instead of set''',1)
    if '#include "mod/game/actor_lookup.h"' not in s:
        s=s.replace('#include "mod/game/offsets.h"','#include "mod/game/actor_lookup.h"\n#include "mod/game/offsets.h"',1)
    return s
rw(DST+'/mod/net/horse_diag.cpp',fcli)
# ---- 33. The guest's load waits in 0x12 for its own M+0x261 >= 2, written only by a PlayAllPreLoadSequencerAck naming
# its own character; the host's server sends it to the field owner's character only (seen hanging from 01:44 on,
# 3+ minutes). After 8 s there with own M+0x261 == 1, write the 2 that handler writes (once per stuck state).
def fown(s):
    a='    if (state == grw::kStateWait) on_wait(p, now);\n    g_busy.clear(std::memory_order_release);\n}'
    assert a in s
    s=s.replace(a,'''    if (state == grw::kStateWait) on_wait(p, now);
    {
        static uint64_t since = 0;
        static bool done = false;
        if (state != grw::kStateOwn2322) {
            since = 0;
            done = false;
        } else if (!since) {
            since = now;
        } else if (!done && now - since >= 8'000'000) {
            done = true;
            const uintptr_t a = own_actor();
            const uintptr_t comps = a ? rdv<uintptr_t>(a + kActor_Comps) : 0;
            const uintptr_t seq = comps ? rdv<uintptr_t>(comps + kComps_Seq) : 0;
            const uintptr_t m = seq ? rdv<uintptr_t>(seq + kSeqComp_Mgr) : 0;
            uint8_t v = 0xFF;
            if (m && rdv<uintptr_t>(m) == g_base + kVtStageManager && rd(m + kMgr_Ready, v) && v == 1) {
                *reinterpret_cast<volatile uint8_t*>(m + kMgr_Ready) = 2;
                log::warn("GuestReloadWait: 8 s in the own-2322 state with own M+0x261 = 1: set to 2 (the missing "
                          "PlayAllPreLoadSequencerAck for this client's character); the load goes on");
            }
        }
    }
    g_busy.clear(std::memory_order_release);
}''',1)
    return s
rw(DST+'/mod/session2/guest_reload_wait.cpp',fown)
# ---- 34. C4: a guest that left before user 2 ever existed (its 2001 handed, no snapshot) quarantined the host for the
# rest of the run ("b sam FAIL: no snapshot"), so no later guest could log in (Legion test 02:27). When no user 2 ever
# existed in the epoch, there is nothing of it in the SAM: check b passes.
def fbh(s):
    a='    bool expect_handed = true;'
    assert a in s
    return s.replace(a,'    bool user_never_existed = false;  // no user 2 was ever seen in the epoch (b: nothing to check)\n'+a,1)
rw(DST+'/mod/session2/guest_lifecycle.h',fbh)
def fbc(s):
    a='''    if (!snap.any()) {
        out.sam = {false, "no snapshot of user 2 was taken: the SAM cannot be checked"};'''
    assert a in s
    return s.replace(a,'''    if (!snap.any() && r.user_never_existed) {
        out.sam = {true, "no user 2 ever existed in this epoch: nothing of it in the SAM"};
    } else if (!snap.any()) {
        out.sam = {false, "no snapshot of user 2 was taken: the SAM cannot be checked"};''',1)
rw(DST+'/mod/session2/guest_lifecycle.cpp',fbc)
def fbs(s):
    a='    r.expect_handed = g_lc.handed();\n'
    assert a in s
    return s.replace(a,a+'    r.user_never_existed = !g_user_seen && !server_user2_present();\n',1)
rw(DST+'/mod/session2/session2.cpp',fbs)
# ---- 35. kSamSlot was never ported (it is read only from protected code in 2976, so the matcher left it): the
# 2976 value 0x6DA67D8 made every C4 SAM reading fail on 3663 (no snapshot of user 2 -> quarantine at the first
# guest logout, no second login in the run). 3663: [exe+0x7283378] = ServerActorManager (vtable exe+0x5F69100, found
# in the live host 2026-11-15; map +0x98/+0x9C/+0xA8/+0xB0 as SAM::FindActor 0x2D78E40, users +0xB8/+0xC0).
def fsam(s):
    a='inline constexpr uintptr_t kSamSlot = 0x6DA67D8;'
    assert a in s
    return s.replace(a,'inline constexpr uintptr_t kSamSlot = 0x7283378;',1)
rw(DST+'/mod/session2/offsets_s2.h',fsam)
# ---- 36. kLiveGateByte also unported (2976 0x6CC0218): 3663's gate before 0x9CF8DF is
# 0x9CF8D6 cmp byte [rip+0x67C630B],bpl -> exe+0x7195BE8.
def fgate(s):
    a='constexpr uintptr_t kLiveGateByte = 0x6CC0218;'
    assert a in s
    return s.replace(a,'constexpr uintptr_t kLiveGateByte = 0x7195BE8;',1)
rw(DST+'/mod/session2/guest_seq_play.cpp',fgate)
# ---- 37. Reload refresh (2026-11-15, Legion test): after user 2's fast travel that RELOADS the shared field
# (TravelAck verdict "reload in the same field (2643/3066)"), the guest's client drops the field's actors but the
# server still counts them as given to user 2 and announces nothing again: the host's NPCs vanish on the guest
# (host client 269 actors, guest 75). The S16 answer (observer_milestone_reload) is REFUSED on 3663. Here, 1.5 s
# after that verdict, on the game's own NotifyAppear thread, everything user 2's session was given (g_known) and
# still lives in the SAM is announced to it again, parents first (a key the guest still has fails harmlessly).
def fks(s):
    a='    size_t size() const { return n_; }\n    bool overflow() const { return overflow_; }\n'
    assert a in s
    return s.replace(a,a+'    template <class F>\n    void for_each(F&& f) const {\n        for (const uint32_t k : slot_) if (k) f(k);\n    }\n',1)
rw(DST+'/mod/session2/family_order_logic.h',fks)
def fchh(s):
    a='Reannounce child_settle_reannounce(uint32_t key, uint32_t delay_ms, bool dry_run = false);'
    assert a in s
    return s.replace(a,a+'\n// Reload refresh: everything user 2 was given is announced to it again delay_ms from now (on the next native\n// NotifyAppear thread). Called after its reload in the same field.\nvoid child_hold_request_refresh(uint64_t delay_ms) noexcept;',1)
rw(DST+'/mod/session2/child_hold.h',fchh)
def fchc(s):
    a='void __fastcall notify_detour(uintptr_t flag, const GameVec* sessions, const GameVec* keys) noexcept {'
    assert s.count(a)==1
    s=s.replace(a,r'''std::atomic<uint64_t> g_refresh_due_ms{0}, g_refresh_runs{0};
void reload_refresh_pass() noexcept {
    uint64_t due = g_refresh_due_ms.load(std::memory_order_relaxed);
    if (!due || now_ms() < due) return;
    if (!server_realm()) return;
    if (!g_refresh_due_ms.compare_exchange_strong(due, 0)) return;
    try {
        const uintptr_t session = server_live_session2();
        if (!session) {
            log::warn("Session2 reload refresh: no live session 2, nothing re-announced");
            return;
        }
        std::vector<uint32_t> known;
        bool overflow = false;
        {
            std::lock_guard lock(g_mu);
            known_sync_locked(session, server_epoch());
            overflow = g_known.overflow();
            known.reserve(g_known.size());
            g_known.for_each([&](uint32_t k) { known.push_back(k); });
        }
        const auto sam = sam_now(false);
        if (!sam) {
            log::warn("Session2 reload refresh: the SAM is unreadable, nothing re-announced ({} known)", known.size());
            return;
        }
        struct K { uint32_t key, parent; uint8_t depth; };
        std::vector<K> live;
        live.reserve(known.size());
        size_t gone = 0;
        for (const uint32_t k : known) {
            if (observer_hold_acks_retired(k)) { ++gone; continue; }
            const auto actor = sam_find_actor(*sam, k);
            if (!actor || !*actor) { ++gone; continue; }
            live.push_back({k, child_hold_actor_parent(*actor).value_or(0), 0});
        }
        std::sort(live.begin(), live.end(), [](const K& a, const K& b) { return a.key < b.key; });
        for (int round = 0; round < 8; ++round) {
            bool changed = false;
            for (K& x : live) {
                if (!x.parent) continue;
                const auto it = std::lower_bound(live.begin(), live.end(), x.parent,
                                                 [](const K& a, uint32_t v) { return a.key < v; });
                if (it == live.end() || it->key != x.parent) continue;
                const uint8_t d = static_cast<uint8_t>(std::min<int>(it->depth + 1, 64));
                if (d > x.depth) { x.depth = d; changed = true; }
            }
            if (!changed) break;
        }
        std::stable_sort(live.begin(), live.end(), [](const K& a, const K& b) { return a.depth < b.depth; });
        std::vector<uint32_t> keys;
        keys.reserve(live.size());
        for (const K& x : live) keys.push_back(x.key);
        constexpr size_t kChunk = 256;
        size_t calls = 0;
        for (size_t i = 0; i < keys.size(); i += kChunk) {
            const size_t n = std::min(kChunk, keys.size() - i);
            const GameVec sv{&session, 1, 1};
            const GameVec kv{keys.data() + i, static_cast<uint32_t>(n), static_cast<uint32_t>(n)};
            call_notify(0, &sv, &kv);
            ++calls;
        }
        g_refresh_runs.fetch_add(1);
        log::info("Session2 reload refresh #{}: {} key(s) announced again to user 2 in {} NotifyAppear call(s) | known {}{}, "
                  "gone or retired {}",
                  g_refresh_runs.load(), keys.size(), calls, known.size(), overflow ? " (OVERFLOWED: partial)" : "",
                  gone);
    } catch (...) {
        g_errors.fetch_add(1, std::memory_order_relaxed);
    }
}

'''+a,1)
    b='''    // observer_hold (M5): the game's own NotifyAppear thread is where a
    // release is legitimate;'''
    assert s.count(b)==1
    s=s.replace(b,'    reload_refresh_pass();  // reload refresh (section 37)\n'+b,1)
    c='Reannounce child_settle_reannounce(uint32_t key, uint32_t delay_ms, bool dry_run) {'
    assert s.count(c)==1
    s=s.replace(c,'''void child_hold_request_refresh(uint64_t delay_ms) noexcept {
    g_refresh_due_ms.store(now_ms() + delay_ms);
    log::info("Session2 reload refresh: requested, in {} ms (user 2 reloaded the shared field)", delay_ms);
}

'''+c,1)
    if '#include <vector>' not in s:
        s=s.replace('#include <mutex>\n','#include <mutex>\n#include <vector>\n',1)
    return s
rw(DST+'/mod/session2/child_hold.cpp',fchc)
def ftp(s):
    a='''            const tpl::Reply r = tpl::classify_reply(c);
            const tpl::HostBurstView hb'''
    assert a in s
    s=s.replace(a,'''            const tpl::Reply r = tpl::classify_reply(c);
            if (r == tpl::Reply::Reload2643) child_hold_request_refresh(1500);  // section 37
            const tpl::HostBurstView hb''',1)
    return s.replace('#include "mod/log.h"\n','#include "mod/log.h"\n#include "mod/session2/child_hold.h"\n',1)
rw(DST+'/mod/session2/travel_probe.cpp',ftp)
# ---- 38. TS -> current sector moved: 2976 [TS+0x540], 3663 [TS+0x548] (live host 2026-11-15: [TS+0x548] is the
# sector whose +0x68 {ServerUserActor*, refs}[+0x70] lists both users; +0x540 holds 0x100000001). With +0x540 every
# held child (horse, pets, companions of either user) read "observer out of range" and was DROPPED, never announced
# to the other client: the invisible mounts.
def fts(s):
    a='inline constexpr uint32_t kTsSector = 0x540;'
    assert a in s
    return s.replace(a,'inline constexpr uint32_t kTsSector = 0x548;',1).replace('[[[actor+0x68]+0x1A0]+0x540]','[[[actor+0x68]+0x1A0]+0x548]')
rw(DST+'/mod/session2/child_hold.h',fts)
def fsv(s):
    a='read_ptr(*ts + 0x540)'
    assert a in s
    return s.replace(a,'read_ptr(*ts + 0x548)',1).replace('// sector = [TS+0x540].','// sector = [TS+0x548] (3663).',1)
rw(DST+'/mod/session2/server.cpp',fsv)
# ---- 39. FPS (Legion guest 2026-11-15: 23 -> 12 -> 4 FPS, the main thread sampled in NtQueryVirtualMemory):
# on_comp_job / on_mc_job (sections 24/27/31) run for EVERY component and movement job every frame and used
# game::safe_read, i.e. one VirtualQuery per read (up to 6 per job). Now plain reads under SEH, as
# remote_without_action already does: no system call in the hot path.
def fjob(s):
    a=s[s.index('void on_comp_job(safetyhook::Context& ctx) {'):s.index('constexpr Site kFlagSet')]
    b='''void on_comp_job(safetyhook::Context& ctx) {
    if (!g_remote_mode) return;
    uintptr_t cc = 0;
    __try {
        cc = *reinterpret_cast<const uintptr_t*>(ctx.rcx);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return;
    }
    if (!cc) return;
    if (contain_remote(cc, g_remote_skipped_job)) ctx.rip = reinterpret_cast<uintptr_t>(g_ret);
}

// SEH reads only (section 39): this runs for every movement job of every actor, every frame.
uintptr_t mc_job_cc(uintptr_t rcx) noexcept {
    __try {
        const auto ws = *reinterpret_cast<const uintptr_t*>(g_exe_base0 + off::kWorldSystemSlot);
        if (!ws) return 0;
        const auto p = *reinterpret_cast<const uintptr_t*>(ws + 0x60);
        if (!p || *reinterpret_cast<const uint8_t*>(p + 0x24C) != 0x19) return 0;
        const auto actor = *reinterpret_cast<const uintptr_t*>(rcx + 0x68);
        if (!actor) return 0;
        const auto comps = *reinterpret_cast<const uintptr_t*>(actor + 0x68);
        if (!comps) return 0;
        return *reinterpret_cast<const uintptr_t*>(comps + 0x40);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return 0;
    }
}

void on_mc_job(safetyhook::Context& ctx) {
    if (!g_remote_mode) return;
    const uintptr_t cc = mc_job_cc(ctx.rcx);
    if (!cc) return;
    if (contain_remote(cc, g_remote_skipped_job)) ctx.rip = reinterpret_cast<uintptr_t>(g_ret);
}

'''
    return s.replace(a,b,1)
rw(DST+'/mod/net/horse_diag.cpp',fjob)
# ---- 40. Host crash 2026-11-15 09:38 (exe+0x2506E7A, null [rcx] while a 2005 serialised an actor's list at
# +0x90): the HOST fast-travelled, its companions were rebuilt, and a mod release (now working since section 38)
# announced one of them to user 2 mid-rebuild. While the host's own client is loading ([[WS]+0x60]+0x24C != 0x19)
# no mod-initiated NotifyAppear runs: every pass keeps its entries for later (nothing dropped, nothing released).
def fhl(s):
    a='std::atomic<uint64_t> g_refresh_due_ms{0}, g_refresh_runs{0};'
    assert a in s
    s=s.replace(a,'''// Section 40: the host's own client is loading (fast travel, revive): its actors are being rebuilt.
bool host_client_loading() noexcept {
    const auto ws = game::fast_ptr(g_base + 0x72451C0);
    if (!ws || !*ws) return false;
    const auto p = game::fast_ptr(*ws + 0x60);
    if (!p || !*p) return false;
    const auto st = game::fast<uint8_t>(*p + 0x24C);
    return st && *st != 0x19;
}

'''+a,1)
    a2='''    if (!due || now_ms() < due) return;
    if (!server_realm()) return;'''
    assert a2 in s
    s=s.replace(a2,a2+'\n    if (host_client_loading()) return;  // section 40: later, once the host is in the world',1)
    reps=[('void sweep_children(const char* where, bool count_deferred) {\n','void sweep_children(const char* where, bool count_deferred) {\n    if (host_client_loading()) return;  // section 40\n'),
          ('void char_pass(bool may_release, const char* where) {\n','void char_pass(bool may_release, const char* where) {\n    if (host_client_loading()) return;  // section 40\n'),
          ('void settle_pass(bool may_release, const char* where) {\n','void settle_pass(bool may_release, const char* where) {\n    if (host_client_loading()) return;  // section 40\n'),
          ('void npc_pass(uint32_t target, bool allow_release, const char* where) {\n','void npc_pass(uint32_t target, bool allow_release, const char* where) {\n    if (allow_release && host_client_loading()) allow_release = false;  // section 40\n'),
          ('void npc_periodic(bool allow_release, const char* where) {\n','void npc_periodic(bool allow_release, const char* where) {\n    if (allow_release && host_client_loading()) allow_release = false;  // section 40\n')]
    for x,y in reps:
        assert s.count(x)==1, x
        s=s.replace(x,y,1)
    return s
rw(DST+'/mod/session2/child_hold.cpp',fhl)
# ---- 41. Host crash again at the same place (09:56:53): the gate of section 40 let the releases through the very
# moment the host's state reached 0x19, and a batch of NPC 2005s for user 2 hit an actor still being rebuilt. The
# host is now "unsettled" while loading AND for 8 s after its load ended.
def fsettle(s):
    a='''    const auto st = game::fast<uint8_t>(*p + 0x24C);
    return st && *st != 0x19;
}'''
    assert a in s
    return s.replace(a,'''    const auto st = game::fast<uint8_t>(*p + 0x24C);
    static std::atomic<uint64_t> last_loading_ms{0};
    const uint64_t now = GetTickCount64();
    if (st && *st != 0x19) {
        last_loading_ms.store(now, std::memory_order_relaxed);
        return true;
    }
    const uint64_t last = last_loading_ms.load(std::memory_order_relaxed);
    return last && now - last < 8000;  // section 41: its actors are still being rebuilt right after the load
}''',1)
rw(DST+'/mod/session2/child_hold.cpp',fsettle)
# ---- 42. Host crash a third time (10:06:55, guest fast travel), same 2005 serialisation, again right after D-53
# released the HOST's type-4 children (0xB010000C.., ~30 at once) to user 2, this time after 12 s held: not timing,
# those actors themselves (the host's companion roster) cannot be serialised for user 2. Before section 38 they were
# always dropped (never crashed). Host-owned type-4 children are dropped again for user 2; horses (type 5) and user 2's
# own children keep being released.
def fd53(s):
    a='''            if (step == ReleaseStep::Release) {
                due[n_due++] = {e.key, e.observer, user_key_of(e.observer_user), age, e.type, e.owner_who,
                                e.observer_host};'''
    assert a in s
    b='            const ReleaseStep step = release_step(e, r, why);'
    assert s.count(b)==1
    s=s.replace(b,'            ReleaseStep step = release_step(e, r, why);',1)
    return s.replace(a,'''            if (step == ReleaseStep::Release && e.owner_who == OwnerWho::Host && e.type == 4 && !e.observer_host) {
                step = ReleaseStep::Drop;
                why = "host's type-4 child: not announced to user 2 (section 42)";
            }
            if (step == ReleaseStep::Release) {
                due[n_due++] = {e.key, e.observer, user_key_of(e.observer_user), age, e.type, e.owner_who,
                                e.observer_host};''',1)
rw(DST+'/mod/session2/child_hold.cpp',fd53)
# ---- 43. Fourth host crash (10:15:16, guest fast travel, same serialisation): every crash so far had a fast travel
# in flight (host twice, guest twice). User 2's travel now unsettles the host too: no mod-initiated NotifyAppear
# from user 2's 2283 (TravelBegin) until 45 s later (the Legion's load takes ~45 s), and while user 2 is loading.
def ftb(s):
    a='void travel_probe_begin(const TravelBeginInfo& b) {\n'
    assert a in s
    return s.replace(a,a+'    child_hold_note_user2_travel();  // section 43\n',1)
rw(DST+'/mod/session2/travel_probe.cpp',ftb)
def fch2(s):
    a='void child_hold_request_refresh(uint64_t delay_ms) noexcept;'
    assert a in s
    return s.replace(a,a+'\n// Section 43: user 2 began a fast travel (its 2283): mod releases wait until it settled.\nvoid child_hold_note_user2_travel() noexcept;',1)
rw(DST+'/mod/session2/child_hold.h',fch2)
def fch3(s):
    a='''    const uint64_t last = last_loading_ms.load(std::memory_order_relaxed);
    return last && now - last < 8000;  // section 41: its actors are still being rebuilt right after the load
}'''
    assert a in s
    s=s.replace(a,'''    const uint64_t last = last_loading_ms.load(std::memory_order_relaxed);
    if (last && now - last < 8000) return true;  // section 41: its actors are still being rebuilt after the load
    // Section 43: user 2's own travel (its 2283) or load in flight.
    const uint64_t u2 = g_user2_travel_ms.load(std::memory_order_relaxed);
    if (u2 && now - u2 < 45000) return true;
    return user2_loading_now(now_ms());
}''',1)
    b='// Section 40: the host\'s own client is loading'
    assert b in s
    s=s.replace(b,'std::atomic<uint64_t> g_user2_travel_ms{0};  // section 43\n'+b,1)
    c='void child_hold_request_refresh(uint64_t delay_ms) noexcept {'
    s=s.replace(c,'''void child_hold_note_user2_travel() noexcept {
    g_user2_travel_ms.store(GetTickCount64(), std::memory_order_relaxed);
}

'''+c,1)
    return s
rw(DST+'/mod/session2/child_hold.cpp',fch3)
# ---- 44. Last line of defence for the same crash: the trampoline call of NotifyAppear runs under SEH. An access
# violation inside it (the 2005 serialisation of a half-built actor) is counted and logged once per 10 s instead of
# killing the game; mod releases then pause 20 s (host_client_loading says "unsettled").
def fseh(s):
    a='    g_notify.unsafe_call<void>(flag, sessions, keys);\n}'
    assert a in s
    s=s.replace(a,'    notify_guarded(flag, sessions, keys);\n}',1)
    b='void call_notify(uintptr_t flag, const GameVec* sessions, const GameVec* keys) {'
    assert s.count(b)==1
    s=s.replace(b,'''std::atomic<uint64_t> g_notify_faults{0}, g_notify_fault_ms{0}, g_notify_fault_logged_ms{0};
std::atomic<uint32_t> g_notify_fault_code{0};
// Section 44: plain function (no C++ objects) so __try is allowed.
void notify_guarded_raw(uintptr_t flag, const GameVec* sessions, const GameVec* keys) noexcept {
    __try {
        g_notify.unsafe_call<void>(flag, sessions, keys);
    } __except (g_notify_fault_code.store(GetExceptionCode()), EXCEPTION_EXECUTE_HANDLER) {
        g_notify_faults.fetch_add(1);
        g_notify_fault_ms.store(GetTickCount64());
    }
}
void notify_guarded(uintptr_t flag, const GameVec* sessions, const GameVec* keys) noexcept {
    const uint64_t before = g_notify_faults.load();
    notify_guarded_raw(flag, sessions, keys);
    if (g_notify_faults.load() == before) return;
    const uint64_t now = GetTickCount64();
    uint64_t last = g_notify_fault_logged_ms.load();
    if (now - last < 10000 || !g_notify_fault_logged_ms.compare_exchange_strong(last, now)) return;
    uint32_t first = 0, n = 0;
    uintptr_t s0 = 0;
    __try {
        n = keys ? keys->count : 0;
        first = n ? *static_cast<const uint32_t*>(keys->data) : 0;
        s0 = sessions && sessions->count ? *static_cast<const uintptr_t*>(sessions->data) : 0;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
    }
    log::error("Session2 NotifyAppear FAULT #{} (code 0x{:08X}) caught: {} key(s), first 0x{:08X}, session 0x{:X}; "
               "mod releases pause 20 s (section 44)",
               g_notify_faults.load(), g_notify_fault_code.load(), n, first, s0);
}

'''+b,1)
    c='''    if (u2 && now - u2 < 45000) return true;'''
    assert c in s
    s=s.replace(c,c+'''
    const uint64_t fault = g_notify_fault_ms.load(std::memory_order_relaxed);
    if (fault && now - fault < 20000) return true;  // section 44''',1)
    return s
rw(DST+'/mod/session2/child_hold.cpp',fseh)
# ---- 45. Section 44 caught the fault (10:29:32: 57 keys to user 2, host alive) but the whole batch was lost, so
# the guest saw no NPCs after the travel. A faulting batch is now retried key by key (each under SEH): only the
# actor(s) that cannot be serialised are skipped (logged, first 8).
def fretry(s):
    a='''    const uint64_t before = g_notify_faults.load();
    notify_guarded_raw(flag, sessions, keys);
    if (g_notify_faults.load() == before) return;'''
    assert a in s
    s=s.replace(a,'''    const uint64_t before = g_notify_faults.load();
    notify_guarded_raw(flag, sessions, keys);
    if (g_notify_faults.load() == before) return;
    // Section 45: one by one, so only the broken actor(s) are lost.
    uint32_t n_keys = 0;
    const uint32_t* key_data = nullptr;
    __try {
        n_keys = keys ? keys->count : 0;
        key_data = n_keys ? static_cast<const uint32_t*>(keys->data) : nullptr;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        n_keys = 0;
    }
    uint32_t bad = 0;
    if (n_keys > 1 && key_data) {
        for (uint32_t i = 0; i < n_keys && i < 0x4000; ++i) {
            uint32_t one = 0;
            __try {
                one = key_data[i];
            } __except (EXCEPTION_EXECUTE_HANDLER) {
                break;
            }
            const GameVec kv{&one, 1, 1};
            const uint64_t b1 = g_notify_faults.load();
            notify_guarded_raw(flag, sessions, &kv);
            if (g_notify_faults.load() != b1) {
                ++bad;
                if (g_notify_bad_logged.fetch_add(1) < 8) {
                    log::warn("Session2 NotifyAppear: key 0x{:08X} cannot be serialised (fault), skipped (section 45)", one);
                }
            }
        }
        log::info("Session2 NotifyAppear: faulting batch of {} retried one by one, {} skipped (section 45)", n_keys, bad);
    }''',1)
    b='std::atomic<uint32_t> g_notify_fault_code{0};'
    assert b in s
    s=s.replace(b,b+'\nstd::atomic<uint64_t> g_notify_bad_logged{0};',1)
    return s
rw(DST+'/mod/session2/child_hold.cpp',fretry)
# ---- 46. Refresh coverage (10:40: refresh sent 148 known keys, 13 faulted; guest 128 actors vs host 176): the
# refresh also takes every 0xB key of the HOST's client actor table (same process: [[[exe+0x72451C0]+0x30]+8],
# buckets +0x98 x 0x100, count +0x88) that resolves in the SAM, and runs a second time 15 s later (late actors,
# the ones that faulted while still being built).
def fcov(s):
    a='''        const auto sam = sam_now(false);
        if (!sam) {
            log::warn("Session2 reload refresh: the SAM is unreadable, nothing re-announced ({} known)", known.size());'''
    assert a in s
    s=s.replace(a,'''        size_t from_client = 0;
        {
            // Section 46: what the host's own client has (both players stand in the same place after the travel).
            const auto ws = game::fast_ptr(g_base + 0x72451C0);
            const auto mgr = ws && *ws ? game::fast_ptr(*ws + 0x30) : std::nullopt;
            const auto tab = mgr && *mgr ? game::fast_ptr(*mgr + 8) : std::nullopt;
            const auto nb = tab && *tab ? game::fast<uint32_t>(*tab + 0x88) : std::nullopt;
            const auto bb = tab && *tab ? game::fast_ptr(*tab + 0x98) : std::nullopt;
            if (nb && bb && *bb && *nb && *nb < 0x10000) {
                std::sort(known.begin(), known.end());
                std::vector<uint32_t> extra;
                for (uint32_t i = 0; i < *nb; ++i) {
                    const uintptr_t bucket = *bb + static_cast<uintptr_t>(i) * 0x100;
                    const auto cnt = game::fast<uint32_t>(bucket);
                    if (!cnt) continue;
                    for (uint32_t j = 0; j < *cnt && j < 31; ++j) {
                        const auto k = game::fast<uint32_t>(bucket + 8 + j * 8);
                        if (!k || (*k >> 28) != 0xB) continue;
                        if (!std::binary_search(known.begin(), known.end(), *k)) extra.push_back(*k);
                    }
                }
                std::sort(extra.begin(), extra.end());
                extra.erase(std::unique(extra.begin(), extra.end()), extra.end());
                from_client = extra.size();
                known.insert(known.end(), extra.begin(), extra.end());
            }
        }
        const auto sam = sam_now(false);
        if (!sam) {
            log::warn("Session2 reload refresh: the SAM is unreadable, nothing re-announced ({} known)", known.size());''',1)
    b='''        log::info("Session2 reload refresh #{}: {} key(s) announced again to user 2 in {} NotifyAppear call(s) | known {}{}, "
                  "gone or retired {}",
                  g_refresh_runs.load(), keys.size(), calls, known.size(), overflow ? " (OVERFLOWED: partial)" : "",
                  gone);'''
    assert b in s
    s=s.replace(b,'''        log::info("Session2 reload refresh #{}: {} key(s) announced again to user 2 in {} NotifyAppear call(s) | known {}{}, "
                  "of them from the host's client {}, gone or retired {}",
                  g_refresh_runs.load(), keys.size(), calls, known.size(), overflow ? " (OVERFLOWED: partial)" : "",
                  from_client, gone);
        // Section 46: once more, 15 s later.
        if (!g_refresh_second.exchange(true)) g_refresh_due_ms.store(now_ms() + 15000);''',1)
    c='std::atomic<uint64_t> g_refresh_due_ms{0}, g_refresh_runs{0};'
    assert c in s
    s=s.replace(c,c+'\nstd::atomic<bool> g_refresh_second{false};  // section 46: the second pass of this request is queued',1)
    d='''    g_refresh_due_ms.store(now_ms() + delay_ms);'''
    assert d in s
    s=s.replace(d,'''    g_refresh_second.store(false);
    g_refresh_due_ms.store(now_ms() + delay_ms);''',1)
    return s
rw(DST+'/mod/session2/child_hold.cpp',fcov)
# ---- 47. Choppy remote NPCs / horses on the guest: RemoteInterp ran (20031 calls) but resolved 0 ("no CC"): the
# movement controller's interpolation buffers moved +0x60 on 3663. Live (Legion, 3 NPCs): pos buffer mc+0x430, rot
# mc+0x4E8 (0xB8 apart as on 2976), samples still 0xA0 bytes with the u64 ms at +0x98; mc+0x60 CC, CC+0xC8 mc,
# mc+0x68 actor unchanged.
def fri(s):
    for a,b in (('inline constexpr uint32_t kPosBuffer = 0x3D0;','inline constexpr uint32_t kPosBuffer = 0x430;'),
                ('inline constexpr uint32_t kRotBuffer = 0x488;','inline constexpr uint32_t kRotBuffer = 0x4E8;')):
        assert a in s
        s=s.replace(a,b,1)
    return s
rw(DST+'/mod/session2/remote_interp_logic.h',fri)
def frm(s):
    a='inline constexpr uint32_t kInterpPosBuffer = 0x3D0;'
    assert a in s
    return s.replace(a,'inline constexpr uint32_t kInterpPosBuffer = 0x430;  // 3663 (section 47)',1)
rw(DST+'/mod/session2/remote_motion_logic.h',frm)
# ---- 48. Root cause of the 2005 serialisation fault (sections 40-45): the observer serialiser loop 0x2353B40
# walks [obj+0x90] x [obj+0x98] entries of 0x38 bytes and calls 0x2506E70(rcx = [entry+8], writer). 0x2506E70
# handles an EMPTY holder ([rcx] == 0 -> writes "absent") but not a missing one (rcx == 0). Some NPCs (13 in the
# 10:52 test, always the same ones) carry such an entry: they were never sent to user 2. A mid hook on the call
# 0x2353B47 gives a missing holder the address of a zeroed one: the field is written as "absent", the NPC goes out.
def fser(s):
    a='    g_notify = safetyhook::create_inline(reinterpret_cast<void*>(g_base + kNotifyAppear),'
    assert s.count(a)==1
    s=s.replace(a,'''    // Section 48: the observer serialiser's missing holder.
    {
        static const uint8_t kSerCall[] = {0x48, 0x8B, 0x4F, 0x08, 0x48, 0x8B, 0xD3, 0xE8};
        if (code_ok(0x2353B40, kSerCall, sizeof kSerCall, "the observer serialiser's holder call")) {
            g_ser_hook = safetyhook::create_mid(reinterpret_cast<void*>(g_base + 0x2353B47), &on_ser_holder);
            log::info("Session2 serialiser fix (section 48): {}", g_ser_hook ? "ON (exe+0x2353B47: a missing holder is "
                      "written as absent)" : "could NOT be hooked");
        }
    }
'''+a,1)
    b='bool code_ok(uintptr_t rva, const uint8_t* bytes, size_t n, const char* what) {'
    assert s.count(b)==1
    s=s.replace(b,'''SafetyHookMid g_ser_hook;
alignas(16) const uint64_t g_empty_holder[2] = {0, 0};
std::atomic<uint64_t> g_ser_fixed{0};
void on_ser_holder(safetyhook::Context& c) noexcept {
    if (c.rcx == 0) {
        c.rcx = reinterpret_cast<uintptr_t>(&g_empty_holder[0]);
        g_ser_fixed.fetch_add(1, std::memory_order_relaxed);
    }
}

'''+b,1)
    return s
rw(DST+'/mod/session2/child_hold.cpp',fser)
