# guest_seq_route / guest_sched_bind on 3663: new sites, new context bytes, frame slots and registers (routeport.py)
import re
def _arr_set(s,name,addr):
    nb=', '.join('0x%02X'%b for b in new.b(addr, len(re.findall(r'0x[0-9A-Fa-f]{2}', re.search(r'constexpr uint8_t %s\[\d*\] = \{([^}]*)\}'%name,s).group(1)))))
    return re.sub(r'(constexpr uint8_t %s\[\d*\] = \{)[^}]*\}'%name,lambda m_: m_.group(1)+nb+'}',s,count=1)
ROUTE_ARR={'kS2335Bytes':0xD73401,'kS2335Key':0xD72F71,'kS2335Find':0xD73347,'kS2126Vf48':0xD73BB1,'kS2958Find':0xD74A4D,
           'kS2909Find':0xD7519D,'kSChangeVf48':0xD74AED,'kActor2661Bytes':0xD6CB95,'kVf48_2661Bytes':0xD6CBF4,'kWrap2981Bytes':0xD6E221,
           'kActor2981Bytes':0xD6E6C1,'kVf48_2981Bytes':0xD6E71A,'kVf48_2810Bytes':0xD6D0EB,'kSeq2810Bytes':0xD6D104,
           'kVf48_2322Bytes':0xCD5631,'kFind2322Bytes':0xCD55AA,'kFind2524Bytes':0xD6F1F9,'kVf48_2524Bytes':0xD6F296}
def fix_route(s):
    for n,a in ROUTE_ARR.items(): s=_arr_set(s,n,a)
    s=s.replace('constexpr uintptr_t kS2335 = 0xC0B5C1;','constexpr uintptr_t kS2335 = 0xD73401;  // 3663: lea rcx,[rbp-0x28] ; mov [rsp+0x60],rcx')
    for o,n in (('{0xC0B242, kS2335Key}','{0xD72F71, kS2335Key}'),('{0xC0B50A, kS2335Find}','{0xD73347, kS2335Find}'),
                ('{0xC0C6B7, kS2958Find}','{0xD74A4D, kS2958Find}'),('{0xC0CBE7, kS2909Find}','{0xD7519D, kS2909Find}'),
                ('{0xC076E6, kActor2981Bytes}','{0xD6E6C1, kActor2981Bytes}'),('{0xC080D1, kFind2524Bytes}','{0xD6F1F9, kFind2524Bytes}')):
        s=s.replace(o,n)
    s=re.sub(r'\{0x[0-9A-F]+, kFind2322Bytes\}','{0xCD55AA, kFind2322Bytes}',s)
    s=s.replace('constexpr uint32_t kFieldSlot[gsr::kPoints] = {0x40, 0x38, 0x48, 0x58, 0x48};',
                'constexpr uint32_t kFieldSlot[gsr::kPoints] = {0x28, 0x10, 0x38, 0x48, 0x38};  // 3663 (kVf48_* sites)')
    # P2661: the kind is r14b (0xD6CB99 movzx r14d ; 0xD6CBFE cmp r14b,4); P2981: the wrapper is r14 (0xD6E221)
    s=s.replace('f.kind_ok = (ctx.rbx & 0xFF) == 4;','f.kind_ok = (ctx.r14 & 0xFF) == 4;  // 3663: r14b')
    s=s.replace('if (point == Point::P2661) ctx.rbx = 0;','if (point == Point::P2661) ctx.r14 = 0;  // 3663: cmp r14b,4')
    s=s.replace('''        case Point::P2981:
            f.actor = rdp(ctx.r15);
            raw = rdp(ctx.r15 + kWrap_Raw);''','''        case Point::P2981:  // 3663: the wrapper is r14 (0xD6E221 mov r14,r8)
            f.actor = rdp(ctx.r14);
            raw = rdp(ctx.r14 + kWrap_Raw);''')
    s=s.replace('''            f.actor = rdv<uint8_t>(ctx.rbp) ? rdp(ctx.rbp - 8) : 0;
            f.packet_key = rdv<uint32_t>(ctx.rbp + 0x78);
            seq = rdv<uint64_t>(ctx.rbp - 0x58);
            value = rdv<uint32_t>(ctx.rbp - 0x60);''','''            f.actor = rdv<uint8_t>(ctx.rbp + 0x10) ? rdp(ctx.rbp + 8) : 0;  // 3663: 0xD6D059
            f.packet_key = rdv<uint32_t>(ctx.rbp + 0x88);
            seq = rdv<uint64_t>(ctx.rbp - 0x48);
            value = rdv<uint32_t>(ctx.rbp - 0x50);''')
    s=s.replace('f.actor = rdv<uint8_t>(ctx.rbp - 8) ? rdp(ctx.rbp - 0x10) : 0;  // the handler\'s own lookup',
                'f.actor = rdv<uint8_t>(ctx.rbp + 8) ? rdp(ctx.rbp) : 0;  // the handler\'s own lookup (3663: 0xCD55AA)')
    s=s.replace('''            f.actor = rdv<uint8_t>(ctx.rbp + 0x68) ? rdp(ctx.rbp + 0x60) : 0;
            f.packet_key = rdv<uint32_t>(ctx.rbp - 0x60);
            key = rdv<uint64_t>(ctx.rbp - 0x38);
            resource = rdv<uint32_t>(ctx.rbp - 0x48);''','''            f.actor = rdv<uint8_t>(ctx.rbp + 0x80) ? rdp(ctx.rbp + 0x78) : 0;  // 3663: 0xD73347
            f.packet_key = rdv<uint32_t>(ctx.rbp - 0x40);
            key = rdv<uint64_t>(ctx.rbp - 0x30);
            resource = rdv<uint32_t>(ctx.rbp - 0x34);''')
    s=s.replace('''            f.actor = rdv<uint8_t>(ctx.rbp - 8) ? rdp(ctx.rbp - 0x10) : 0;
            f.packet_key = rdv<uint32_t>(ctx.rbp + 0x70);
            key = rdv<uint64_t>(ctx.rbp + 0x78);''','''            f.actor = rdv<uint8_t>(ctx.rbp + 8) ? rdp(ctx.rbp) : 0;  // 3663: 0xD73B1F
            f.packet_key = rdv<uint32_t>(ctx.rbp + 0x80);
            key = rdv<uint64_t>(ctx.rbp + 0x88);''')
    s=s.replace('''            f.actor = rdv<uint8_t>(ctx.rbp + 0x18) ? rdp(ctx.rbp + 0x10) : 0;
            f.packet_key = rdv<uint32_t>(ctx.rbp + 0x10A8);
            key = rdv<uint64_t>(ctx.rbp - 0x60);
            resource = rdv<uint32_t>(ctx.rbp - 0x58);''','''            f.actor = rdv<uint8_t>(ctx.rbp + 0x28) ? rdp(ctx.rbp + 0x20) : 0;  // 3663: 0xD74A4D / 0xD7519D
            f.packet_key = rdv<uint32_t>(ctx.rbp + 0x10B8);
            key = rdv<uint64_t>(ctx.rbp - 0x50);
            resource = rdv<uint32_t>(ctx.rbp - 0x48);''')
    return s
def fix_bind(s):
    s=_arr_set(s,'kKeyBytes',0xD72F71)
    s=s.replace('!bytes_at(0xC0B242, kKeyBytes)','!bytes_at(0xD72F71, kKeyBytes)')
    s=s.replace('constexpr uint32_t kFrameKey = 0x38;  // [rbp-0x38]','constexpr uint32_t kFrameKey = 0x30;  // [rbp-0x30] (3663: 0xD72F75)')
    return s
rw(DST+'/mod/session2/guest_seq_route.cpp',fix_route)
rw(DST+'/mod/session2/guest_sched_bind.cpp',fix_bind)
# 2126: the point is now lea rdx,[rbp+0x88] (7) + mov rcx,[rax+0x178] (7): both relocated, 14 bytes
rw(DST+'/mod/session2/guest_seq_route.cpp',lambda s: s.replace('{gsr::SchedPoint::P2126, kS2126, 11, kS2126Bytes,','{gsr::SchedPoint::P2126, kS2126, 14, kS2126Bytes,'))
