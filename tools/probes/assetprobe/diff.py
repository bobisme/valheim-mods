import sys
def load(p):
    d={}
    for l in open(p):
        parts=l.rstrip('\n').split('\t')
        if len(parts)==3: d[(parts[0],parts[2])]=int(parts[1])
        elif l.startswith(('total','asm','unity')): d[('#',l.rsplit(' ',1)[0] if not l.startswith('unity') else 'mem')]=l.strip()
    return d
a,b=load(sys.argv[1]),load(sys.argv[2])
for k in sorted(set(a)|set(b)):
    if k[0]=='#':
        if a.get(k)!=b.get(k): print(a.get(k),'->',b.get(k))
        continue
    x,y=a.get(k,0),b.get(k,0)
    if x!=y: print(f"{k[0]:15} {y-x:+5}  {k[1]}")
