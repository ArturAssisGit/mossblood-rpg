using UnityEngine;

public partial class SkyboxCycle
{
    [Header("Living sky")]
    [Tooltip("Adds drifting cloud layers and soft horizon haze over the existing sky images.")]
    public bool livingSky = true;
    public Texture2D cloudNoise;
    [Range(0f,1f)] public float movingCloudOpacity = .58f;
    [Range(.06f,.5f)] public float movingCloudScale = .18f;
    [Range(0f,.005f)] public float movingCloudSpeed = .00075f;
    [Range(0f,.4f)] public float horizonHaze = .10f;
    float nextNaturalSkyUpdate, lastCloudTime;
    Vector2 cloudOffset, previousCloudVelocity;
    static readonly int SkyDepthId=Shader.PropertyToID("_SkyDepth"), CloudMotionId=Shader.PropertyToID("_CloudMotion"), CloudNoiseId=Shader.PropertyToID("_CloudNoise"), CloudShapeId=Shader.PropertyToID("_CloudShape"),
        CloudWindId=Shader.PropertyToID("_CloudWind"), CloudLightId=Shader.PropertyToID("_CloudLight"),
        CloudShadeId=Shader.PropertyToID("_CloudShade"), CloudSunId=Shader.PropertyToID("_CloudSun"), CloudHazeId=Shader.PropertyToID("_CloudHaze");

    void ApplyNaturalSky(float night)
    {
        if(Time.time<nextNaturalSkyUpdate)return;
        nextNaturalSkyUpdate=Time.time+.2f;
        if(cloudNoise==null)cloudNoise=Resources.Load<Texture2D>("Mossblood/Atmosphere/CloudNoise");
        bool enabled=livingSky && cloudNoise!=null;
        skyMaterial.SetTexture(CloudNoiseId,cloudNoise);
        float cover=Mathf.Clamp01(Cloudiness), rain=Mathf.Clamp01(RainSystem.RainAmount);
        skyMaterial.SetVector(CloudShapeId,new Vector4(enabled?movingCloudOpacity:0,Mathf.Lerp(.56f,.37f,cover),movingCloudScale,horizonHaze));
        Vector3 wind=RainSystem.WindDirection;wind.y=0;
        if(wind.sqrMagnitude<.01f)wind=new Vector3(1,0,.3f);
        wind.Normalize();
        cloudOffset+=previousCloudVelocity*Mathf.Max(0,Time.time-lastCloudTime);
        lastCloudTime=Time.time;
        previousCloudVelocity=new Vector2(wind.x,wind.z)*movingCloudSpeed*(1+rain*.65f);
        skyMaterial.SetVector(CloudMotionId,new Vector4(cloudOffset.x,cloudOffset.y,Time.time,0));
        // Shader time supplies continuous motion; climate uniforms only update five times a second.
        skyMaterial.SetVector(CloudWindId,new Vector4(wind.x,wind.z,movingCloudSpeed*(1+rain*.65f),enabled?1:0));
        Color dayLight=Color.Lerp(new Color(.92f,.94f,.97f),new Color(.69f,.72f,.76f),cover);
        Light sun=RenderSettings.sun;
        if(sun!=null)dayLight=Color.Lerp(dayLight,dayLight*sun.color,.10f*(1-night));
        Color nightLight=new Color(.075f,.105f,.16f);
        Color shade=Color.Lerp(new Color(.63f,.66f,.70f),new Color(.34f,.38f,.43f),cover);
        skyMaterial.SetColor(CloudLightId,Color.Lerp(dayLight,nightLight,night));
        skyMaterial.SetColor(CloudShadeId,Color.Lerp(shade,new Color(.024f,.04f,.07f),night));
        Vector3 light=sun!=null?-sun.transform.forward:Vector3.up;
        skyMaterial.SetVector(CloudSunId,new Vector4(light.x,light.y,light.z,1-night*.6f));
        skyMaterial.SetColor(CloudHazeId,RenderSettings.fogColor);
        skyMaterial.SetVector(SkyDepthId,new Vector4(horizonDepthHeight,1.12f,Mathf.Clamp01(horizonHaze*10f),0));
    }
}
