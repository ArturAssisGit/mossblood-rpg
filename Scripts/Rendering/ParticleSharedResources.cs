using System.Collections.Generic;
using UnityEngine;

// Explicit owner for the tiny process-wide VFX cache; individual emitters never delete shared resources.
public sealed class ParticleSharedResources : MonoBehaviour
{
    static ParticleSharedResources instance;
    [SerializeField] List<Object> owned=new();
    public static void Track(Object resource)
    {
        if(resource==null || !Application.isPlaying)return;
        if(instance==null)instance=FindAnyObjectByType<ParticleSharedResources>();
        if(instance==null)
        {
            var root=new GameObject("Shared particle resource cache"){hideFlags=HideFlags.HideInHierarchy};
            DontDestroyOnLoad(root);instance=root.AddComponent<ParticleSharedResources>();
        }
        if(!instance.owned.Contains(resource))instance.owned.Add(resource);
    }
    void OnDestroy()
    {
        foreach(var resource in owned)
        {
            if(resource==null)continue;
            if(Application.isPlaying)Destroy(resource);else DestroyImmediate(resource);
        }
        owned.Clear();if(instance==this)instance=null;
    }
}
