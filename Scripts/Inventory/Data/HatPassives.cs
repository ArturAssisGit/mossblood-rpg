using UnityEngine;

// Stable explicit values: item assets, upgrade copies, saves and network item IDs keep the same meaning.
public enum HatPassive
{
    None=0,SlowerHunger=1,SlowerThirst=2,FasterStaminaRecovery=3,SlowerSpiritDrain=4,
    FasterHealthRecovery=5,LowerSprintCost=6,LowerDodgeCost=7,LowerBlockCost=8,StrongerConsumables=9
}

public static class HatPassives
{
    public static float Fraction(PlayerItemData item,HatPassive effect)
        =>item==null?0:HatPassiveRules.Fraction(item.type==ItemType.Helmet,item.hatPassive,effect,item.hatPassivePercent);
    // Read the equipped slot every time: equipping/removing/loading applies immediately and never mutates base stats.
    // The local inventory must not leak its passive onto another player's stats on the host.
    public static float EquippedFraction(PlayerStats owner,HatPassive effect)
    {
        if(owner==null || owner!=PlayerRef.Stats)return 0;
        var gear=PlayerEquipmentManager.Instance;
        return gear!=null && gear.helmetSlotUI!=null?Fraction(gear.helmetSlotUI.itemData,effect):0;
    }
    public static float Reduction(PlayerStats owner,HatPassive effect)=>1f-EquippedFraction(owner,effect);
    public static float Bonus(PlayerStats owner,HatPassive effect)=>1f+EquippedFraction(owner,effect);
    public static string Describe(PlayerItemData item)
    {
        if(item==null || item.type!=ItemType.Helmet || item.hatPassive==HatPassive.None)return "";
        float value=Fraction(item,item.hatPassive)*100f;if(value<=0)return "";
        string percent=value.ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+"%";
        return item.hatPassive switch
        {
            HatPassive.SlowerHunger=>"Frugal appetite: "+percent+" slower hunger drain",
            HatPassive.SlowerThirst=>"Water keeper: "+percent+" slower thirst drain",
            HatPassive.FasterStaminaRecovery=>"Second wind: +"+percent+" stamina regeneration",
            HatPassive.SlowerSpiritDrain=>"Steady spirit: "+percent+" slower spirit drain",
            HatPassive.FasterHealthRecovery=>"Recovery: +"+percent+" health regeneration",
            HatPassive.LowerSprintCost=>"Long stride: "+percent+" less sprint stamina",
            HatPassive.LowerDodgeCost=>"Nimble step: "+percent+" less dodge stamina",
            HatPassive.LowerBlockCost=>"Steady guard: "+percent+" less block stamina",
            HatPassive.StrongerConsumables=>"Resourceful: +"+percent+" food/water restoration and warming duration",
            _=>""
        };
    }
}

public static class HatPassiveRules
{
    public static float Fraction(bool helmet,HatPassive selected,HatPassive requested,float percent)
    {
        if(!helmet || selected!=requested || (uint)((int)selected-1)>=9u
            || float.IsNaN(percent) || float.IsInfinity(percent))return 0;
        return Mathf.Clamp(percent,0,30)*.01f;
    }
}
