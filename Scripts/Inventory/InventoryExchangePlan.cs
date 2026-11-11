using System;
using System.Collections.Generic;

public enum InventoryExchangeFailure { None, Invalid, MissingItems, NoSpace }

// Pure planning: nothing is changed unless every cost and the complete output fit.
public static class InventoryExchangePlan
{
    public static InventoryExchangeFailure Build(int[] ids,int[] amounts,IEnumerable<KeyValuePair<int,int>> costs,
        int outputId,int outputAmount,int outputStack,out int[] resultIds,out int[] resultAmounts)
    {
        resultIds=null;resultAmounts=null;
        if(ids==null || amounts==null || ids.Length!=amounts.Length || costs==null || outputAmount<0
            || (outputAmount>0 && (outputId==0 || outputStack<1)))return InventoryExchangeFailure.Invalid;
        var required=new Dictionary<int,int>();
        foreach(var cost in costs)
        {
            if(cost.Key==0 || cost.Value<=0)return InventoryExchangeFailure.Invalid;
            required.TryGetValue(cost.Key,out int old);long total=(long)old+cost.Value;
            if(total>int.MaxValue)return InventoryExchangeFailure.Invalid;required[cost.Key]=(int)total;
        }
        for(int i=0;i<ids.Length;i++)if(amounts[i]<0 || (ids[i]==0 && amounts[i]!=0) || (ids[i]!=0 && amounts[i]==0))return InventoryExchangeFailure.Invalid;
        foreach(var cost in required)
        {
            long available=0;for(int i=0;i<ids.Length;i++)if(ids[i]==cost.Key)available+=amounts[i];
            if(available<cost.Value)return InventoryExchangeFailure.MissingItems;
        }
        var nextIds=(int[])ids.Clone();var nextAmounts=(int[])amounts.Clone();
        foreach(var cost in required)
        {
            int left=cost.Value;
            // Backpack slots come last; consume them first, preserving the normal inventory preference.
            for(int i=ids.Length-1;i>=0 && left>0;i--)
            {
                if(nextIds[i]!=cost.Key)continue;
                int take=Math.Min(left,nextAmounts[i]);left-=take;nextAmounts[i]-=take;
                if(nextAmounts[i]==0)nextIds[i]=0;
            }
        }
        int remaining=outputAmount;
        if(outputStack>1)for(int i=0;i<ids.Length && remaining>0;i++)
        {
            if(nextIds[i]!=outputId)continue;
            int put=(int)Math.Min(remaining,Math.Max(0L,(long)outputStack-nextAmounts[i]));nextAmounts[i]+=put;remaining-=put;
        }
        for(int i=0;i<ids.Length && remaining>0;i++)
        {
            if(nextIds[i]!=0)continue;
            int put=Math.Min(remaining,outputStack);nextIds[i]=outputId;nextAmounts[i]=put;remaining-=put;
        }
        if(remaining>0)return InventoryExchangeFailure.NoSpace;
        resultIds=nextIds;resultAmounts=nextAmounts;return InventoryExchangeFailure.None;
    }
}
