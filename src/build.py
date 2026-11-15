"""Build the Q/E/Z/C camera patch.  Usage: python build.py eaw|foc
Reads <game>\\StarWarsG.exe.orig, writes StarWarsG_<game>.exe here."""
import subprocess, struct, sys, os

ROOT = r"F:\Game Files\Steam\steamapps\common\Star Wars Empire at War"
GAMES = {
    'eaw': dict(dir='GameData', hook=0x1403e50e0, ret=0x1403e50e9, je=0x1403e5160,
                iat=0x14075d888, const=0x140958820, vcall=0x1d0),
    'foc': dict(dir='corruption', hook=0x14044bf20, ret=0x14044bf29, je=0x14044bfa0,
                iat=0x1407fd888, const=0x140a2a6f0, vcall=0x1e8),
}
game = sys.argv[1]
G = GAMES[game]
os.environ['EAW_EXE'] = os.path.join(ROOT, G['dir'], 'StarWarsG.exe.orig')
from pe import *

B = r"G:\msys64\mingw64\bin"
HOOK_LEN = 9
src = open("cave.s").read().replace("VCALL_GETCTRL", hex(G['vcall']))
open("cave_gen.s", "w").write(src)
subprocess.check_call([B + r"\as.exe", "cave_gen.s", "-o", "cave.o"])
subprocess.check_call([B + r"\objcopy.exe", "-O", "binary", "-j", ".text", "cave.o", "cave.bin"])
code = bytearray(open("cave.bin", "rb").read())

n, va, vsz, ro, rs = sec('.text')
CAVE = (BASE + va + vsz + 3) & ~3
assert CAVE + len(code) <= BASE + va + rs, "cave too big: %d > %d" % (len(code), BASE + va + rs - CAVE)

targets = {0x11111111: G['iat'], 0x22222222: G['const'], 0x33333333: G['je'], 0x44444444: G['ret']}
for ph, tgt in targets.items():
    pb = struct.pack('<I', ph)
    p = code.find(pb)
    cnt = 0
    while p >= 0:
        struct.pack_into('<i', code, p, tgt - (CAVE + p + 4))
        cnt += 1
        p = code.find(pb, p + 4)
    assert cnt, hex(ph)
    print(hex(ph), '->', hex(tgt), 'x', cnt)

out = bytearray(data)
co = va2off(CAVE)
assert all(b == 0 for b in out[co:co + len(code)])
out[co:co + len(code)] = code
ho = va2off(G['hook'])
assert out[ho:ho + 7] == bytes.fromhex('4038b780000000') and out[ho + 7] == 0x74
assert G['hook'] + 9 + struct.unpack_from('<b', out, ho + 8)[0] == G['je']
out[ho:ho + HOOK_LEN] = b'\xe9' + struct.pack('<i', CAVE - (G['hook'] + 5)) + b'\x90' * (HOOK_LEN - 5)
# make the slack part of the mapped section: VirtualSize = SizeOfRawData
sh = pe + 24 + struct.unpack_from('<H', data, pe + 20)[0]
assert out[sh:sh + 5] == b'.text'
struct.pack_into('<I', out, sh + 8, rs)
open("StarWarsG_%s.exe" % game, "wb").write(out)
print('cave at', hex(CAVE), len(code), 'bytes, free left', BASE + va + rs - CAVE - len(code))

from capstone import Cs, CS_ARCH_X86, CS_MODE_64
md2 = Cs(CS_ARCH_X86, CS_MODE_64)
with open("StarWarsG_%s.lst" % game, "w") as f:
    for i in md2.disasm(bytes(out[ho:ho + HOOK_LEN]), G['hook']):
        f.write("%x: %s %s\n" % (i.address, i.mnemonic, i.op_str))
    f.write('---\n')
    for i in md2.disasm(bytes(code), CAVE):
        f.write("%x: %s %s\n" % (i.address, i.mnemonic, i.op_str))
