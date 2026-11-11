using System;

// Host-side policy. Integer enemies round once per encounter, not once per enemy type.
public static class CoopBalanceRules
{
    public static int Party(int players)=>Math.Max(1,Math.Min(4,players));
    // Normal enemies come in bigger groups AND get a bit tougher; a boss stays alone, so it carries all the extra weight.
    // Total enemy health per player still drops a little with every friend: playing together should feel stronger, never slower.
    static readonly float[] NormalHealth={1f,1.15f,1.35f,1.6f};
    static readonly float[] BossHealth={1f,1.7f,2.4f,3.1f};
    static readonly float[] HitDamage={1f,1.05f,1.12f,1.2f};
    static readonly int[] GroupTenths={10,13,15,17};
    public static float Health(int players)=>Health(players,false);
    public static float Health(int players,bool boss)=>(boss?BossHealth:NormalHealth)[Party(players)-1];
    public static float Damage(int players)=>HitDamage[Party(players)-1];
    public static int Quantity(int count,int players)=>(int)(((long)Math.Max(0,count)*GroupTenths[Party(players)-1]+5)/10);
    public static int GroupQuantity(int count,int previousNormalCount,int players,bool boss)=>boss?Math.Max(0,count):Quantity(previousNormalCount+Math.Max(0,count),players)-Quantity(previousNormalCount,players);
    public static int LootSeed(int deathSeed,ulong owner)
    {
        unchecked
        {
            uint x=(uint)deathSeed^(uint)owner*0x9e3779b9u^(uint)(owner>>32)*0x85ebca6bu;
            x^=x>>16;x*=0x7feb352du;x^=x>>15;x*=0x846ca68bu;x^=x>>16;
            return (int)(x&0x7fffffffu)|1;
        }
    }
}

public static class CoopDifficulty
{
    public static int Players=>MultiplayerSync.Instance!=null?MultiplayerSync.Instance.AuthoritativePartySize:1;
    public static ulong LootOwner=>MultiplayerSync.Instance!=null?MultiplayerSync.Instance.LocalLootOwner:0;
    public static bool IsBoss(UnityEngine.GameObject prefab)
    {
        if(prefab==null)return false;
        var enemy=prefab.GetComponentInChildren<EnemyAI_01>(true);
        return enemy!=null && enemy.isBoss;
    }
}
