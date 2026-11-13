using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// WorldSaveGameManager: Menu buttons (split out of WorldSaveGameManager.cs; same class, same behaviour)
public partial class WorldSaveGameManager
{
    // ---------- Menu buttons ----------

    // The old saves stay in the list (Load), a new game simply starts
    public void StartNewGame()
    {
        if (IsLoading) return;
        StartCoroutine(LoadWorld(null));
    }

    // Loads the most recent save
    public void ContinueGame()
    {
        string path = LatestSavePath();
        if (path == null) { Debug.LogWarning("Continue: there is no save yet."); return; }
        LoadSave(path);
    }

    // Loads one save of the list (from the main menu or from the game)
    public void LoadSave(string path)
    {
        if (IsLoading) return;
        SaveData data = ReadFile(path);
        if (data == null) { ShowMessage("This save can't be read"); return; }
        StartCoroutine(LoadWorld(data));
    }

    // Old name kept so older buttons still work
    public IEnumerator LoadNewGame()
    {
        yield return LoadWorld(null);
    }

    IEnumerator LoadWorld(SaveData data)
    {
        IsLoading = true;
        StartedFromMenu = true;
        Time.timeScale = 1f; // loading from the pause menu
        bool fromMenu = TitleScreenManager.IsOpen;
        string introText = OnistoneLore.FatherLetterPages; // the story's opening letter, one page at a time (one place: OnistoneLore, also kept in the Diary)
        bool hasIntro = data == null && !string.IsNullOrWhiteSpace(introText);
        // New game: the world comes in over newGameFadeSeconds (0 = a short fade). A save: a short fade.
        float fadeSeconds = data == null ? (newGameFadeSeconds > 0f ? newGameFadeSeconds : 1.5f) : fromMenu ? 1.4f : 1f;

        // From the click the screen and the sound go softly to black (never a cut), then the loading happens behind it
        ScreenFade.FadeOut(fromMenu ? 0.9f : 0.5f, true);
        for (float waited = 0f; !ScreenFade.IsBlack && waited < 3f; waited += Time.unscaledDeltaTime) yield return null;
        // New game: the father's letter is on screen while the world loads behind it, so there is no waiting on a
        // black screen; the hint (and going on) only comes once the world is ready.
        if (hasIntro) ScreenFade.ShowIntro(introText, newGameIntroHint, newGameIntroSkipKey, newGameIntroFontSize, false);
        yield return null; // one frame so the letter is drawn before the loading starts
        RemoveOldGameSystems();
        // Use the scene from the save, unless it's the menu we are in now
        string scene = data != null && !string.IsNullOrEmpty(data.sceneName) && data.sceneName != SceneManager.GetActiveScene().name
            ? data.sceneName : worldSceneName;

        AsyncOperation op = !string.IsNullOrEmpty(scene) ? SceneManager.LoadSceneAsync(scene) : SceneManager.LoadSceneAsync(worldSceneIndex);
        if (op == null)
        {
            Debug.LogError($"Could not load the scene '{scene}'. Add it in File > Build Profiles > Scene List.");
            IsLoading = false;
            ScreenFade.FadeIn(0.5f);
            yield break;
        }
        while (!op.isDone) yield return null;

        // Let every script run its Start() first (PlayerStats fills health etc. there)
        yield return null;
        yield return null;

        CreateGameSystems();
        PlayerStats player = EnsurePlayer();
        if (player != null)
        {
            PlayerRef.BindLocal(player.gameObject);
            if (PlayerEquipmentManager.Instance != null) PlayerEquipmentManager.Instance.ReconcileLocalEquipment(player.transform);
        }
        if (data != null) ApplySave(data);
        else if (player != null) { MoveToSpawnPoint(player.transform); SeasonSystem.RestoreSave(0f); GiveStartingGear(); StartRainIfWanted(); StartSeatedIfWanted(); CampStartPoint.PitchForNewGame(); HorseOwnership.Restore(null); ItemPickup.Restore(null); MapSystem.ResetExplored(); NpcDialogue.RestoreDiscovered(null); ShrineSystem.RestoreDiscovered(null); DiaryProgress.Restore(null); RoninLeague.Restore(null); ArmySystem.Restore(null); ExpeditionSystem.Restore(null); TimedBuffs.Clear(); RecipeKnowledge.Restore(null); WorldActivitySystem.Restore(null); }

        ResetAutosaveTimer();
        IsLoading = false;
        // A couple of frames for the new world to settle (first shadows, streaming, the camera finding the player)
        for (int i = 0; i < 3; i++) yield return null;
        if (hasIntro)
        {
            ScreenFade.SetIntroReady();
            while (ScreenFade.IntroShowing) yield return null;
        }
        ScreenFade.FadeIn(fadeSeconds, data != null || newGameFadeSound);
        if (data == null) NewGameTutorial.Begin(); // the first steps, one at a time
        else NewGameTutorial.Stop();
    }

    void StartSeatedIfWanted()
    {
        if (newGameStartsSitting) SitAnimation.StartSeated();
    }

    void StartRainIfWanted()
    {
        if (newGameStartsRaining && RainSystem.Instance != null) RainSystem.Instance.StartRainInstant(newGameRainPreset);
    }

    // New game: the starting kit is equipped. The backpack is not (it lies on the ground by the spawn) unless
    // "Equip Starting Backpack" is on; then it goes first so its storage is available for the rest of the kit.
    void GiveStartingGear()
    {
        PlayerInventoryManager inv = PlayerInventoryManager.Instance;
        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;
        if (inv == null || gear == null || inv.inventorySlots == null) return;

        bool wasMuted = SoundManager.Muted;
        SoundManager.Muted = true; // no equip sounds at the start
        try
        {
            PlayerItemData backpack = !equipStartingBackpack ? null : startingBackpack != null ? startingBackpack : ItemDatabase.Find("backpack_01");
            if (backpack != null && !gear.EquipNewBackpack(backpack))
                Debug.LogWarning("New Game: could not equip the starting backpack.");
            foreach (PlayerItemData item in new[] {
                startingHelmet != null ? startingHelmet : ItemDatabase.Find("hat_01"),
                startingWeapon != null ? startingWeapon : ItemDatabase.Find("sword_01"),
                startingCape != null ? startingCape : ItemDatabase.Find("cape_01"),
                startingLantern != null ? startingLantern : ItemDatabase.Find("lantern_01") })
            {
                if (item == null) continue;
                PlayerItemSlot temp = null;
                foreach (PlayerItemSlot s in inv.UsableSlots())
                    if (s != null && s.IsEmpty) { temp = s; break; }
                if (temp == null) { Debug.LogWarning($"New Game: no free slot for '{item.name}'."); continue; }
                temp.SetItem(item, 1);
                gear.EquipItem(temp);
                if (temp.itemData == item)
                    Debug.LogWarning($"New Game: could not equip '{item.name}'; kept it in the inventory.");
            }
        }
        finally { SoundManager.Muted = wasMuted; }
        // A few coins to start the road with
        PlayerItemData coin = ItemDatabase.Find("coins");
        if (coin != null && startingCoins > 0 && inv.AddItem(coin, startingCoins) < startingCoins)
            Debug.LogWarning("New Game: not every starting coin fit in the inventory.");
    }
}
