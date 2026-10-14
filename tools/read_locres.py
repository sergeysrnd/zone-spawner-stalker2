import pathlib, struct, json
class Reader:
    def __init__(self,b): self.b=b; self.p=0
    def num(self,f):
        v=struct.unpack_from('<'+f,self.b,self.p)[0];self.p+=struct.calcsize(f);return v
    def string(self):
        n=self.num('i')
        if n==0:return ''
        length=abs(n)*(2 if n<0 else 1)
        b=self.b[self.p:self.p+length];self.p+=length
        return b.decode('utf-16-le' if n<0 else 'utf-8').rstrip('\0')
def read(path):
    r=Reader(path.read_bytes());r.p=16;v=r.num('B');offset=r.num('q');pos=r.p
    r.p=offset;strings=[]
    for _ in range(r.num('i')):
        strings.append(r.string())
        if v>=2:r.num('i')
    r.p=pos
    if v>=2:r.num('I')
    result={}
    for _ in range(r.num('I')):
        if v>=2:r.num('I')
        ns=r.string()
        for _ in range(r.num('I')):
            if v>=2:r.num('I')
            key=r.string();r.num('I');index=r.num('i')
            result[key]=strings[index] if index>=0 else ''
    return result
if __name__=='__main__':
    root=pathlib.Path(__file__).parent
    result=read(root/'Game-ru.locres')
    (root/'ru-strings.json').write_text(json.dumps(result,ensure_ascii=False),encoding='utf8')
    print('Localization entries:',len(result))
    print([(k,v) for k,v in result.items() if 'Medkit' in k][:15])
