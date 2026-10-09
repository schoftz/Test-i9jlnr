import numpy as np, json, sys
from skimage.draw import polygon
from skimage.morphology import skeletonize, binary_closing, disk, remove_small_objects
from scipy import ndimage
d=np.load('tris.npz')
st=[d[k] for k in d.files if k.startswith('Street')][0]
lots=[(k,d[k]) for k in d.files if k.startswith('ParkingLot')]
R=2.0
v=st.reshape(-1,3); x0,z0=v[:,0].min()-10,v[:,2].min()-10; x1,z1=v[:,0].max()+10,v[:,2].max()+10
W=int((x1-x0)/R)+1; H=int((z1-z0)/R)+1
mask=np.zeros((H,W),bool); hgt=np.full((H,W),-1e9)
for t in st:
    # dik/çok eğimli üçgenleri atla
    n=np.cross(t[1]-t[0],t[2]-t[0]); 
    if np.linalg.norm(n)<1e-9 or abs(n[1])/np.linalg.norm(n)<0.6: continue
    cols=(t[:,0]-x0)/R; rows=(t[:,2]-z0)/R
    rr,cc=polygon(rows,cols,(H,W))
    if len(rr)==0:
        rr=np.clip(np.round(rows).astype(int),0,H-1); cc=np.clip(np.round(cols).astype(int),0,W-1)
    # yükseklik: barisentrik yerine üçgen ortalaması + düzlem
    A=np.c_[t[:,0],t[:,2],np.ones(3)]
    try: coef=np.linalg.solve(A,t[:,1])
    except np.linalg.LinAlgError: coef=np.array([0,0,t[:,1].mean()])
    wx=x0+cc*R; wz=z0+rows.mean()*0+rr*R
    y=coef[0]*wx+coef[1]*wz+coef[2]
    mask[rr,cc]=True; hgt[rr,cc]=np.maximum(hgt[rr,cc],y)
print('raster',W,H,mask.sum()*R*R/1e6,'km2')
mask=binary_closing(mask,disk(2))
mask=remove_small_objects(mask,200)
# boşluk yüksekliklerini doldur
known=hgt>-1e8
idx=ndimage.distance_transform_edt(~known,return_distances=False,return_indices=True)
hgt=hgt[idx[0],idx[1]]
dist=ndimage.distance_transform_edt(mask)*R
sk=skeletonize(mask)
# kısa çıkıntıları buda
def nb(r,c):
    for dr in (-1,0,1):
        for dc in (-1,0,1):
            if (dr or dc) and 0<=r+dr<H and 0<=c+dc<W and sk[r+dr,c+dc]: yield r+dr,c+dc
for it in range(12):
    ends=[(r,c) for r,c in zip(*np.nonzero(sk)) if sum(1 for _ in nb(r,c))==1]
    removed=0
    for r,c in ends:
        path=[(r,c)]; cur=(r,c); prev=None
        while len(path)<8:
            ns=[p for p in nb(*cur) if p!=prev and p not in path]
            if len(ns)!=1: break
            prev=cur; cur=ns[0]; path.append(cur)
        if len(path)<8 and sum(1 for _ in nb(*cur))>=3:
            for p in path[:-1]: sk[p]=False; removed+=1
    if removed==0: break
pix=set(zip(*np.nonzero(sk)))
deg={p:sum(1 for _ in nb(*p)) for p in pix}
key=[p for p in pix if deg[p]!=2]
# kavşak kümelerini birleştir
lab,nl=ndimage.label(np.isin(np.arange(H*W).reshape(H,W),[r*W+c for r,c in key]) if False else np.zeros((H,W)),)
keyset=set(key)
nodes=[];nid={}
def addnode(p):
    if p in nid: return nid[p]
    nid[p]=len(nodes); nodes.append(p); return nid[p]
# junction clustering
jm=np.zeros((H,W),bool)
for p in key: jm[p]=True
jl,jn=ndimage.label(jm,structure=np.ones((3,3)))
cluster={}
for p in key:
    l=jl[p]; cluster.setdefault(l,[]).append(p)
cid={}
for l,ps in cluster.items():
    c=tuple(np.round(np.mean(ps,axis=0)).astype(int)); i=len(nodes); nodes.append(c)
    for p in ps: cid[p]=i
edges=[]; visited=set()
for p in key:
    for q in nb(*p):
        if q in keyset:
            if cid[p]!=cid[q]: edges.append((cid[p],cid[q],[]))
            continue
        if (p,q) in visited: continue
        path=[q]; prev=p; cur=q
        while True:
            ns=[x for x in nb(*cur) if x!=prev]
            nk=[x for x in ns if x in keyset]
            if nk: end=nk[0]; break
            nn=[x for x in ns if x not in path]
            if not nn: end=None; break
            prev=cur; cur=nn[0]; path.append(cur)
        if end is None: continue
        visited.add((end,path[-1]))
        visited.add((p,q))
        edges.append((cid[p],cid[end],path))
# basitleştir: kenar boyunca ~25 m'de ara düğüm
G={'nodes':[],'edges':[]}
def world(p): r,c=p; return [float(x0+c*R), float(hgt[r,c]), float(z0+r*R)]
def lane(p): return float(np.clip(dist[p]/2.0,1.6,5.0))
for p in nodes: G['nodes'].append(world(p)+[lane(p)])
seen=set()
for a,b,path in edges:
    if a==b and len(path)<20: continue
    k=(min(a,b),max(a,b),len(path))
    if k in seen: continue
    seen.add(k)
    step=max(1,int(25/R)); prev=a
    pts=path[step:-step:step] if len(path)>2*step else []
    for p in pts:
        i=len(G['nodes']); G['nodes'].append(world(p)+[lane(p)]); G['edges'].append([prev,i]); prev=i
    if prev!=b: G['edges'].append([prev,b])
# küçük bağlı bileşenleri at
N=len(G['nodes']); adj=[[] for _ in range(N)]
for a,b in G['edges']: adj[a].append(b); adj[b].append(a)
comp=[-1]*N; best=None
for s in range(N):
    if comp[s]>=0: continue
    st_=[s]; comp[s]=s; mem=[s]
    while st_:
        u=st_.pop()
        for w in adj[u]:
            if comp[w]<0: comp[w]=s; st_.append(w); mem.append(w)
    if best is None or len(mem)>len(best): best=mem
keep=sorted(best); remap={o:i for i,o in enumerate(keep)}
G['nodes']=[G['nodes'][i] for i in keep]
G['edges']=[[remap[a],remap[b]] for a,b in G['edges'] if a in remap and b in remap and a!=b]
print('graph',len(G['nodes']),'nodes',len(G['edges']),'edges')
np.savez_compressed('raster.npz',mask=mask,hgt=hgt,x0=x0,z0=z0,R=R)
G['lots']=[]
for k,t in lots:
    v=t.reshape(-1,3); c=v.mean(0); s=v.max(0)-v.min(0)
    G['lots'].append([float(c[0]),float(c[1]),float(c[2]),float(s[0]),float(s[2])])
json.dump(G,open('graph_raw.json','w'))
