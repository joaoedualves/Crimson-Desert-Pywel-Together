import pefile, struct, bisect, numpy as np

class Img:
    def __init__(self, path):
        pe = pefile.PE(path, fast_load=True)
        self.pe = pe
        size = pe.OPTIONAL_HEADER.SizeOfImage
        data = open(path, 'rb').read()
        self.mem = bytearray(size)
        self.secs = []
        for s in pe.sections:
            va, raw, rs = s.VirtualAddress, s.PointerToRawData, s.SizeOfRawData
            n = min(rs, s.Misc_VirtualSize or rs)
            self.mem[va:va + n] = data[raw:raw + n]
            self.secs.append((s.Name.rstrip(b'\0').decode(errors='ignore'), va, s.Misc_VirtualSize, s.Characteristics))
        exc = pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]
        arr = np.frombuffer(bytes(self.mem[exc.VirtualAddress:exc.VirtualAddress + exc.Size]), dtype='<u4').reshape(-1, 3)
        arr = arr[arr[:, 0].argsort()]
        self.fstart = arr[:, 0].astype(np.int64)
        self.fend = arr[:, 1].astype(np.int64)
        self.ts = pe.FILE_HEADER.TimeDateStamp

    def func_at(self, rva):
        i = bisect.bisect_right(self.fstart, rva) - 1
        if i >= 0 and self.fstart[i] <= rva < self.fend[i]:
            return int(self.fstart[i]), int(self.fend[i])
        return None

    def sec(self, rva):
        for n, va, vs, ch in self.secs:
            if va <= rva < va + vs:
                return n
        return None

    def b(self, rva, n):
        return bytes(self.mem[rva:rva + n])
