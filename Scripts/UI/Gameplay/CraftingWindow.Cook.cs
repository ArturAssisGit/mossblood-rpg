using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Camp window: CAMPFIRE page. Your learned recipes on the left, the chosen one on the right with its ingredients.
// Cook makes one, Cook all as many as your bags allow. Every cook is checked again at the moment you click
// (still at the fire, recipe known and offered by this fire, all ingredients there), so nothing is lost or duplicated.
public partial class CraftingWindow
{
    CraftingRecipe selectedRecipe;
    RectTransform recipeList;
    Image recipeIcon;
    TextMeshProUGUI recipeName, recipeEffect, recipeText, recipeIngredients, recipeEmpty;
    Button cookButton, cookAllButton;
    readonly List<CraftingRecipe> knownRecipes = new List<CraftingRecipe>();

    void BuildCookPage(RectTransform page)
    {
        RectTransform left = Card("Recipes", page, Vector2.zero, new Vector2(0.45f, 1f), Vector2.zero, new Vector2(-12f, 0f));
        Heading(left, "RECIPES", 26f);
        recipeList = ScrollList(left, new Vector2(26f, 26f), new Vector2(-22f, -70f));

        RectTransform right = Card("Recipe", page, new Vector2(0.45f, 0f), Vector2.one, new Vector2(12f, 0f), Vector2.zero);
        recipeIcon = UIKit.CreateImage("Icon", right, Color.white).GetComponent<Image>();
        recipeIcon.preserveAspect = true;
        recipeIcon.raycastTarget = false;
        RectTransform iconRect = recipeIcon.rectTransform;
        iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0f, 1f);
        iconRect.anchoredPosition = new Vector2(36f, -30f);
        iconRect.sizeDelta = new Vector2(112f, 112f);

        recipeName = Text("Name", right, "", 30f, UIStyle.Gold);
        UIKit.SetRect(recipeName.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(166f, -76f), new Vector2(-34f, -30f));
        recipeEffect = Paragraph("Effect", right, "", 18f, UIStyle.Tan);
        UIKit.SetRect(recipeEffect.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(166f, -146f), new Vector2(-34f, -80f));
        recipeText = Paragraph("Description", right, "", 19f, UIStyle.Cream);
        UIKit.SetRect(recipeText.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(36f, -236f), new Vector2(-34f, -160f));
        Heading(right, "INGREDIENTS", 250f, 36f);
        recipeIngredients = Paragraph("Ingredients", right, "", 21f, UIStyle.Cream);
        UIKit.SetRect(recipeIngredients.rectTransform, Vector2.zero, Vector2.one, new Vector2(40f, 100f), new Vector2(-34f, -292f));
        recipeEmpty = Paragraph("Empty", right, "", 21f, UIStyle.Tan);
        UIKit.SetRect(recipeEmpty.rectTransform, Vector2.zero, Vector2.one, new Vector2(40f, 100f), new Vector2(-40f, -40f));

        cookButton = PageButton(right, "COOK", () => Cook(selectedRecipe, 1), new Vector2(0.5f, 0f), new Vector2(-130f, 28f), new Vector2(230f, 58f), 30f);
        cookAllButton = PageButton(right, "COOK ALL", () => Cook(selectedRecipe, int.MaxValue), new Vector2(0.5f, 0f), new Vector2(130f, 28f), new Vector2(230f, 58f), 24f);
    }

    void RefreshCook()
    {
        CraftingStation fire = CookStation;
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        knownRecipes.Clear();
        int unknown = 0;
        if (fire != null && fire.recipes != null)
            foreach (CraftingRecipe recipe in fire.recipes)
            {
                if (!Valid(recipe) || knownRecipes.Contains(recipe)) continue;
                if (RecipeKnowledge.IsKnown(recipe)) knownRecipes.Add(recipe);
                else unknown++;
            }

        // Keep the choice; else the first one you can cook, else the first one
        if (selectedRecipe != null && !knownRecipes.Contains(selectedRecipe)) selectedRecipe = null;
        if (selectedRecipe == null)
            foreach (CraftingRecipe recipe in knownRecipes) if (CanCook(recipe, inventory) > 0) { selectedRecipe = recipe; break; }
        if (selectedRecipe == null && knownRecipes.Count > 0) selectedRecipe = knownRecipes[0];

        Clear(recipeList);
        foreach (CraftingRecipe recipe in knownRecipes)
        {
            CraftingRecipe captured = recipe;
            int count = CanCook(recipe, inventory);
            string label = recipe.DisplayName + (recipe.resultAmount > 1 ? $"  <color=#B8AA90>x{recipe.resultAmount}</color>" : "");
            string detail = count > 0 ? $"<color=#9BE08A>Ready  ·  enough for {count}</color>" : $"<color=#FF8A70>Missing {Missing(recipe, inventory)}</color>";
            ItemRow(recipeList, recipe.result, label, detail, count > 0, recipe == selectedRecipe, () =>
            {
                selectedRecipe = captured;
                RefreshCook();
            });
        }
        if (knownRecipes.Count == 0) Note(recipeList, "You don't know any recipes yet.", 40f);
        if (unknown > 0)
            Note(recipeList, $"<color=#B8AA90>{unknown} more {(unknown == 1 ? "recipe" : "recipes")} can be cooked here. Read recipe scrolls to learn them.</color>", 60f);

        RefreshRecipeDetail(inventory);
    }

    void RefreshRecipeDetail(PlayerInventoryManager inventory)
    {
        CraftingRecipe recipe = selectedRecipe;
        bool any = recipe != null;
        recipeIcon.gameObject.SetActive(any && recipe.result.itemIcon != null);
        recipeName.gameObject.SetActive(any);
        recipeEffect.gameObject.SetActive(any);
        recipeText.gameObject.SetActive(any);
        recipeIngredients.gameObject.SetActive(any);
        recipeIngredients.transform.parent.Find("Heading")?.gameObject.SetActive(any);
        recipeEmpty.gameObject.SetActive(!any);
        cookButton.gameObject.SetActive(any);
        cookAllButton.gameObject.SetActive(any);
        if (!any)
        {
            recipeEmpty.text = "Nothing to cook yet.\n\nRecipe scrolls are found and bought on your journey. Read one (click it in your inventory) to learn its recipe, then come back to the fire.";
            return;
        }

        PlayerItemData result = recipe.result;
        recipeIcon.sprite = result.itemIcon;
        recipeName.text = recipe.DisplayName + (recipe.resultAmount > 1 ? $"  x{recipe.resultAmount}" : "");
        recipeEffect.text = LootWindow.Describe(result);
        recipeText.text = string.IsNullOrWhiteSpace(result.itemDescription) ? "" : result.itemDescription;

        var lines = new System.Text.StringBuilder();
        foreach (var cost in Costs(recipe))
        {
            int have = inventory != null ? inventory.CountItem(cost.Key) : 0;
            string color = have >= cost.Value ? "#9BE08A" : "#FF8A70";
            lines.Append($"<color={color}>{have}/{cost.Value}</color>   {NameOf(cost.Key)}\n");
        }
        recipeIngredients.text = lines.ToString();

        int count = CanCook(recipe, inventory);
        SetEnabled(cookButton, count > 0);
        SetEnabled(cookAllButton, count > 1);
        SetLabel(cookAllButton, count > 1 ? $"COOK ALL ({count})" : "COOK ALL");
    }

    // ---------- Cooking ----------

    void Cook(CraftingRecipe recipe, int times)
    {
        if (!open || recipe == null) return;
        int made = 0;
        string problem = null;
        for (int i = 0; i < times && i < 99; i++)
        {
            problem = CookOnce(recipe);
            if (problem != null) break;
            made++;
        }
        if (made > 0)
        {
            SoundManager.Play(Sfx.UseItem, SoundPoint());
            string amount = made * recipe.resultAmount > 1 ? $"{made * recipe.resultAmount}x " : "";
            ShowMessage(droppedSome ? $"Cooked {amount}{recipe.DisplayName} (bags full: the rest is on the ground)" : $"Cooked {amount}{recipe.DisplayName}");
        }
        else if (problem != null) ShowMessage(problem);
        droppedSome = false;
        RefreshPage();
    }

    bool droppedSome;

    // One cook, checked from scratch. Null = done, else why not
    string CookOnce(CraftingRecipe recipe)
    {
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        CraftingStation fire = CookStation;
        if (inventory == null || !StillUsable() || fire == null || fire.upgradeTable) return "You need to be at the fire";
        if (!Valid(recipe) || fire.recipes == null || !fire.recipes.Contains(recipe) || !RecipeKnowledge.IsKnown(recipe)) return "This recipe can't be cooked here";
        List<KeyValuePair<PlayerItemData, int>> costs = Costs(recipe);
        if (!inventory.TryConsumeItems(costs)) return "Missing ingredients";

        int added = inventory.AddItem(recipe.result, recipe.resultAmount);
        if (added < recipe.resultAmount)
        {
            LootBag.DropFromPlayer(player, recipe.result, inventory.lootBagPrefab, inventory.bagMergeRadius, recipe.resultAmount - added);
            droppedSome = true;
        }
        return null;
    }

    static bool Valid(CraftingRecipe recipe)
    {
        if (recipe == null || recipe.result == null || recipe.resultAmount <= 0 || recipe.ingredients == null || recipe.ingredients.Count == 0) return false;
        foreach (RecipeIngredient ingredient in recipe.ingredients)
            if (ingredient == null || ingredient.item == null || ingredient.amount <= 0) return false;
        return true;
    }

    // The ingredients, the same item listed twice counted together
    static List<KeyValuePair<PlayerItemData, int>> Costs(CraftingRecipe recipe)
    {
        var totals = new List<KeyValuePair<PlayerItemData, int>>();
        if (!Valid(recipe)) return totals;
        foreach (RecipeIngredient ingredient in recipe.ingredients)
        {
            int index = totals.FindIndex(p => p.Key == ingredient.item);
            if (index >= 0) totals[index] = new KeyValuePair<PlayerItemData, int>(ingredient.item, totals[index].Value + ingredient.amount);
            else totals.Add(new KeyValuePair<PlayerItemData, int>(ingredient.item, ingredient.amount));
        }
        return totals;
    }

    // How many times the bags allow it
    static int CanCook(CraftingRecipe recipe, PlayerInventoryManager inventory)
    {
        if (inventory == null || !Valid(recipe)) return 0;
        int best = int.MaxValue;
        foreach (var cost in Costs(recipe)) best = Mathf.Min(best, inventory.CountItem(cost.Key) / cost.Value);
        return best == int.MaxValue ? 0 : best;
    }

    static string Missing(CraftingRecipe recipe, PlayerInventoryManager inventory)
    {
        var names = new List<string>();
        foreach (var cost in Costs(recipe))
        {
            int have = inventory != null ? inventory.CountItem(cost.Key) : 0;
            if (have < cost.Value) names.Add(cost.Value - have > 1 ? $"{cost.Value - have} {NameOf(cost.Key)}" : NameOf(cost.Key));
        }
        return names.Count == 0 ? "nothing" : string.Join(", ", names);
    }

    Vector3 SoundPoint() => Camera.main != null ? Camera.main.transform.position : player != null ? player.position : Vector3.zero;
}
