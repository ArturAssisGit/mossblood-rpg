using System;
using System.Collections.Generic;
using UnityEngine;

// Identical pile appearances share resources; borrowed project textures are never destroyed.
internal static class LeafPileResources
{
    readonly struct Key : IEquatable<Key>
    {
        public readonly Texture2D source;readonly int sourceHash;public readonly bool process;public readonly float tolerance;
        public Key(Texture2D source,bool process,float tolerance){this.source=source;sourceHash=source!=null?source.GetHashCode():0;this.process=source!=null&&process;this.tolerance=this.process?tolerance:0;}
        public bool Equals(Key other)=>ReferenceEquals(source,other.source) && process==other.process && tolerance.Equals(other.tolerance);
        public override bool Equals(object value)=>value is Key other && Equals(other);
        public override int GetHashCode()=>unchecked((sourceHash*397^process.GetHashCode())*397^tolerance.GetHashCode());
    }
    sealed class Entry {public Key key;public Material material;public Texture2D texture;public bool ownsTexture;public int users;}
    static readonly Dictionary<Key,Entry> entries=new();
    static float playerSearchAt;static readonly List<GameObject> players=new List<GameObject>(8);
    // Shared by every pile; filled into the same list (no new array every half second = no garbage)
    public static List<GameObject> PlayersThisFrame
    {
        get
        {
            if(Time.unscaledTime>=playerSearchAt){GameObject.FindGameObjectsWithTag("Player",players);playerSearchAt=Time.unscaledTime+.5f;}
            return players;
        }
    }
    internal sealed class Lease : IDisposable
    {
        Entry entry;
        public Material Material=>entry!=null?entry.material:null;
        internal Lease(object value){entry=(Entry)value;}
        public void Dispose()
        {
            if(entry==null)return;
            var value=entry;entry=null;
            if(--value.users>0)return;
            entries.Remove(value.key);Release(value);
        }
    }
    public static Lease Acquire(Texture2D source,bool process,float tolerance)
    {
        var key=new Key(source,process,tolerance);
        if(!entries.TryGetValue(key,out var entry))
        {
            Texture2D texture=source==null?LeafPile.DefaultLeaf():process?ParticleMaterials.RemoveBackground(source,tolerance):source;
            entry=new Entry {key=key,texture=texture,ownsTexture=texture!=source,
                material=ParticleMaterials.Create("Leaf Pile",texture,false,.4f,true,true,true)};
            entries.Add(key,entry);
        }
        entry.users++;return new Lease(entry);
    }
    static void Release(Entry entry)
    {
        if(entry.material!=null)WorldUtil.DestroyOwned(entry.material);
        if(entry.ownsTexture && entry.texture!=null)WorldUtil.DestroyOwned(entry.texture);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        foreach(var entry in entries.Values)Release(entry);entries.Clear();
        players.Clear();playerSearchAt=0;
    }
}
