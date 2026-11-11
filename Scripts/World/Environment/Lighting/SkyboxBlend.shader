// Used by SkyboxCycle: shows two 360 sky images and fades smoothly from one to the other.
Shader "Skybox/Panoramic Blend"
{
    Properties
    {
        _CloudNoise ("Moving cloud noise", 2D) = "gray" {}
        [HideInInspector] _CloudShape ("Cloud shape", Vector) = (0,.64,.18,.1)
        [HideInInspector] _CloudMotion ("Cloud phase", Vector) = (0,0,0,0)
        [HideInInspector] _CloudWind ("Cloud wind", Vector) = (1,.3,.00075,0)
        [HideInInspector] _CloudLight ("Cloud light", Color) = (.8,.85,.9,1)
        [HideInInspector] _CloudShade ("Cloud shadow", Color) = (.3,.4,.5,1)
        [HideInInspector] _CloudSun ("Light direction", Vector) = (0,1,0,1)
        [HideInInspector] _CloudHaze ("Horizon haze", Color) = (.3,.4,.5,1)
        [HideInInspector] _PhotoHorizonA ("Photo horizon A", Float) = .5
        [HideInInspector] _PhotoHorizonB ("Photo horizon B", Float) = .5
        [HideInInspector] _SkyDepth ("Atmospheric horizon", Vector) = (.18,1.12,1,0)
        _TexA ("Sky A (HDR)", 2D) = "grey" {}
        _TexB ("Sky B (HDR)", 2D) = "grey" {}
        _Blend ("Blend A -> B", Range(0, 1)) = 0
        [Gamma] _ExposureA ("Exposure A", Float) = 1
        [Gamma] _ExposureB ("Exposure B", Float) = 1
        [Gamma] _TintA ("Tint A", Color) = (0.5, 0.5, 0.5, 0.5)
        [Gamma] _TintB ("Tint B", Color) = (0.5, 0.5, 0.5, 0.5)
        _RotationA ("Rotation A", Range(0, 360)) = 0
        _RotationB ("Rotation B", Range(0, 360)) = 0
        _Brightness ("Brightness", Float) = 1
        _SeamBlend ("Seam Smoothing (0 = off)", Range(0, 0.25)) = 0.06
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _CloudNoise;
            float4 _CloudShape, _CloudWind, _CloudSun, _CloudMotion;
            half4 _CloudLight, _CloudShade, _CloudHaze;
            sampler2D _TexA;
            sampler2D _TexB;
            half4 _TexA_HDR;
            half4 _TexB_HDR;
            half _Blend;
            half _ExposureA;
            half _ExposureB;
            half4 _TintA;
            half4 _TintB;
            float _RotationA;
            float _RotationB;
            half _Brightness;
            float _SeamBlend;
            float _PhotoHorizonA, _PhotoHorizonB;
            float4 _SkyDepth;
            // Transient globals; never modify the sky assets or saved exposure.
            float4 _MossbloodLightningFlash, _MossbloodLightningSource;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float3 RotateY (float3 d, float degrees)
            {
                float a = degrees * UNITY_PI / 180.0;
                float s, c;
                sincos(a, s, c);
                return float3(c * d.x - s * d.z, d.y, s * d.x + c * d.z);
            }

            // Same mapping as Unity's Skybox/Panoramic (Latitude Longitude, 360 degrees)
            float2 ToRadialCoords (float3 d)
            {
                d = normalize(d);
                float latitude = acos(d.y);
                float longitude = atan2(d.z, d.x);
                float2 sphereCoords = float2(longitude, latitude) * float2(0.5 / UNITY_PI, 1.0 / UNITY_PI);
                return float2(0.5, 1.0) - sphereCoords;
            }

            half3 SampleSky (sampler2D tex, half4 hdr, float3 dir, float rotation, half4 tint, half exposure, float photoHorizon)
            {
                float3 ray = normalize(dir);
                float2 uv = ToRadialCoords(RotateY(ray, rotation));
                // These photographs are not calibrated equirectangular panoramas. Place their
                // actual distant horizon at world elevation zero; use only the sky above it.
                // The lower hemisphere stays at the horizon, avoiding the photographed mirror.
                float elevation = saturate((uv.y - .5) * 2.0);
                uv.y = lerp(1.0 - clamp(photoHorizon,.05,.98), 1.0, pow(elevation, _SkyDepth.y));
                half4 c = tex2Dlod(tex, float4(uv, 0, 0)); // mip 0: no thin line at the back

                // Images whose left and right edges don't match leave a hard vertical line.
                // Near that line, mix in the mirrored image so both sides meet at the same color.
                float seamDist = min(uv.x, 1.0 - uv.x);
                if (_SeamBlend > 0.0001 && seamDist < _SeamBlend)
                {
                    half4 mirrored = tex2Dlod(tex, float4(1.0 - uv.x, uv.y, 0, 0));
                    float t = 1.0 - seamDist / _SeamBlend;          // 1 on the line, 0 at the edge of the zone
                    float k = 0.5 * t * t * (3.0 - 2.0 * t);        // exactly 50/50 on the line
                    c = lerp(c, mirrored, k);
                }

                half3 col = DecodeHDR(c, hdr);
                col = col * tint.rgb * unity_ColorSpaceDouble.rgb;
                return col * exposure;
            }

            half4 frag (v2f i) : SV_Target
            {
                half3 a = SampleSky(_TexA, _TexA_HDR, i.dir, _RotationA, _TintA, _ExposureA, _PhotoHorizonA);
                half3 col = a;
                if (_Blend > 0.001)
                {
                    half3 b = SampleSky(_TexB, _TexB_HDR, i.dir, _RotationB, _TintB, _ExposureB, _PhotoHorizonB);
                    col = lerp(a, b, _Blend);
                }
                if (_CloudWind.w > .5)
                {
                    float3 ray=normalize(i.dir);
                    // Compact dome projection keeps distant clouds from stretching into horizon ribbons.
                    float2 plane=ray.xz / max(.45, ray.y+.35);
                    float2 wind=_CloudMotion.xy+_CloudWind.xy*(_Time.y-_CloudMotion.z)*_CloudWind.z;
                    float2 parallax=_WorldSpaceCameraPos.xz*.000025;
                    float2 uv=plane*(_CloudShape.z*.70)+parallax;
                    float2 bodyUV=uv-wind;
                    // Broad connected lobes dominate. Detail only softens their edges; no domain folding.
                    half broad=tex2D(_CloudNoise,bodyUV).r;
                    half shoulder=tex2D(_CloudNoise,bodyUV*1.31+float2(.37,.19)).g;
                    half fine=tex2D(_CloudNoise,bodyUV*3.1+float2(.53,.19)).r;
                    half field=broad*.88+shoulder*.12;
                    half edgeDetail=(fine-.5)*.045;
                    half mass=smoothstep(_CloudShape.y-.10,_CloudShape.y+.13,field+edgeDetail);
                    half cloud=1-exp(-mass*2.4);
                    half aboveHorizon=smoothstep(.035,.20,ray.y);
                    float3 lightDir=normalize(_CloudSun.xyz);
                    float2 lightOffset=lightDir.xz*.12/max(.3,lightDir.y);
                    half towardLight=tex2D(_CloudNoise,bodyUV+lightOffset).r;
                    half opticalShadow=exp(-max(0,towardLight-field+.065)*5);
                    half transmission=exp(-mass*1.65);
                    half illumination=saturate(.18+transmission*.52+opticalShadow*.30);
                    half sunward=pow(saturate(dot(ray,lightDir)),9);
                    half rim=sunward*pow(1-mass,2)*cloud*.16*_CloudSun.w;
                    half3 cloudColor=lerp(_CloudShade.rgb,_CloudLight.rgb,illumination)+_CloudLight.rgb*rim;
                    // Harmonise the overlay with the photographed sky instead of introducing a separate blue/white tint.
                    half photoLuma=dot(col,half3(.2126,.7152,.0722));
                    half3 photoNeutral=lerp(photoLuma.xxx,col,.15);
                    // Match neutral photographed clouds, but do not darken a new cloud with the blue sky behind it.
                    half photoMax=max(col.r,max(col.g,col.b)),photoMin=min(col.r,min(col.g,col.b));
                    half photoCloud=saturate(1-(photoMax-photoMin)/max(.001,photoMax)*2.5);
                    cloudColor=lerp(cloudColor,photoNeutral,photoCloud*.35);
                    // One coherent cloud body replaces the old elongated secondary layer.
                    col=lerp(col,cloudColor,cloud*aboveHorizon*_CloudShape.x);

                }
                col *= _Brightness;
                // Distant cloud contrast vanishes into the same atmosphere as the mountains.
                // Independent of the moving-cloud toggle; haze color is already world-exposed.
                float height = max(0.0, normalize(i.dir).y);
                half aerial = exp(-pow(height / max(.01, _SkyDepth.x), 2.0));
                col = lerp(col, _CloudHaze.rgb, aerial * saturate(_SkyDepth.z));
                if(_MossbloodLightningFlash.w>.0001)
                {
                    float3 skyRay=normalize(i.dir);
                    float towardBolt=pow(saturate(dot(skyRay,normalize(_MossbloodLightningSource.xyz))),12.0);
                    float glow=_MossbloodLightningFlash.w*(.28+.72*towardBolt);
                    col=col*(1.0+glow*.45)+_MossbloodLightningFlash.rgb*glow*.28;
                }
                return half4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
