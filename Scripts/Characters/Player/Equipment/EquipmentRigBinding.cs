using System.Collections.Generic;
using UnityEngine;

// The original body skin is the source of truth. Never search a scene root containing other rigs or gear copies.
public static class EquipmentRigBinding
{
    static Transform Root(SkinnedMeshRenderer body, Transform owner)
    {
        if (body == null || owner == null) return null;
        if (body.rootBone != null && body.rootBone.IsChildOf(owner)) return body.rootBone;
        foreach (var bone in body.bones) if (bone != null && bone.IsChildOf(owner)) return bone;
        return null;
    }
    public static bool IsBound(SkinnedMeshRenderer skin, SkinnedMeshRenderer body, Transform owner, Transform equipment)
    {
        Transform root = Root(body,owner);
        if (skin == null || skin.sharedMesh == null || root == null || skin.rootBone != root) return false;
        foreach (Transform bone in skin.bones)
            if (bone == null || !bone.IsChildOf(owner) || bone.IsChildOf(equipment)) return false;
        return true;
    }
    public static bool Bind(SkinnedMeshRenderer skin, SkinnedMeshRenderer body, Transform owner, Transform equipment)
    {
        Transform root = Root(body,owner);
        if (skin == null || skin.sharedMesh == null || root == null) return false;
        var map = new Dictionary<string,Transform>();
        foreach (var bone in body.bones)
            for (Transform t = bone; t != null && t.IsChildOf(owner); t = t.parent)
            {
                if (t.IsChildOf(equipment)) break;
                if (!map.ContainsKey(t.name)) map.Add(t.name,t);
            }
        if (!map.ContainsKey(root.name)) map.Add(root.name,root);
        Transform[] old = skin.bones, bound = new Transform[old.Length];
        for (int i = 0; i < old.Length; i++)
        {
            bound[i] = root;
            for (Transform t = old[i]; t != null; t = t.parent)
                if (map.TryGetValue(t.name,out Transform match)) { bound[i] = match; break; }
        }
        skin.bones = bound; skin.rootBone = root;
        Bounds bounds = skin.localBounds; bounds.Encapsulate(body.localBounds); skin.localBounds = bounds;
        return true;
    }
}
