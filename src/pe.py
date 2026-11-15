import struct, re, sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_64

import os
EXE = os.environ.get("EAW_EXE", r"F:\Game Files\Steam\steamapps\common\Star Wars Empire at War\GameData\StarWarsG.exe.orig")
data = open(EXE, 'rb').read()
pe = struct.unpack_from('<I', data, 0x3c)[0]
nsec = struct.unpack_from('<H', data, pe + 6)[0]
optsz = struct.unpack_from('<H', data, pe + 20)[0]
BASE = struct.unpack_from('<Q', data, pe + 24 + 24)[0]
secs = []
for i in range(nsec):
    o = pe + 24 + optsz + i * 40
    name = data[o:o+8].rstrip(b'\0').decode()
    vsz, va, rsz, roff = struct.unpack_from('<IIII', data, o + 8)
    secs.append((name, va, vsz, roff, rsz))

def sec(name):
    return [s for s in secs if s[0] == name][0]

def off2va(off):
    for n, va, vsz, ro, rs in secs:
        if ro <= off < ro + rs:
            return BASE + va + off - ro

def va2off(v):
    r = v - BASE
    for n, va, vsz, ro, rs in secs:
        if va <= r < va + max(vsz, rs):
            return ro + r - va

md = Cs(CS_ARCH_X86, CS_MODE_64)
md.detail = False

def dis(v, n=60):
    o = va2off(v)
    out = []
    for i in md.disasm(data[o:o + n * 15], v):
        out.append("%x: %-8s %s" % (i.address, i.mnemonic, i.op_str))
        if len(out) >= n:
            break
    return out

def strva(s):
    res = []
    for m in re.finditer(re.escape(s.encode()), data):
        res.append(off2va(m.start()))
    return res

_text = sec('.text')
def rip_xrefs(target):
    """find instructions with rip-relative disp32 to target (scan raw bytes)."""
    n, va, vsz, ro, rs = _text
    res = []
    t = data[ro:ro + rs]
    # every position p where disp32 at p resolves: insn_end = p+4+k (k imm bytes 0..4)
    for p in range(len(t) - 4):
        d = struct.unpack_from('<i', t, p)[0]
        base = BASE + va + p + 4
        for k in (0, 1, 4):
            if base + k + d == target:
                # check modrm before p is rip-relative
                if (t[p - 1] & 0xC7) == 0x05:
                    res.append((BASE + va + p, k))
    return res

def find_qword(val, secname='.rdata'):
    n, va, vsz, ro, rs = sec(secname)
    b = struct.pack('<Q', val)
    return [BASE + va + m.start() for m in re.finditer(re.escape(b), data[ro:ro + rs]) if m.start() % 8 == 0]

def vtables_for(mangled):
    res = []
    for s in strva(mangled):
        td = s - 0x10
        tdr = td - BASE
        n, va, vsz, ro, rs = sec('.rdata')
        blob = data[ro:ro + rs]
        for m in re.finditer(re.escape(struct.pack('<I', tdr)), blob):
            col = BASE + va + m.start() - 12
            if struct.unpack_from('<I', blob, m.start() - 12)[0] == 1:
                for q in find_qword(col):
                    res.append(q + 8)
    return res

def q(v):
    return struct.unpack_from('<Q', data, va2off(v))[0]

def funcs():
    n, va, vsz, ro, rs = sec('.pdata')
    out = []
    for i in range(0, vsz, 12):
        b, e, u = struct.unpack_from('<III', data, ro + i)
        if b == 0: break
        out.append((BASE + b, BASE + e))
    return out
FUNCS = None
def func_of(a):
    global FUNCS
    if FUNCS is None: FUNCS = funcs()
    import bisect
    i = bisect.bisect_right([f[0] for f in FUNCS], a) - 1
    return FUNCS[i]
def disf(a):
    b, e = func_of(a)
    o = va2off(b)
    return ["%x: %-8s %s" % (i.address, i.mnemonic, i.op_str) for i in md.disasm(data[o:o + (e - b)], b)]
