import sys, json, pickle, os, hashlib, difflib
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
from capstone.x86 import X86_OP_MEM, X86_OP_IMM, X86_REG_RIP
sys.path.insert(0, '/home/claude/re')
from pe import Img

md = Cs(CS_ARCH_X86, CS_MODE_64); md.detail = True; md.skipdata = True
BR = {'call', 'jmp'} | {m for m in ('je jne jg jge jl jle ja jae jb jbe js jns jo jno jp jnp jrcxz jecxz loop').split()}

def norm(im, start, end, maxn=None):
    code = im.b(start, end - start)
    out = []
    for i in md.disasm(code, 0x140000000 + start):
        if i.mnemonic == '.byte':
            out.append('DB'); continue
        m = i.mnemonic
        if m in BR:
            out.append(m + ' REL' if i.operands and i.operands[0].type == X86_OP_IMM else m + ' ' + i.op_str);
        else:
            ops = []
            for o in i.operands:
                if o.type == X86_OP_MEM:
                    if o.mem.base == X86_REG_RIP: ops.append('[RIP]')
                    else: ops.append('[%d+%d*%d+%#x]' % (o.mem.base, o.mem.index, o.mem.scale, o.mem.disp))
                elif o.type == X86_OP_IMM:
                    v = o.imm & 0xFFFFFFFFFFFFFFFF
                    ops.append('ADDR' if 0x140000000 <= v < 0x160000000 else hex(v))
                else:
                    ops.append(i.reg_name(o.reg))
            out.append(m + ' ' + ','.join(ops))
        if maxn and len(out) >= maxn: break
    return out

def h(seq): return hashlib.md5('\n'.join(seq).encode()).hexdigest()

def build_index(im, path, k=24):
    if os.path.exists(path): return pickle.load(open(path, 'rb'))
    idx = {}
    for s, e in zip(im.fstart, im.fend):
        s, e = int(s), int(e)
        if e - s > 0x20000: continue
        p = h(norm(im, s, min(e, s + 400), k))
        idx.setdefault(p, []).append((s, e))
    pickle.dump(idx, open(path, 'wb'))
    return idx

def ratio(a, b):
    return difflib.SequenceMatcher(None, a, b, autojunk=False).ratio()
