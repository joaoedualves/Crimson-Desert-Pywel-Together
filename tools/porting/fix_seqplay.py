# GuestSeqPlay on 3663: 0x8B6060 was split; the tick is 0x9CB630 (called from 0x9CF7FA with xmm6 = dt, callee-saved),
# Q (the ClientField) in rbx, M = 0xAE0710(Q) (old vf48 0x9A5BE0), the mode byte [[rdi+0x80]+0x58]; the live update
# 0x9CB7A0 is called at 0x9CF8E6 with no gate byte any more.
import re
def _set(s,name,addr,n=None):
    m=re.search(r'constexpr uint8_t %s\[\d*\] = \{([^}]*)\}'%name,s)
    n=n or len(re.findall(r'0x[0-9A-Fa-f]{2}',m.group(1)))
    nb=', '.join('0x%02X'%b for b in new.b(addr,n))
    return re.sub(r'(constexpr uint8_t %s\[\d*\] = \{)[^}]*\}'%name,lambda m_: m_.group(1)+nb+'}',s,count=1)
def fix_seqplay(s):
    s=s.replace('constexpr uintptr_t kPoint = 0x8B6144;','constexpr uintptr_t kPoint = 0x9CB6D9;  // 3663: movzx r8d,byte [r8+0x58] in the tick 0x9CB630')
    s=_set(s,'kPointBytes',0x9CB6D9)
    s=_set(s,'kFrameBytes',0x9CF7F7,8)      # mov rcx,rdi ; call 0x9CB630 (the caller keeps dt in xmm6)
    s=_set(s,'kDtBytes',0x9CF7E5,4)         # vmovaps xmm6,xmm1
    s=_set(s,'kActorBytes',0x9CB64B)
    s=_set(s,'kAttachBytes',0x9CB662,7)     # mov rax,[rcx+0x70] ; mov rbx,[rax]  (Q in rbx)
    s=_set(s,'kVf48Bytes',0x9CB6C5,13)      # mov rcx,rbx ; call 0xAE0710 ; test rax,rax ; je
    s=_set(s,'kModePtrBytes',0x9CB6D2)
    s=_set(s,'kLiveGate1Bytes',0x9CF8DF,12) # vmovaps xmm1,xmm6 ; mov rcx,rdi ; call 0x9CB7A0
    s=_set(s,'kLiveGate2Bytes',0x9CF8DF,12)
    s=s.replace('''        {0x8B6076, kFrameBytes},      {0x8B608F, kDtBytes},     {0x8B60AF, kActorBytes},
        {0x8B60C6, kAttachBytes},     {0x8B6102, kVf48Bytes},   {0x8B613D, kModePtrBytes},
        {0x8B63CE, kLiveGate1Bytes},  {0x8B67E3, kLiveGate2Bytes}, {kTickRva, kTickBytes},''','''        {0x9CF7F7, kFrameBytes},      {0x9CF7E5, kDtBytes},     {0x9CB64B, kActorBytes},
        {0x9CB662, kAttachBytes},     {0x9CB6C5, kVf48Bytes},   {0x9CB6D2, kModePtrBytes},
        {0x9CF8DF, kLiveGate1Bytes},  {0x9CF8DF, kLiveGate2Bytes}, {kTickRva, kTickBytes},''')
    s=s.replace('constexpr uint32_t kModeByte = 0x50;              // [r8+0x50]','constexpr uint32_t kModeByte = 0x58;              // [r8+0x58] (3663)')
    s=s.replace('const uintptr_t q = rdp(ctx.rbp - kFrame_Q);','const uintptr_t q = ctx.rbx;  // 3663: Q stays in rbx (0x9CB666) up to the tick call')
    s=s.replace('base.live_gate = rdv<uint8_t>(g_base + kLiveGateByte, 1) == 0;','base.live_gate = true;  // 3663: 0x9CF8E6 calls the live update with no gate byte')
    return s
rw(DST+'/mod/session2/guest_seq_play.cpp',fix_seqplay)
