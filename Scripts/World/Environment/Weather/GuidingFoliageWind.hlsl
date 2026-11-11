#ifndef MOSSBLOOD_GUIDING_FOLIAGE_INCLUDED
#define MOSSBLOOD_GUIDING_FOLIAGE_INCLUDED
// xy = horizontal WORLD direction, z = smooth guide blend, w = weather/guide amplitude.
// A global, never a material property: terrain instances and rotated prefabs share the same world flow.
float4 _MossbloodGuidingFoliage;

void MossbloodGuidingFoliage_float(float3 BaseOS, float3 AnimatedOS, out float3 OutOS)
{
#if defined(SHADERGRAPH_PREVIEW)
    OutOS = AnimatedOS;
#else
    float3 deltaWS = mul((float3x3)GetObjectToWorldMatrix(), AnimatedOS - BaseOS);
    float weight = saturate(_MossbloodGuidingFoliage.z);
    float2 flow = _MossbloodGuidingFoliage.xy;
    flow *= rsqrt(max(dot(flow, flow), 0.000001));
    float2 across = float2(-flow.y, flow.x);
    // Keep the original flutter and root mask; bias bending downwind instead of
    // rotating the UV noise. This remains continuous even after hours of play.
    float amplitude = length(deltaWS.xz);
    float2 guided = flow * amplitude + across * dot(deltaWS.xz, across) * 0.22;
    deltaWS.xz = lerp(deltaWS.xz, guided, weight);
    deltaWS *= max(_MossbloodGuidingFoliage.w, 1.0);
    OutOS = BaseOS + mul((float3x3)GetWorldToObjectMatrix(), deltaWS);
#endif
}

void MossbloodGuidingFoliage_half(half3 BaseOS, half3 AnimatedOS, out half3 OutOS)
{
    float3 position;
    MossbloodGuidingFoliage_float(BaseOS, AnimatedOS, position);
    OutOS = (half3)position;
}
#endif
