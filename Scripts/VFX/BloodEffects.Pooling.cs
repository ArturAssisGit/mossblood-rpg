using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class BloodEffects
{
    struct BloodLease { public GameObject root; public float expires; public bool gush; }
    readonly Stack<GameObject> hitPool=new(16), gushPool=new(4);
    readonly Stack<Stain> stainPool=new(60);
    readonly List<BloodLease> bloodLeases=new(24);
    readonly List<ParticleSystem> recycleSystems=new(4);
    RaycastHit[] groundHits=new RaycastHit[32];
    Transform poolRoot;
    Transform PoolRoot()
    {
        if(poolRoot==null)poolRoot=new GameObject("Blood VFX pool").transform;
        return poolRoot;
    }

    GameObject RentBlood(bool gush)
    {
        var pool=gush?gushPool:hitPool;
        GameObject root=null;
        while(pool.Count>0 && root==null)root=pool.Pop();
        if(root==null)root=new GameObject(gush?"Blood Gush":"Blood");
        root.transform.SetParent(PoolRoot(),false);
        root.transform.localScale=Vector3.one;
        root.SetActive(true);
        return root;
    }
    void LeaseBlood(GameObject root,float lifetime,bool gush)
        =>bloodLeases.Add(new BloodLease {root=root,expires=Time.time+lifetime,gush=gush});

    void TickBloodPools()
    {
        for(int i=bloodLeases.Count-1;i>=0;i--)
        {
            var lease=bloodLeases[i];
            if(lease.root!=null && Time.time<lease.expires)continue;
            bloodLeases.RemoveAt(i);
            if(lease.root==null)continue;
            lease.root.GetComponentsInChildren(true,recycleSystems);
            foreach(var ps in recycleSystems)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            lease.root.SetActive(false);
            lease.root.transform.SetParent(PoolRoot(),false);
            var pool=lease.gush?gushPool:hitPool;
            // Limit retained memory, never the simultaneous visible hits/finishers.
            if(pool.Count<(lease.gush?4:16))pool.Push(lease.root);
            else Destroy(lease.root);
        }
    }

    Stain RentStain()
    {
        while(stainPool.Count>0)
        {
            var stain=stainPool.Pop();
            if(stain.go!=null) {stain.go.SetActive(true);return stain;}
        }
        var root=new GameObject("Blood Stain");root.transform.SetParent(PoolRoot(),false);
        root.AddComponent<MeshFilter>().sharedMesh=stainMesh;
        var renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=stainMaterial;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
        return new Stain {go=root,renderer=renderer};
    }

    void ClearBloodWorld(Scene scene,LoadSceneMode mode)
    {
        if(mode!=LoadSceneMode.Single)return;
        ClearGroundSpatter();
        ClearArcPulses();
        ClearBleedingEffects();
        foreach(var lease in bloodLeases)if(lease.root!=null)Destroy(lease.root);
        bloodLeases.Clear();
        while(stains.Count>0)RemoveStain(stains.Count-1);
        if(poolRoot!=null)Destroy(poolRoot.gameObject);poolRoot=null;
        hitPool.Clear();gushPool.Clear();stainPool.Clear();recycleSystems.Clear();
    }

    void OnDisable() => ClearBloodWorld(default,LoadSceneMode.Single);

    void OnDestroy()
    {
        SceneManager.sceneLoaded-=ClearBloodWorld;
        ClearGroundSpatter();
        ClearArcPulses();
        ClearBleedingEffects();
        foreach(var lease in bloodLeases)if(lease.root!=null)Destroy(lease.root);
        bloodLeases.Clear();
        foreach(var stain in stains)if(stain.go!=null)Destroy(stain.go);
        stains.Clear();
        while(hitPool.Count>0){var root=hitPool.Pop();if(root!=null)Destroy(root);}
        while(gushPool.Count>0){var root=gushPool.Pop();if(root!=null)Destroy(root);}
        while(stainPool.Count>0){var stain=stainPool.Pop();if(stain.go!=null)Destroy(stain.go);}
        if(poolRoot!=null)Destroy(poolRoot.gameObject);
        if(Instance!=this)return;
        Instance=null;
        if(dropMaterial!=null)Destroy(dropMaterial);dropMaterial=null;
        if(mistMaterial!=null)Destroy(mistMaterial);mistMaterial=null;
        if(bloodDropTexture!=null)Destroy(bloodDropTexture);bloodDropTexture=null;
        if(stainMaterial!=null)Destroy(stainMaterial);stainMaterial=null;
        if(stainMesh!=null)Destroy(stainMesh);stainMesh=null;
        if(stainTextures!=null)foreach(var texture in stainTextures)if(texture!=null)Destroy(texture);
        stainTextures=null;
    }
}
