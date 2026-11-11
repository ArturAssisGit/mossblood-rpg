using UnityEngine;

public enum WeaponChoice { None, Katana, SmallSwords, Bow, Sheathe }

// PlayerCombat: weapons chosen from the quick wheel, and drawing your weapon by itself when enemies start hunting you.
// Both go through the very same path as the keys 1 / 2 / 3 (a "virtual key press"), so every rule of drawing,
// swapping and sheathing stays exactly as it is.
public partial class PlayerCombat
{
    [Header("Auto draw")]
    [Tooltip("When an enemy starts hunting you, your weapon comes out by itself (the last one you used). Once per fight: put it away and it stays away")]
    public bool autoDrawWhenHunted = true;

    WeaponChoice queuedChoice;
    int queuedFrame = -100;
    WeaponChoice lastWeapon = WeaponChoice.Katana;
    bool huntedBefore;
    float nextHuntCheck, notHuntedSince;

    // The quick wheel asks for a weapon (used on the next frames the controls are free)
    public void RequestWeapon(WeaponChoice choice)
    {
        queuedChoice = choice;
        queuedFrame = Time.frameCount;
    }

    public WeaponChoice DrawnWeapon => isBowDrawn ? WeaponChoice.Bow
        : isEquipped ? (equipmentManager != null && equipmentManager.UsingSmallSwords ? WeaponChoice.SmallSwords : WeaponChoice.Katana)
        : WeaponChoice.None;

    // The key that does what was asked, or None when it is already like that
    KeyCode TakeQueuedPress()
    {
        WeaponChoice choice = queuedChoice;
        if (choice == WeaponChoice.None) return KeyCode.None;
        queuedChoice = WeaponChoice.None;
        if (Time.frameCount - queuedFrame > 30) return KeyCode.None; // too old (the controls stayed blocked)
        return KeyFor(choice);
    }

    KeyCode KeyFor(WeaponChoice choice)
    {
        WeaponChoice now = DrawnWeapon;
        if (choice == now || equipmentManager == null) return KeyCode.None;
        switch (choice)
        {
            case WeaponChoice.Katana: return equipmentManager.HasKatanaEquipped ? swordKey : KeyCode.None;
            case WeaponChoice.SmallSwords: return equipmentManager.HasSmallSwordsEquipped ? smallSwordsKey : KeyCode.None;
            case WeaponChoice.Bow: return equipmentManager.HasBowEquipped ? bowDrawKey : KeyCode.None;
            case WeaponChoice.Sheathe:
                if (now == WeaponChoice.Bow) return bowDrawKey;
                if (now == WeaponChoice.SmallSwords) return smallSwordsKey;
                if (now == WeaponChoice.Katana) return swordKey;
                return KeyCode.None;
        }
        return KeyCode.None;
    }

    // Rising edge of "an enemy is hunting me": draw the last weapon used (only if nothing is drawn yet)
    void TickAutoDraw(ref KeyCode virtualKey)
    {
        if (!autoDrawWhenHunted || virtualKey != KeyCode.None || Time.time < nextHuntCheck) return;
        nextHuntCheck = Time.time + 0.25f;
        bool hunted = Hunted();
        if (!hunted)
        {
            if (huntedBefore && Time.time - notHuntedSince > 3f) huntedBefore = false; // the fight is over
            return;
        }
        notHuntedSince = Time.time;
        if (huntedBefore) return;
        huntedBefore = true;
        if (DrawnWeapon != WeaponChoice.None || IsCharging || SwordAbilityActive) return;
        WeaponChoice pick = Available(lastWeapon) ? lastWeapon
            : Available(WeaponChoice.Katana) ? WeaponChoice.Katana
            : Available(WeaponChoice.SmallSwords) ? WeaponChoice.SmallSwords
            : Available(WeaponChoice.Bow) ? WeaponChoice.Bow : WeaponChoice.None;
        if (pick != WeaponChoice.None) virtualKey = KeyFor(pick);
    }

    bool Available(WeaponChoice choice)
    {
        if (equipmentManager == null) return false;
        return choice == WeaponChoice.Katana ? equipmentManager.HasKatanaEquipped
            : choice == WeaponChoice.SmallSwords ? equipmentManager.HasSmallSwordsEquipped
            : choice == WeaponChoice.Bow && equipmentManager.HasBowEquipped;
    }

    bool Hunted()
    {
        Transform me = PlayerRef.Transform;
        if (me == null) return false;
        foreach (EnemyAI_01 enemy in EnemyAI_01.All)
            if (enemy != null && enemy.IsInCombat && enemy.isActiveAndEnabled && enemy.IsTargeting(me)) return true;
        return false;
    }

    // Remembered after the keys were handled this frame
    void RememberWeapon()
    {
        WeaponChoice now = DrawnWeapon;
        if (now != WeaponChoice.None) lastWeapon = now;
    }
}
