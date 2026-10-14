"""Read flattened version-2 game CFG nodes without executing their contents."""
import pathlib, struct

def read(path):
    data = pathlib.Path(path).read_bytes()
    pos = 0
    def number():
        nonlocal pos
        value = struct.unpack_from('<i', data, pos)[0]; pos += 4
        return value
    def string():
        nonlocal pos
        length = number(); size = abs(length) * (2 if length < 0 else 1)
        if pos + size > len(data): raise ValueError('Truncated CFG string')
        raw = data[pos:pos + size]; pos += size
        return raw.decode('utf-16-le' if length < 0 else 'utf8').rstrip('\0')
    if number() != 2: raise ValueError('Unsupported CFG version')
    count = number()
    if not 0 <= count <= 1000000: raise ValueError('Invalid string count')
    strings = [string() for _ in range(count)]
    def lookup(index):
        if not 0 <= index < len(strings): raise ValueError('Invalid string index')
        return strings[index]
    def node():
        nonlocal pos
        name, _, value = number(), number(), number(); number()
        if value: number()
        flag = data[pos]; pos += 1
        if flag in (1, 5, 7): string(); string()
        if value: return lookup(name), lookup(value)
        count = number()
        if not 0 <= count < 100000: raise ValueError('Invalid node count')
        return lookup(name), dict(node() for _ in range(count))
    if data[pos:pos + 13] != bytes(13): raise ValueError('Invalid CFG root')
    pos += 13; count = number()
    if not 0 <= count < 1000000: raise ValueError('Invalid root count')
    nodes = dict(node() for _ in range(count))
    if pos != len(data): raise ValueError('Trailing CFG bytes')
    nodes.update({v['SID']: v for v in list(nodes.values()) if isinstance(v, dict) and v.get('SID')})
    return nodes
