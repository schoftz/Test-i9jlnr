import json,numpy as np,matplotlib; matplotlib.use('Agg'); import matplotlib.pyplot as plt, sys
G=json.load(open(sys.argv[1])); r=np.load('raster.npz')
m=r['mask']; x0,z0,R=float(r['x0']),float(r['z0']),float(r['R'])
fig,ax=plt.subplots(figsize=(14,19))
ax.imshow(m,origin='lower',extent=[x0,x0+m.shape[1]*R,z0,z0+m.shape[0]*R],cmap='Greys',alpha=0.5)
n=np.array(G['nodes'])
for a,b in G['edges']: ax.plot([n[a,0],n[b,0]],[n[a,2],n[b,2]],'b-',lw=0.8)
ax.scatter(n[:,0],n[:,2],s=2,c='r')
cols=['orange','green','magenta','cyan','purple','brown','olive','pink']
for i,rc in enumerate(G.get('races',[])):
    p=np.array(rc['route']); ax.plot(p[:,0],p[:,2],'-',color=cols[i%8],lw=3,alpha=0.7,label=rc['name'])
    for s in rc.get('special',[]): ax.plot(p[s,0],p[s,2],'*',color=cols[i%8],ms=14)
if 'garage' in G: ax.plot(G['garage'][0],G['garage'][2],'gs',ms=14,label='Garaj')
for h in G.get('hiding',[]): ax.add_patch(plt.Rectangle((h[0]-h[3]/2,h[2]-h[4]/2),h[3],h[4],fill=False,ec='blue',lw=2))
for s in G.get('police',[]): ax.plot(s[0],s[2],'k^',ms=6)
if G.get('races'): ax.legend(loc='lower left')
ax.set_aspect('equal'); ax.set_title('city_3d_model yol grafiği (Unity X-aynalı koordinatlar, m)')
plt.savefig(sys.argv[2],dpi=70,bbox_inches='tight')
