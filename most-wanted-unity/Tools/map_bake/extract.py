import sys, numpy as np, pygltflib, struct
g=pygltflib.GLTF2().load(sys.argv[1]); blob=g.binary_blob()
def acc(i):
    a=g.accessors[i]; bv=g.bufferViews[a.bufferView]
    n={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a.type]
    dt={5126:np.float32,5125:np.uint32,5123:np.uint16,5121:np.uint8}[a.componentType]
    off=(bv.byteOffset or 0)+(a.byteOffset or 0)
    stride=bv.byteStride
    item=np.dtype(dt).itemsize*n
    if stride and stride!=item:
        raw=np.frombuffer(blob,np.uint8,count=stride*a.count,offset=off).reshape(a.count,stride)[:,:item].copy()
        return raw.view(dt).reshape(a.count,n)
    return np.frombuffer(blob,dt,count=a.count*n,offset=off).reshape(a.count,n) if n>1 else np.frombuffer(blob,dt,count=a.count,offset=off)
def local(n):
    if n.matrix: return np.array(n.matrix,dtype=np.float64).reshape(4,4).T
    M=np.eye(4)
    if n.scale: M=np.diag(list(n.scale)+[1])@M
    if n.rotation:
        x,y,z,w=n.rotation
        R=np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])
        R4=np.eye(4);R4[:3,:3]=R;M=R4@M
    if n.translation: T=np.eye(4);T[:3,3]=n.translation;M=T@M
    return M
out={}
def walk(i,P,cat):
    n=g.nodes[i]; M=P@local(n)
    if cat is None and i in g.nodes[2].children: cat=n.name
    if n.mesh is not None:
        for p in g.meshes[n.mesh].primitives:
            v=acc(p.attributes.POSITION).astype(np.float64)
            v=(M@np.c_[v,np.ones(len(v))].T).T[:,:3]
            idx=acc(p.indices).astype(np.int64).reshape(-1,3) if p.indices is not None else np.arange(len(v)).reshape(-1,3)
            out.setdefault(cat,[]).append(v[idx])
    for c in n.children or []: walk(c,M,cat)
walk(0,np.eye(4),None)
res={}
for k,v in out.items():
    t=np.concatenate(v); t[:,:,0]*=-1; t*=100.0   # glTFast: X aynalanır (sağ el -> sol el)
    res[k]=t
np.savez_compressed('tris.npz',**{k.replace('.','_'):v for k,v in res.items()})
allv=np.concatenate([v.reshape(-1,3) for v in res.values()])
print('bounds',allv.min(0),allv.max(0))
print([ (k,len(v)) for k,v in res.items() if not k.startswith(('Roof','Facade','Yard','Parking','Flat'))])
