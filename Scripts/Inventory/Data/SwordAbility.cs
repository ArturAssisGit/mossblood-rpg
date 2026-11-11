using UnityEngine;

[CreateAssetMenu(fileName="SwordArt",menuName="Combat/Sword Ability")]
public sealed class SwordAbility : ScriptableObject
{
    public string displayName;
    public AnimationClip animation;
    [HideInInspector] public string animatorState;
    [Range(.5f,1.5f)] public float playbackSpeed=1f;
    [Tooltip("Contact frames, in normalized clip time. Ordered; each entry is one strike.")]
    public SwordAbilityHit[] hits={new SwordAbilityHit{time=.4f,damage=2f}};
    [Range(1f,1.6f)] public float radiusMultiplier=1.2f;
    [Range(60f,360f)] public float arcDegrees=160f;
    [Tooltip("Leave the final recovery smoothly at this normalized point, after all contacts.")]
    [Range(.65f,.98f)] public float recoveryPoint=.9f;
    public string StateName=>string.IsNullOrEmpty(animatorState)?"Sword Art "+name:animatorState;
    public bool Usable
    {
        get
        {
            if(animation==null || !animation.humanMotion || hits==null || hits.Length<1 || hits.Length>4)return false;
            if(!RuntimeValues.IsFinite(playbackSpeed) || playbackSpeed<.5f || playbackSpeed>1.5f
                || !RuntimeValues.IsFinite(radiusMultiplier) || radiusMultiplier<1 || radiusMultiplier>1.6f
                || !RuntimeValues.IsFinite(arcDegrees) || arcDegrees<60 || arcDegrees>360
                || !RuntimeValues.IsFinite(recoveryPoint) || recoveryPoint<.65f || recoveryPoint>.98f)return false;
            float previous=0;
            foreach(var hit in hits)
            {
                if(!RuntimeValues.IsFinite(hit.time) || !RuntimeValues.IsFinite(hit.damage)
                    || hit.time<=previous || hit.time>=.95f || hit.damage<=0 || hit.damage>4)return false;
                previous=hit.time;
            }
            return true;
        }
    }
}
[System.Serializable]
public struct SwordAbilityHit
{
    [Range(.05f,.94f)]public float time;
    [Tooltip("Multiplier of this sword's normal damage, per strike.")][Range(.1f,4f)]public float damage;
}

// A shared personal reserve: changing swords cannot create charges or erase progress.
public struct SwordAbilityReserve
{
    public const int RequiredHits=8;
    public int Hits{get;private set;}
    public bool Ready=>Hits==RequiredHits;
    public float Fill=>Hits/(float)RequiredHits;
    public void AddHit(){Hits=Mathf.Min(RequiredHits,Hits+1);}
    public bool Spend(){if(!Ready)return false;Hits=0;return true;}
    // Old saves stored 0..3 kills; preserve their fill percentage on the new 0..8 scale.
    public static int LegacyKillsToHits(int kills)=>Mathf.RoundToInt(Mathf.Clamp(kills,0,3)*(RequiredHits/3f));
    public void Restore(int value){Hits=Mathf.Clamp(value,0,RequiredHits);}
}
