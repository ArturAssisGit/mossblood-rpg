using System.Collections.Generic;
using UnityEngine;

// Small readable copies, never Read/Write-enabled production textures. Samples happen only when a patch is placed.
public sealed class GroundMudPalette : System.IDisposable
{
    readonly Dictionary<Texture2D,Texture2D> samples = new Dictionary<Texture2D,Texture2D>();
    Terrain terrain;
    TerrainLayer[] layers;
    static void Release(Texture2D texture)
    {
        if (Application.isPlaying) Object.Destroy(texture); else Object.DestroyImmediate(texture);
    }
    public void Dispose()
    {
        foreach (var copy in samples.Values) if (copy != null) Release(copy);
        samples.Clear(); terrain = null; layers = null;
    }
    Texture2D Readable(Texture2D source)
    {
        if (source == null) return null;
        if (samples.TryGetValue(source,out var ready)) return ready;
        if (samples.Count >= 32) Dispose();
        RenderTexture previous = RenderTexture.active;
        var temporary = RenderTexture.GetTemporary(64,64,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
        Texture2D copy = null;
        try
        {
            Graphics.Blit(source,temporary); RenderTexture.active = temporary;
            copy = new Texture2D(64,64,TextureFormat.RGBA32,false,true) { name = "Mud terrain palette", wrapMode = TextureWrapMode.Repeat };
            copy.ReadPixels(new Rect(0,0,64,64),0,0,false); copy.Apply(false,false);
        }
        catch (UnityException)
        {
            if (copy != null) Release(copy); copy = null;
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(temporary); }
        samples[source] = copy;
        return copy;
    }
    public Color At(Terrain ground, Vector3 position)
    {
        var data = ground.terrainData;
        if (terrain != ground || layers == null) { terrain = ground; layers = data.terrainLayers; }
        Vector3 local = position-ground.transform.position;
        int x = Mathf.Clamp(Mathf.RoundToInt(local.x/data.size.x*(data.alphamapWidth-1)),0,data.alphamapWidth-1);
        int z = Mathf.Clamp(Mathf.RoundToInt(local.z/data.size.z*(data.alphamapHeight-1)),0,data.alphamapHeight-1);
        var weights = data.GetAlphamaps(x,z,1,1);
        Color color = Color.black; float sum = 0;
        // Readable may evict its bounded cache, so keep this call's layer array locally.
        var currentLayers = layers;
        for (int i = 0; i < currentLayers.Length && i < weights.GetLength(2); i++)
        {
            float weight = weights[0,0,i]; var layer = currentLayers[i];
            if (layer == null || weight < .005f) continue;
            var texture = Readable(layer.diffuseTexture);
            Color sample = new Color(.2f,.17f,.115f,1);
            if (texture != null)
            {
                Vector2 size = layer.tileSize, offset = layer.tileOffset;
                float u = Mathf.Repeat((local.x+offset.x)/Mathf.Max(.01f,size.x),1);
                float v = Mathf.Repeat((local.z+offset.y)/Mathf.Max(.01f,size.y),1);
                sample = texture.GetPixelBilinear(u,v);
                Vector4 min = layer.diffuseRemapMin, max = layer.diffuseRemapMax;
                sample = new Color(min.x+sample.r*(max.x-min.x),min.y+sample.g*(max.y-min.y),min.z+sample.b*(max.z-min.z),1);
            }
            color += sample*weight; sum += weight;
        }
        if (sum <= .001f) return new Color(.2f,.17f,.115f,1);
        color /= sum; color.a = 1; return color;
    }
}
