#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Makes a scabbard (saya) for every katana in Assets/Art/Items/Weapons/Swords, shaped around that katana's own blade:
// it follows the blade's curve and taper, starts right after the guard (tsuba) and closes past the tip.
// Dark leather body, black lacquered mouth collar (koiguchi) and end cap (kojiri), two braided cord wraps.
// Runs once by itself after compiling; run it again from Mossblood > Setup > Build Katana Scabbards after adding a
// katana or changing a katana's model/offsets. Report: Logs/KatanaScabbards.txt
static class KatanaScabbardBuilder
{
    const string RanKey = "Mossblood.KatanaScabbards.v2";
    const string SwordFolder = "Assets/Art/Items/Weapons/Swords";
    const string Folder = "Assets/Art/Items/Scabbards";
    const int Bins = 96, Around = 24, Stations = 44;
    static StringBuilder report;

    [InitializeOnLoadMethod]
    static void Auto()
    {
        if (EditorPrefs.GetBool(RanKey, false)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) { EditorPrefs.SetBool(RanKey, false); Auto(); return; }
            EditorPrefs.SetBool(RanKey, true);
            Run();
        };
    }

    [MenuItem("Mossblood/Setup/Build Katana Scabbards")]
    static void Run()
    {
        report = new StringBuilder($"Katana scabbards  {System.DateTime.Now}\n\n");
        try
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Art/Items", "Scabbards");
            Material[] materials = Materials();
            foreach (string guid in AssetDatabase.FindAssets("t:PlayerItemData", new[] { SwordFolder }))
            {
                var item = AssetDatabase.LoadAssetAtPath<PlayerItemData>(AssetDatabase.GUIDToAssetPath(guid));
                if (item == null || item.smallSword || item.itemPrefab == null) continue;
                Mesh built = Build(item, out string note);
                report.AppendLine($"{item.name}: {note}");
                if (built == null) continue;
                string path = $"{Folder}/{item.name}_saya.asset";
                Mesh asset = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (asset == null) { AssetDatabase.CreateAsset(built, path); asset = built; }
                else { EditorUtility.CopySerialized(built, asset); Object.DestroyImmediate(built); EditorUtility.SetDirty(asset); }
                item.scabbardMesh = asset;
                item.scabbardMaterials = materials;
                EditorUtility.SetDirty(item);
            }
            AssetDatabase.SaveAssets();
        }
        catch (System.Exception e) { report.AppendLine("ERROR: " + e); Debug.LogError("[Katana Scabbards] " + e); }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/KatanaScabbards.txt", report.ToString());
        Debug.Log("[Katana Scabbards] done - see Logs/KatanaScabbards.txt");
    }

    // ---------- Reading the blade ----------

    static Mesh Build(PlayerItemData item, out string note)
    {
        var points = new List<Vector3>();
        GameObject probe = Object.Instantiate(item.itemPrefab);
        probe.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Matrix4x4 toRoot = probe.transform.worldToLocalMatrix;
            foreach (MeshFilter mf in probe.GetComponentsInChildren<MeshFilter>(true)) Add(points, mf.sharedMesh, toRoot * mf.transform.localToWorldMatrix);
            foreach (SkinnedMeshRenderer s in probe.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Add(points, s.sharedMesh, toRoot * s.transform.localToWorldMatrix);
        }
        finally { Object.DestroyImmediate(probe); }
        if (points.Count < 30) { note = "skipped - no readable mesh in its prefab"; return null; }

        // Real proportions: the katana is worn with Equip Scale (often not the same on every axis)
        Vector3 S = item.equipScale != Vector3.zero ? item.equipScale : item.itemPrefab.transform.localScale;
        S = new Vector3(Mathf.Max(1e-5f, Mathf.Abs(S.x)), Mathf.Max(1e-5f, Mathf.Abs(S.y)), Mathf.Max(1e-5f, Mathf.Abs(S.z)));
        for (int i = 0; i < points.Count; i++) points[i] = Vector3.Scale(points[i], S);

        // Main axis (the length of the sword), the blade's width and its thickness
        Vector3 mean = Vector3.zero;
        foreach (Vector3 p in points) mean += p;
        mean /= points.Count;
        Axes(points, mean, out Vector3 along, out Vector3 width);
        float tMin = float.MaxValue, tMax = float.MinValue;
        foreach (Vector3 p in points) { float t = Vector3.Dot(p - mean, along); tMin = Mathf.Min(tMin, t); tMax = Mathf.Max(tMax, t); }
        float length = tMax - tMin;
        if (length <= 1e-5f) { note = "skipped - empty model"; return null; }

        // Which end is the handle: the hand holds it (the drawn offsets tell where the hand is)
        Matrix4x4 inHand = Matrix4x4.TRS(item.equipPositionOffset, Quaternion.Euler(item.equipRotationOffset), S);
        Vector3 grip = Vector3.Scale(inHand.inverse.MultiplyPoint3x4(Vector3.zero), S);
        float gripT = Vector3.Dot(grip - mean, along);
        bool gripKnown = Mathf.Abs(gripT) > length * 0.08f && Mathf.Abs(gripT) < length * 1.5f;

        Profile prof = Slice(points, mean, along, width, tMin, length);
        if (!gripKnown)
        {
            int widest = prof.WidestBin(0, Bins - 1);
            gripT = widest < Bins / 2 ? -1f : 1f; // the guard sits much nearer the handle end
        }
        if (gripT > 0f)
        {
            along = -along; width = -width; // from the handle to the tip
            prof = Slice(points, mean, along, width, -tMax, length);
            tMin = -tMax;
        }
        Vector3 thick = Vector3.Cross(along, width).normalized;

        // The guard: the broadest slice in the handle half. The scabbard begins right after it.
        int guard = prof.WidestBin(Mathf.RoundToInt(Bins * 0.06f), Mathf.RoundToInt(Bins * 0.5f));
        float bladeWidth = prof.MedianWidth(Mathf.RoundToInt(Bins * 0.55f), Mathf.RoundToInt(Bins * 0.9f));
        int mouthBin;
        string how;
        if (guard >= 0 && prof.HalfU[guard] * 2f > bladeWidth * 1.25f)
        {
            // Past the guard and the blade collar (habaki): the scabbard starts where the bare blade starts
            float medHuB = bladeWidth * 0.5f, medHwB = prof.MedianHalfW(Mathf.RoundToInt(Bins * 0.55f), Mathf.RoundToInt(Bins * 0.9f));
            int limit = Mathf.Min(Bins - 2, guard + Mathf.RoundToInt(Bins * 0.15f));
            mouthBin = guard + 1;
            while (mouthBin < limit && (prof.Count[mouthBin] == 0 || prof.HalfU[mouthBin] > medHuB * 1.25f || prof.HalfW[mouthBin] > medHwB * 1.8f)) mouthBin++;
            how = "guard found";
        }
        else { mouthBin = Mathf.RoundToInt(Bins * 0.27f); how = "no clear guard: started at 27% of the length"; }
        int tipBin = Bins - 1;
        while (tipBin > mouthBin && prof.Count[tipBin] == 0) tipBin--;
        if (tipBin - mouthBin < Bins / 5) { note = "skipped - could not tell the blade from the handle"; return null; }

        float binLen = length / Bins;
        float mouthT = tMin + mouthBin * binLen + binLen * 0.15f;
        float tipT = tMin + (tipBin + 1) * binLen;
        float bladeLen = tipT - mouthT;

        // A close mould of the blade: at every point just its own width and thickness plus a thin wall,
        // following its curve and its taper to the tip (only smoothed, never widened as a whole)
        float medHu = Mathf.Max(1e-5f, bladeWidth * 0.5f);
        float medHw = Mathf.Max(medHu * 0.05f, prof.MedianHalfW(Mathf.RoundToInt(Bins * 0.55f), Mathf.RoundToInt(Bins * 0.9f)));
        float wall = medHu * 0.07f;
        float extra = bladeLen * 0.02f;
        var rings = new List<Ring>();
        var needU = new List<float>(); var needW = new List<float>();
        for (int k = 0; k <= Stations; k++)
        {
            float t = Mathf.Lerp(mouthT, tipT + extra, k / (float)Stations);
            int bin = Mathf.Clamp(Mathf.FloorToInt((t - tMin) / binLen), mouthBin, tipBin);
            prof.Window(bin, 1, mouthBin, tipBin, out float cu, out float cw, out float hu, out float hw);
            float nu = Mathf.Max(hu, medHu * 0.28f) + wall, nw = Mathf.Max(hw, medHw * 0.6f) + wall * 0.8f;
            needU.Add(nu); needW.Add(nw);
            rings.Add(new Ring { t = t, cu = cu, cw = cw, hu = nu * 1.03f, hw = Mathf.Max(nw * 1.15f, nu * 0.34f) });
        }
        // Smooth the outline (no steps from the slices), never below what the blade needs
        var su = new float[rings.Count]; var sw = new float[rings.Count];
        for (int k = 0; k < rings.Count; k++)
        {
            float a = 0f, b = 0f; int n = 0;
            for (int j = Mathf.Max(0, k - 2); j <= Mathf.Min(rings.Count - 1, k + 2); j++) { a += rings[j].hu; b += rings[j].hw; n++; }
            su[k] = a / n; sw[k] = b / n;
        }
        for (int k = 0; k < rings.Count; k++) { Ring r = rings[k]; r.hu = Mathf.Max(su[k], needU[k]); r.hw = Mathf.Max(sw[k], needW[k]); rings[k] = r; }
        SmoothCenters(rings);
        float baseHu = medHu + wall;
        Fit(points, mean, along, width, thick, rings, mouthT, tipT, wall);

        // Make sure no part of the blade pokes through; grow only very slightly and check again if needed
        int outside = 0, grown = 0;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            outside = Outside(points, mean, along, width, thick, rings, mouthT + bladeLen * 0.01f, tipT);
            if (outside == 0) break;
            grown++;
            for (int k = 0; k < rings.Count; k++) { Ring r = rings[k]; r.hu *= 1.03f; r.hw *= 1.03f; rings[k] = r; }
        }

        Mesh mesh = Assemble(rings, mean, along, width, thick, bladeLen, baseHu, S, item.name);
        note = $"{how}, blade {bladeLen / length:P0} of the sword, scabbard {rings[0].hu / medHu:0.00}x the blade's width at the mouth"
            + (grown > 0 ? $", grown {grown}x3% to fit" : "") + (outside > 0 ? $", WARNING: {outside} blade points still outside" : "");
        return mesh;
    }

    static void Add(List<Vector3> points, Mesh mesh, Matrix4x4 m)
    {
        if (mesh == null) return;
        Vector3[] v;
        try { v = mesh.vertices; } catch { return; }
        if (v == null) return;
        for (int i = 0; i < v.Length; i++) points.Add(m.MultiplyPoint3x4(v[i]));
    }

    // Principal axes of the point cloud: 'along' = longest, 'width' = second
    static void Axes(List<Vector3> pts, Vector3 mean, out Vector3 along, out Vector3 width)
    {
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (Vector3 p in pts)
        {
            Vector3 d = p - mean;
            xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
        }
        Vector3 Mul(Vector3 v) => new Vector3((float)(xx * v.x + xy * v.y + xz * v.z), (float)(xy * v.x + yy * v.y + yz * v.z), (float)(xz * v.x + yz * v.y + zz * v.z));
        Vector3 Power(Vector3 start, Vector3 avoid)
        {
            Vector3 v = start.normalized;
            for (int i = 0; i < 64; i++)
            {
                v = Mul(v);
                if (avoid != Vector3.zero) v -= Vector3.Dot(v, avoid) * avoid;
                if (v.sqrMagnitude < 1e-20f) return Vector3.zero;
                v.Normalize();
            }
            return v;
        }
        along = Power(new Vector3(0.577f, 0.578f, 0.576f), Vector3.zero);
        if (along == Vector3.zero) along = Vector3.forward;
        Vector3 seed = Mathf.Abs(along.x) < 0.9f ? Vector3.right : Vector3.up;
        width = Power(seed - Vector3.Dot(seed, along) * along, along);
        if (width == Vector3.zero) width = Vector3.Cross(along, Vector3.up).normalized;
    }

    struct Ring { public float t, cu, cw, hu, hw; }

    sealed class Profile
    {
        public float[] MinU = new float[Bins], MaxU = new float[Bins], MinW = new float[Bins], MaxW = new float[Bins], HalfU = new float[Bins], HalfW = new float[Bins];
        public int[] Count = new int[Bins];
        public float Area(int b) => Count[b] == 0 ? 0f : HalfU[b] * HalfW[b];
        public int WidestBin(int from, int to)
        {
            int best = -1; float area = 0f;
            for (int b = Mathf.Max(0, from); b <= Mathf.Min(Bins - 1, to); b++) if (Area(b) > area) { area = Area(b); best = b; }
            return best;
        }
        public float MedianWidth(int from, int to)
        {
            var list = new List<float>();
            for (int b = from; b <= to; b++) if (Count[b] > 0) list.Add(HalfU[b] * 2f);
            if (list.Count == 0) return 0f;
            list.Sort();
            return list[list.Count / 2];
        }
        public float MedianHalfW(int from, int to)
        {
            var list = new List<float>();
            for (int b = from; b <= to; b++) if (Count[b] > 0) list.Add(HalfW[b]);
            if (list.Count == 0) return 0f;
            list.Sort();
            return list[list.Count / 2];
        }
        public float MaxHalfW(int from, int to)
        {
            float m = 0f;
            for (int b = from; b <= to; b++) if (Count[b] > 0) m = Mathf.Max(m, HalfW[b]);
            return m;
        }
        // Center and the widest half-sizes around a bin (empty bins borrow from their neighbours)
        public void Window(int bin, int reach, int from, int to, out float cu, out float cw, out float hu, out float hw)
        {
            cu = cw = 0f; hu = hw = 0f; int n = 0;
            for (int r = 0; r <= Bins; r++)
            {
                for (int b = Mathf.Max(from, bin - reach - r); b <= Mathf.Min(to, bin + reach + r); b++)
                {
                    if (Count[b] == 0) continue;
                    cu += (MinU[b] + MaxU[b]) * 0.5f; cw += (MinW[b] + MaxW[b]) * 0.5f;
                    hu = Mathf.Max(hu, HalfU[b]); hw = Mathf.Max(hw, HalfW[b]); n++;
                }
                if (n > 0) break;
            }
            if (n > 0) { cu /= n; cw /= n; }
        }
    }

    static Profile Slice(List<Vector3> pts, Vector3 mean, Vector3 along, Vector3 width, float tMin, float length)
    {
        var p = new Profile();
        for (int b = 0; b < Bins; b++) { p.MinU[b] = p.MinW[b] = float.MaxValue; p.MaxU[b] = p.MaxW[b] = float.MinValue; }
        Vector3 thick = Vector3.Cross(along, width).normalized;
        foreach (Vector3 q in pts)
        {
            Vector3 d = q - mean;
            int b = Mathf.Clamp(Mathf.FloorToInt((Vector3.Dot(d, along) - tMin) / length * Bins), 0, Bins - 1);
            float u = Vector3.Dot(d, width), w = Vector3.Dot(d, thick);
            p.MinU[b] = Mathf.Min(p.MinU[b], u); p.MaxU[b] = Mathf.Max(p.MaxU[b], u);
            p.MinW[b] = Mathf.Min(p.MinW[b], w); p.MaxW[b] = Mathf.Max(p.MaxW[b], w);
            p.Count[b]++;
        }
        for (int b = 0; b < Bins; b++)
            if (p.Count[b] > 0) { p.HalfU[b] = (p.MaxU[b] - p.MinU[b]) * 0.5f; p.HalfW[b] = (p.MaxW[b] - p.MinW[b]) * 0.5f; }
        return p;
    }

    static void SmoothCenters(List<Ring> rings)
    {
        var cu = new float[rings.Count]; var cw = new float[rings.Count];
        for (int k = 0; k < rings.Count; k++)
        {
            float su = 0f, sw = 0f; int n = 0;
            for (int j = Mathf.Max(0, k - 3); j <= Mathf.Min(rings.Count - 1, k + 3); j++) { su += rings[j].cu; sw += rings[j].cw; n++; }
            cu[k] = su / n; cw[k] = sw / n;
        }
        for (int k = 0; k < rings.Count; k++) { Ring r = rings[k]; r.cu = cu[k]; r.cw = cw[k]; rings[k] = r; }
    }

    const float Squareness = 3.2f; // a slightly squared oval, like a real saya

    static int Outside(List<Vector3> pts, Vector3 mean, Vector3 along, Vector3 width, Vector3 thick, List<Ring> rings, float from, float to)
    {
        int count = 0;
        foreach (Vector3 q in pts)
        {
            Vector3 d = q - mean;
            float t = Vector3.Dot(d, along);
            if (t < from || t > to) continue;
            Ring r = At(rings, t);
            float x = Mathf.Abs(Vector3.Dot(d, width) - r.cu) / r.hu, y = Mathf.Abs(Vector3.Dot(d, thick) - r.cw) / r.hw;
            if (Mathf.Pow(x, Squareness) + Mathf.Pow(y, Squareness) > 1.0f) count++;
        }
        return count;
    }

    // Makes every ring just big enough for the blade points around it: first the width (the edge and the spine),
    // then the thickness that the spine's corners need at that width. A thin wall is left all around.
    static void Fit(List<Vector3> pts, Vector3 mean, Vector3 along, Vector3 width, Vector3 thick, List<Ring> rings, float from, float to, float wall)
    {
        int n = rings.Count;
        var needU = new float[n]; var needW = new float[n];
        for (int k = 0; k < n; k++) { needU[k] = rings[k].hu * 0.55f; needW[k] = rings[k].hw * 0.55f; } // only a floor (the very tip); the blade's own points decide the rest
        const float edgeRoom = 0.96f; // the blade may reach 96% of the half width

        void Each(System.Action<int, float, float> use)
        {
            foreach (Vector3 q in pts)
            {
                Vector3 d = q - mean;
                float t = Vector3.Dot(d, along);
                if (t < from || t > to) continue;
                int k1 = 1;
                while (k1 < n - 1 && rings[k1].t < t) k1++;
                int k0 = k1 - 1;
                foreach (int k in new[] { k0, k1 })
                {
                    float du = Mathf.Abs(Vector3.Dot(d, width) - rings[k].cu), dw = Mathf.Abs(Vector3.Dot(d, thick) - rings[k].cw);
                    use(k, du, dw);
                }
            }
        }
        // Width
        Each((k, du, dw) => needU[k] = Mathf.Max(needU[k], (du + wall * 0.5f) / edgeRoom));
        for (int k = 0; k < n; k++) { Ring r = rings[k]; r.hu = needU[k]; rings[k] = r; }
        // Thickness at that width
        Each((k, du, dw) =>
        {
            float x = Mathf.Min(edgeRoom, du / rings[k].hu);
            float room = Mathf.Pow(Mathf.Max(0.02f, 1f - Mathf.Pow(x, Squareness)), 1f / Squareness);
            needW[k] = Mathf.Max(needW[k], (dw + wall * 0.4f) / room);
        });
        // Smooth outline that still holds everything: a running max over neighbours, then a soft average on top of it
        var hu = new float[n]; var hw = new float[n];
        for (int k = 0; k < n; k++)
        {
            float a = 0f, b = 0f;
            for (int j = Mathf.Max(0, k - 1); j <= Mathf.Min(n - 1, k + 1); j++) { a = Mathf.Max(a, needU[j]); b = Mathf.Max(b, needW[j]); }
            hu[k] = a; hw[k] = b;
        }
        for (int k = 0; k < n; k++)
        {
            float a = 0f, b = 0f; int c = 0;
            for (int j = Mathf.Max(0, k - 1); j <= Mathf.Min(n - 1, k + 1); j++) { a += hu[j]; b += hw[j]; c++; }
            Ring r = rings[k]; r.hu = Mathf.Max(a / c, needU[k]); r.hw = Mathf.Max(b / c, needW[k]); rings[k] = r;
        }
    }

    static Ring At(List<Ring> rings, float t)
    {
        if (t <= rings[0].t) return rings[0];
        for (int k = 1; k < rings.Count; k++)
            if (t <= rings[k].t)
            {
                float s = Mathf.InverseLerp(rings[k - 1].t, rings[k].t, t);
                Ring a = rings[k - 1], b = rings[k];
                return new Ring { t = t, cu = Mathf.Lerp(a.cu, b.cu, s), cw = Mathf.Lerp(a.cw, b.cw, s), hu = Mathf.Lerp(a.hu, b.hu, s), hw = Mathf.Lerp(a.hw, b.hw, s) };
            }
        return rings[rings.Count - 1];
    }

    // ---------- Building the scabbard ----------

    sealed class Builder
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int>[] tris = { new List<int>(), new List<int>(), new List<int>() }; // leather, lacquer, cord
        public Vector3 mean, along, width, thick;
        public float uvScale;

        Vector3 Point(Ring r, float scale, float angle)
        {
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            float x = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / Squareness), y = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2f / Squareness);
            return mean + along * r.t + width * (r.cu + x * r.hu * scale) + thick * (r.cw + y * r.hw * scale);
        }

        int RingVerts(Ring r, float scale, float vCoord)
        {
            int start = v.Count;
            for (int j = 0; j <= Around; j++)
            {
                v.Add(Point(r, scale, j / (float)Around * Mathf.PI * 2f));
                uv.Add(new Vector2(j / (float)Around, vCoord));
            }
            return start;
        }

        // A tube through the rings (outside facing out; inward = true for the inside of the mouth)
        public void Tube(List<Ring> rings, List<float> scales, int sub, bool inward = false)
        {
            int prev = -1;
            for (int k = 0; k < rings.Count; k++)
            {
                int cur = RingVerts(rings[k], scales[k], (rings[k].t - rings[0].t) * uvScale);
                if (prev >= 0)
                    for (int j = 0; j < Around; j++)
                    {
                        int a = prev + j, b = prev + j + 1, c = cur + j, d = cur + j + 1;
                        if (!inward) { tris[sub].Add(a); tris[sub].Add(b); tris[sub].Add(c); tris[sub].Add(b); tris[sub].Add(d); tris[sub].Add(c); }
                        else { tris[sub].Add(a); tris[sub].Add(c); tris[sub].Add(b); tris[sub].Add(b); tris[sub].Add(c); tris[sub].Add(d); }
                    }
                prev = cur;
            }
        }

        // A flat ring between two sizes at one station, facing back (toward the handle) or forward (toward the tip)
        public void Annulus(Ring r, float inner, float outer, int sub, bool facingHandle)
        {
            int a0 = RingVerts(r, inner, 0f), b0 = RingVerts(r, outer, 1f);
            for (int j = 0; j < Around; j++)
            {
                int a = a0 + j, b = a0 + j + 1, c = b0 + j, d = b0 + j + 1;
                if (facingHandle) { tris[sub].Add(a); tris[sub].Add(b); tris[sub].Add(c); tris[sub].Add(b); tris[sub].Add(d); tris[sub].Add(c); }
                else { tris[sub].Add(a); tris[sub].Add(c); tris[sub].Add(b); tris[sub].Add(b); tris[sub].Add(c); tris[sub].Add(d); }
            }
        }

        // Closes a ring with a point (the very end of the end cap)
        public void Cap(Ring r, float scale, Vector3 tip, int sub)
        {
            int ring = RingVerts(r, scale, 0f);
            int centre = v.Count; v.Add(tip); uv.Add(new Vector2(0.5f, 0.5f));
            for (int j = 0; j < Around; j++) { tris[sub].Add(ring + j); tris[sub].Add(ring + j + 1); tris[sub].Add(centre); }
        }
    }

    static Mesh Assemble(List<Ring> rings, Vector3 mean, Vector3 along, Vector3 width, Vector3 thick, float bladeLen, float baseHu, Vector3 S, string name)
    {
        var b = new Builder { mean = mean, along = along, width = width, thick = thick, uvScale = 1f / Mathf.Max(1e-5f, baseHu * 4f) };
        float mouth = rings[0].t, end = rings[rings.Count - 1].t;

        List<Ring> Span(float from, float to, int steps)
        {
            var list = new List<Ring>();
            for (int i = 0; i <= steps; i++) list.Add(At(rings, Mathf.Lerp(from, to, i / (float)steps)));
            return list;
        }
        List<float> Same(int n, float s) { var l = new List<float>(); for (int i = 0; i < n; i++) l.Add(s); return l; }

        // Leather body (ends inside the end cap)
        float bodyEnd = end - bladeLen * 0.05f;
        var body = new List<Ring>();
        foreach (Ring r in rings) if (r.t < bodyEnd) body.Add(r);
        body.Add(At(rings, bodyEnd));
        b.Tube(body, Same(body.Count, 1f), 0);

        // Mouth collar (koiguchi): a lacquered sleeve, its front face around the opening, and a short dark throat inside
        float collarEnd = mouth + bladeLen * 0.045f;
        Ring m0 = At(rings, mouth);
        var collar = Span(mouth, collarEnd, 3);
        b.Tube(collar, Same(collar.Count, 1.07f), 1);
        b.Annulus(m0, 0.68f, 1.07f, 1, true);
        b.Annulus(collar[collar.Count - 1], 1f, 1.07f, 1, false);
        var throat = Span(mouth, mouth + bladeLen * 0.03f, 2);
        b.Tube(throat, Same(throat.Count, 0.68f), 1, true);

        // Two braided cord wraps (sageo) below the mouth
        foreach (float at in new[] { 0.09f, 0.135f })
        {
            float from = mouth + bladeLen * at, to = from + bladeLen * 0.02f;
            var band = Span(from, to, 2);
            b.Tube(band, Same(band.Count, 1.04f), 2);
            b.Annulus(band[0], 1f, 1.04f, 2, true);
            b.Annulus(band[band.Count - 1], 1f, 1.04f, 2, false);
        }

        // Lacquered end cap (kojiri), rounded at the end
        float capFrom = end - bladeLen * 0.065f;
        var cap = Span(capFrom, end, 3);
        var capScales = Same(cap.Count, 1.06f);
        b.Annulus(cap[0], 1f, 1.06f, 1, true);
        Ring last = rings[rings.Count - 1];
        float dome = Mathf.Max(last.hu, last.hw) * 0.9f;
        foreach (float f in new[] { 0.35f, 0.7f, 0.92f })
        {
            Ring r = last; r.t = end + dome * f;
            cap.Add(r);
            capScales.Add(1.06f * Mathf.Sqrt(Mathf.Max(0.02f, 1f - f * f)));
        }
        b.Tube(cap, capScales, 1);
        Ring tipRing = cap[cap.Count - 1];
        b.Cap(tipRing, capScales[capScales.Count - 1], mean + along * (end + dome) + width * last.cu + thick * last.cw, 1);

        // Back into the katana's own (unscaled) space, so the scabbard takes the very same transform as the sword
        var verts = new Vector3[b.v.Count];
        for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3(b.v[i].x / S.x, b.v[i].y / S.y, b.v[i].z / S.z);
        var mesh = new Mesh { name = name + " saya" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, b.uv);
        mesh.subMeshCount = 3;
        for (int s = 0; s < 3; s++) mesh.SetTriangles(b.tris[s], s);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        mesh.UploadMeshData(false);
        return mesh;
    }

    // ---------- Materials ----------

    static Material[] Materials()
    {
        Texture2D leather = TextureAsset("saya_leather_grain", 256, (x, y) =>
        {
            float n = Mathf.PerlinNoise(x * 0.09f, y * 0.09f) * 0.55f + Mathf.PerlinNoise(x * 0.37f + 11f, y * 0.37f + 7f) * 0.3f
                + Mathf.PerlinNoise(x * 1.3f + 3f, y * 1.3f + 5f) * 0.15f;
            float crease = Mathf.Abs(Mathf.PerlinNoise(x * 0.05f + 40f, y * 0.2f) - 0.5f) < 0.02f ? 0.82f : 1f;
            float k = Mathf.Lerp(0.78f, 1.08f, n) * crease;
            return new Color(k, k, k, 1f);
        });
        Texture2D cord = TextureAsset("saya_cord_braid", 64, (x, y) =>
        {
            float a = Mathf.Repeat((x + y) / 8f, 1f), b = Mathf.Repeat((x - y) / 8f, 1f);
            float weave = (Mathf.Floor((x + y) / 8f) + Mathf.Floor((x - y + 64) / 8f)) % 2 == 0 ? a : b;
            float k = Mathf.Lerp(0.62f, 1.05f, Mathf.Sin(weave * Mathf.PI));
            return new Color(k, k, k, 1f);
        });
        return new[]
        {
            MaterialAsset("saya_leather", leather, new Color(0.24f, 0.13f, 0.07f), 0.32f, 0f, new Vector2(1f, 1f)),
            MaterialAsset("saya_lacquer", null, new Color(0.045f, 0.035f, 0.03f), 0.72f, 0.05f, Vector2.one),
            MaterialAsset("saya_cord", cord, new Color(0.16f, 0.12f, 0.3f), 0.12f, 0f, new Vector2(3f, 1f)),
        };
    }

    static Texture2D TextureAsset(string name, int size, System.Func<int, int, Color> pixel)
    {
        string path = $"{Folder}/{name}.asset";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex != null) return tex;
        tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) px[y * size + x] = pixel(x, y);
        tex.SetPixels(px);
        tex.Apply(true, false);
        AssetDatabase.CreateAsset(tex, path);
        return tex;
    }

    static Material MaterialAsset(string name, Texture2D tex, Color color, float smoothness, float metallic, Vector2 tiling)
    {
        string path = $"{Folder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat; // keep any change you made to it
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        mat = new Material(shader) { name = name };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (tex != null)
        {
            if (mat.HasProperty("_BaseMap")) { mat.SetTexture("_BaseMap", tex); mat.SetTextureScale("_BaseMap", tiling); }
            if (mat.HasProperty("_MainTex")) { mat.SetTexture("_MainTex", tex); mat.SetTextureScale("_MainTex", tiling); }
        }
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
        mat.enableInstancing = true;
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
#endif
