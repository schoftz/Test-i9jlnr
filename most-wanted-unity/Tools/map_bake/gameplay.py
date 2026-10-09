import json, numpy as np, heapq, random
G=json.load(open('graph_raw.json')); n=np.array(G['nodes']); N=len(n)
adj=[[] for _ in range(N)]
for a,b in G['edges']:
    w=float(np.linalg.norm(n[a,[0,2]]-n[b,[0,2]])); adj[a].append((b,w)); adj[b].append((a,w))
def dij(s,t,ban=set()):
    D=[1e18]*N; P=[-1]*N; D[s]=0; h=[(0,s)]
    while h:
        d,u=heapq.heappop(h)
        if u==t: break
        if d>D[u]: continue
        for v,w in adj[u]:
            if (min(u,v),max(u,v)) in ban: continue
            if d+w<D[v]: D[v]=d+w; P[v]=u; heapq.heappush(h,(D[v],v))
    if D[t]>=1e18: return None,1e18
    p=[t]
    while p[-1]!=s: p.append(P[p[-1]])
    return p[::-1],D[t]
def near(x,z):
    return int(np.argmin((n[:,0]-x)**2+(n[:,2]-z)**2))
deg=np.array([len(a) for a in adj])
c=n[:,[0,2]].mean(0)
cand=[i for i in range(N) if deg[i]>=3]
gi=min(cand,key=lambda i:np.hypot(*(n[i,[0,2]]-[0,-150])))
gnb=adj[gi][0][0]
out={'version':1,'source':'city_3d_model.glb','scale':100.0,'mirrorX':True,
 'nodes':[[round(v,2) for v in p] for p in G['nodes']],'edges':G['edges'],
 'garage':[round(float(v),2) for v in n[gi,:3]],'garageDir':[round(float(v),3) for v in (n[gnb,[0,2]]-n[gi,[0,2]])/np.linalg.norm(n[gnb,[0,2]]-n[gi,[0,2]])]}
def route(name,typ,pts,prize,laps=1,special=None):
    return {'name':name,'type':typ,'laps':laps,'prize':prize,'nodes':[int(i) for i in pts],'special':special or []}
races=[]
def sprint(name,a,b,prize):
    p,d=dij(near(*a),near(*b)); print(name,'len',int(d)); return p
p=sprint('Merkez Sprinti',(-1000,-140),(1550,-140),4000); races.append(route('Bulvar Sprinti',0,p,4000))
p=sprint('Kuzey',(-700,980),(700,-1000),5000); races.append(route('Kuzey-Güney Sprinti',0,p,5000))
# halka/tur: güney döngüsü (x~110 düz yol + batı kıvrımı)
a=near(110,-1480); b=near(-760,-2100)
p1,d1=dij(a,b); ban={(min(u,v),max(u,v)) for u,v in zip(p1,p1[1:])}
p2,d2=dij(b,a,ban); print('loop',int(d1),int(d2))
loop=p1+p2[1:-1]; races.append(route('Güney Halka Turu',1,loop,6000,laps=2))
# şehir içi tur: halka yollar
a=near(-480,250); b=near(480,250); p1,_=dij(a,b); ban={(min(u,v),max(u,v)) for u,v in zip(p1,p1[1:])}
p2,_=dij(b,a,ban); races.append(route('Merkez Turu',1,p1+p2[1:-1],4500,laps=2))
# hız kamerası
p,_=dij(near(-1100,-300),near(1450,-550)); races.append(route('Radar Avı',2,p,5000,special=[len(p)//4,len(p)//2,3*len(p)//4]))
# gişe
p,_=dij(near(-200,1150),near(110,-2380)); races.append(route('Gişe Koşusu',3,p,5500,special=list(range(12,len(p)-1,12))))
# drag: x~110 düz yol
s=near(108,-1520); e=near(108,-2370); p,d=dij(s,e); print('drag',int(d))
races.append({'name':'Liman Yolu Dragı','type':4,'laps':1,'prize':3500,'nodes':[int(s),int(e)],'special':[]})
out['races']=races
# drag şerit genişliği
print('drag lane offset',n[p,3].mean())
# polis noktaları
rnd=random.Random(1); pol=[]
idx=list(range(N)); rnd.shuffle(idx)
for i in idx:
    if all(np.hypot(*(n[i,[0,2]]-n[j,[0,2]]))>180 for j in pol): pol.append(i)
pol=pol[:40]; out['police']=pol
# saklanma: çıkmaz sokaklar (ara sokak) — birbirinden uzak 12 nokta
hid=[]
ends=[i for i in range(N) if deg[i]==1 and np.hypot(*(n[i,[0,2]]-[0,-150]))<1300]
rnd.shuffle(ends)
for i in ends:
    if all(np.hypot(n[i,0]-h[0],n[i,2]-h[2])>350 for h in hid): hid.append([round(float(n[i,0]),1),round(float(n[i,1])+4,1),round(float(n[i,2]),1),30.0,30.0])
    if len(hid)>=12: break
out['hiding']=hid
out['labels']=[['Merkez',0,-150],['Güney Halka',-400,-2000],['Liman Yolu',110,-1900],['Kuzey',-300,900],['Doğu',1300,-400]]
json.dump(out,open('city_roadgraph.json','w'),separators=(',',':'))
import os; print('nodes',N,'hiding',len(out['hiding']),'police',len(pol),'size',os.path.getsize('city_roadgraph.json'))
# plot için düğüm koordinatlı kopya
P=dict(out); P['races']=[dict(r,route=[n[i,:3].tolist() for i in r['nodes']]) for r in races]
P['garage']=out['garage']; P['police']=[n[i,:3].tolist() for i in pol]
json.dump(P,open('plot.json','w'))
