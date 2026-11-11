using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public partial class PlayerStats : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;
    public float currentHealth;
    public Image healthBarFill;
    public CanvasGroup healthGroup;
    public float quitDelay = 2f;

    [Header("Health Regen")]
    public float healthRegenRate = 3f;
    [Tooltip("Seconds without taking damage before health starts regenerating")] public float healthRegenDelay = 4f;

    [Header("Stamina")]
    public float maxStamina = 100f;
    public float currentStamina;
    public Image staminaBarFill;
    public CanvasGroup staminaGroup;
    public float staminaRegenRate = 15f;

    [Header("Hunger")]
    public float maxHunger = 100f;
    public float currentHunger;
    [Tooltip("Hunger lost per minute (100 / 2 = empty after 50 minutes)")] public float hungerPerMinute = 2f;

    [Header("Thirst")]
    public float maxThirst = 100f;
    public float currentThirst;
    [Tooltip("Thirst lost per minute (100 / 2.5 = empty after 40 minutes)")] public float thirstPerMinute = 2.5f;

    [Header("Spirit (restored by praying at a shrine)")]
    public float maxSpirit = 100f;
    public float currentSpirit;
    public Image spiritBarFill;
    public CanvasGroup spiritGroup;
    [Tooltip("Spirit lost per minute (100 / 4 = empty after 25 minutes)")] public float spiritPerMinute = 4f;
    [Tooltip("Below this part of the spirit you start to walk and attack slower (0.35 = under 35%)")] [Range(0f, 1f)] public float lowSpiritThreshold = 0.35f;
    [Tooltip("Speed of walking and attacking with no spirit at all (1 = no slow down, 0.7 = 30% slower). Between the threshold and 0 it slows down gradually")] [Range(0.3f, 1f)] public float emptySpiritSpeed = 0.7f;
    [Tooltip("Icon shown when the spirit is low (empty = shows the word 'Restless')")] public Sprite spiritIcon;

    [Header("Hunger & Thirst Effects")]
    [Tooltip("Below this % health stops regenerating and stamina regenerates at half speed")] [Range(0f, 1f)] public float lowNeedsThreshold = 0.25f;
    [Tooltip("Health lost per second while starving or dehydrated (each one counts)")] public float emptyNeedsDamage = 1f;

    [Header("Hunger & Thirst Icons (only appear when low)")]
    [Tooltip("Icon shown when hungry (empty = shows the word 'Hungry')")] public Sprite hungerIcon;
    [Tooltip("Icon shown when thirsty (empty = shows the word 'Thirsty')")] public Sprite thirstIcon;
    [Tooltip("Distance from the left edge of the screen (pixels on a 1920x1080 screen)")] public float needsIconsLeft = 40f;
    [Tooltip("Height of the first icon on the screen (0 = bottom, 0.5 = middle, 1 = top)")] [Range(0f, 1f)] public float needsIconsHeight = 0.6f;
    [Tooltip("Space between the icons, one under the other (pixels)")] public float needsIconsSpacing = 8f;
    public float needsIconSize = 56f;

    [Header("Defense")]
    [Tooltip("Defense needed to block half of the damage")] public float defenseScaling = 50f;

    [Header("Blocking")]
    [Range(0f, 1f), Tooltip("1 = blocks all damage, 0.5 = blocks half")] public float blockReduction = 1f;
    public float blockStaminaCost = 10f;
    [Tooltip("Only blocks attacks coming from within this angle in front of the player")] public float blockAngle = 120f;
    public Animator anim;

    [Header("Bar Visibility")]
    public float fadeSpeed = 6f;
    public float hideDelay = 1.5f;

    private float statsHideTimer, statsAlpha, lastSpiritForHud;
    private PlayerMovement hudMovement;
    private float lastDamageTime = -999f;
    public uint HitRevision { get; private set; }
    private bool deathHandled, enemyDeathPenaltyApplied;
    private CanvasGroup hungerIconGroup, thirstIconGroup, spiritIconGroup;
    private CanvasGroup[] needIcons;
    private Graphic hungerIconGraphic, thirstIconGraphic, spiritIconGraphic;

    public bool IsDead => currentHealth <= 0f;
    public bool IsHungry => currentHunger <= MaxHunger * lowNeedsThreshold;
    public bool IsThirsty => currentThirst <= MaxThirst * lowNeedsThreshold;
    public bool IsSpiritLow => currentSpirit <= MaxSpirit * lowSpiritThreshold;

    // 1 = normal. Lower while the spirit is low: PlayerMovement walks and PlayerCombat swings this much slower
    public float SpiritSpeedMultiplier
    {
        get
        {
            float low = MaxSpirit * lowSpiritThreshold;
            if (low <= 0f || currentSpirit >= low) return 1f;
            return Mathf.Lerp(emptySpiritSpeed, 1f, Mathf.Clamp01(currentSpirit / low));
        }
    }

    // The local player's slow down from low spirit (1 = none)
    public static float SpiritSpeed
    {
        get
        {
            PlayerStats stats = PlayerRef.Stats;
            return stats != null ? stats.ActionSpeedMultiplier : 1f;
        }
    }

    void Start()
    {
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (PlayerEquipmentManager.Instance != null) PlayerEquipmentManager.Instance.PrepareCapeEquipment();
        RefreshBodyTemperature();
        currentHealth = MaxHealth;
        currentStamina = MaxStamina;
        currentHunger = MaxHunger;
        currentThirst = MaxThirst;
        currentSpirit = MaxSpirit;
        BuildNeedsIcons();

        BindStatsBars();
        hudMovement = GetComponentInChildren<PlayerMovement>();
        lastSpiritForHud = currentSpirit;
        if(useVitalOrbHUD)statsHideTimer=3f;
        SetStatsAlpha(0f);
    }

    void Update()
    {
        TickSurvivalStatus();
        if (Time.unscaledTime >= nextTemperatureCheck) RefreshBodyTemperature();
        UpdateNeeds();
        bool lowNeeds = IsHungry || IsThirsty;

        float staminaRegen = EffectiveStaminaRegen * HatPassives.Bonus(this,HatPassive.FasterStaminaRecovery) * (lowNeeds ? 0.5f : 1f) * (ItemUpgrades.Active(OniSpirit.Nue) ? 1f + ItemUpgrades.NueStaminaBonus : 1f);
        currentStamina = Mathf.Min(currentStamina + staminaRegen * Time.deltaTime, MaxStamina);

        if (!IsBleeding && !lowNeeds && currentHealth > 0f && Time.time >= lastDamageTime + healthRegenDelay)
            currentHealth = Mathf.Min(currentHealth + EffectiveHealthRegen * HatPassives.Bonus(this,HatPassive.FasterHealthRecovery) * Time.deltaTime, MaxHealth);

        if (healthBarFill != null) healthBarFill.fillAmount = MaxHealth > 0f ? Mathf.Clamp01(currentHealth / MaxHealth) : 0f;
        if (staminaBarFill != null) staminaBarFill.fillAmount = MaxStamina > 0f ? Mathf.Clamp01(currentStamina / MaxStamina) : 0f;
        if (spiritBarFill != null) spiritBarFill.fillAmount = MaxSpirit > 0f ? Mathf.Clamp01(currentSpirit / MaxSpirit) : 0f;
        UpdateStatsVisibility();
        UpdateLowHealthBorder();
    }

    // ---------- Hunger & thirst ----------

    void UpdateNeeds()
    {
        if (currentHealth <= 0f) return;

        currentHunger = Mathf.Max(currentHunger - hungerPerMinute * HatPassives.Reduction(this,HatPassive.SlowerHunger) / 60f * Time.deltaTime, 0f);
        float thirstRate = thirstPerMinute * HatPassives.Reduction(this,HatPassive.SlowerThirst) * (ItemUpgrades.Active(OniSpirit.Kappa) ? ItemUpgrades.KappaThirstMultiplier : 1f);
        currentThirst = Mathf.Max(currentThirst - thirstRate / 60f * Time.deltaTime, 0f);
        if (!ShrineSystem.IsPraying) currentSpirit = Mathf.Max(currentSpirit - spiritPerMinute * HatPassives.Reduction(this,HatPassive.SlowerSpiritDrain) / 60f * Time.deltaTime, 0f);

        // Starving or dehydrated: slowly lose health
        int emptyNeeds = (currentHunger <= 0f ? 1 : 0) + (currentThirst <= 0f ? 1 : 0);
        if (emptyNeeds > 0 && !ShrineSystem.IsPraying)
        {
            currentHealth = Mathf.Max(currentHealth - emptyNeedsDamage * emptyNeeds * Time.deltaTime, 0f);
            if (currentHealth <= 0f) Die();
        }

        UpdateNeedIcon(thirstIconGroup, thirstIconGraphic, IsThirsty, currentThirst <= 0f);
        UpdateNeedIcon(hungerIconGroup, hungerIconGraphic, IsHungry, currentHunger <= 0f);
        UpdateNeedIcon(spiritIconGroup, spiritIconGraphic, IsSpiritLow, currentSpirit <= 0f);
        UpdateNeedIcon(temperatureIconGroup, temperatureIconGraphic, Temperature != BodyTemperatureState.Normal, false);
        UpdateSurvivalIcons();
        StackNeedIcons();
    }

    // Fades the icon in when low; pulses red when completely empty
    void UpdateNeedIcon(CanvasGroup group, Graphic graphic, bool low, bool empty)
    {
        if (group == null) return;
        float pulse = (Mathf.Sin(Time.time * (empty ? 7f : 3f)) + 1f) * 0.5f;
        float targetAlpha = low ? Mathf.Lerp(0.6f, 1f, pulse) : 0f;
        group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, fadeSpeed * Time.deltaTime);

        if (graphic != null) graphic.color = empty ? Color.Lerp(Color.white, new Color(1f, 0.25f, 0.2f), pulse) : Color.white;
    }

    // Call these from food and drink items
    public void Eat(float amount) => currentHunger = Mathf.Min(currentHunger + amount, MaxHunger);
    public void Drink(float amount) => currentThirst = Mathf.Min(currentThirst + amount, MaxThirst);
    public void Heal(float amount) => currentHealth = Mathf.Min(currentHealth + amount, MaxHealth);
    public void RestoreSpirit(float amount) => currentSpirit = Mathf.Clamp(currentSpirit + amount, 0f, MaxSpirit);

    void BuildNeedsIcons()
    {
        GameObject canvasObject = new GameObject("NeedsIcons", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        thirstIconGroup = CreateNeedIcon(canvasObject.transform, "Thirsty", thirstIcon, out thirstIconGraphic);
        hungerIconGroup = CreateNeedIcon(canvasObject.transform, "Hungry", hungerIcon, out hungerIconGraphic);
        spiritIconGroup = CreateNeedIcon(canvasObject.transform, "Restless", spiritIcon, out spiritIconGraphic);
        temperatureIconGroup = CreateNeedIcon(canvasObject.transform, "Body temperature", null, out temperatureIconGraphic);
        ((RectTransform)temperatureIconGroup.transform).sizeDelta = new Vector2(240f, needsIconSize * .5f);
        UpdateTemperatureLabel();
        BuildSurvivalIcons(canvasObject.transform);
        needIcons = new[] { thirstIconGroup, hungerIconGroup, spiritIconGroup, temperatureIconGroup,bleedingIconGroup,warmthIconGroup };
        StackNeedIcons(true);
    }

    // Left side of the screen, one under the other. Only the ones showing take a place, so there are never gaps
    void StackNeedIcons(bool instant = false)
    {
        if (needIcons == null) return;
        float y = 0f;
        foreach (CanvasGroup group in needIcons)
        {
            if (group == null) continue;
            RectTransform rect = (RectTransform)group.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, needsIconsHeight);
            Vector2 wanted = new Vector2(needsIconsLeft, -y);
            rect.anchoredPosition = instant ? wanted : Vector2.Lerp(rect.anchoredPosition, wanted, 1f - Mathf.Exp(-12f * Time.deltaTime));
            if (group.alpha > 0.01f) y += rect.sizeDelta.y + needsIconsSpacing;
        }
    }

    CanvasGroup CreateNeedIcon(Transform parent, string label, Sprite sprite, out Graphic graphic)
    {
        GameObject root = new GameObject(label + "Icon", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(parent, false);
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, needsIconsHeight);
        rect.pivot = new Vector2(0f, 1f); // top-left corner: the next icon goes under it (StackNeedIcons)
        rect.sizeDelta = sprite != null ? Vector2.one * needsIconSize : new Vector2(needsIconSize * 1.6f, needsIconSize * 0.5f);

        if (sprite != null)
        {
            Image image = root.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            graphic = image;
        }
        else
        {
            // No icon assigned yet: show the word instead
            TextMeshProUGUI text = root.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 22;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;
            if (LootWindow.Instance != null && LootWindow.Instance.font != null) text.font = LootWindow.Instance.font;
            graphic = text;
        }

        CanvasGroup group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        return group;
    }

    // Bind once, including UI created in the scene that has not yet been wired in the Inspector.
    void BindStatsBars()
    {
        Transform root = healthGroup != null ? healthGroup.transform.parent :
            staminaGroup != null ? staminaGroup.transform.parent : null;
        if (root == null)
            foreach (RectTransform candidate in FindObjectsByType<RectTransform>(FindObjectsInactive.Include))
                if (candidate.name == "PlayerUIStats") { root = candidate; break; }
        if (root == null) return;
        BindStatsBar(root, "HealthBackground", "HealthFill", ref healthGroup, ref healthBarFill);
        BindStatsBar(root, "StaminaBackground", "StaminaFill", ref staminaGroup, ref staminaBarFill);
        BindStatsBar(root, "SpiritBackground", "SpiritFill", ref spiritGroup, ref spiritBarFill);
        if (spiritBarFill != null)
        {
            spiritBarFill.type = Image.Type.Filled;
            spiritBarFill.fillMethod = Image.FillMethod.Horizontal;
            spiritBarFill.fillOrigin = 0;
        }
    }

    static void BindStatsBar(Transform root, string backgroundName, string fillName, ref CanvasGroup group, ref Image fill)
    {
        Transform background = root.Find(backgroundName);
        if (background == null) return;
        if (group == null)
        {
            group = background.GetComponent<CanvasGroup>();
            if (group == null) group = background.gameObject.AddComponent<CanvasGroup>();
        }
        if (fill == null)
        {
            Transform child = background.Find(fillName);
            if (child != null) fill = child.GetComponent<Image>();
        }
    }

    void UpdateStatsVisibility()
    {
        // Passive spirit drain must not keep the entire HUD permanently visible.
        float passiveDrain = Mathf.Max(0f, spiritPerMinute) / 60f * Time.deltaTime;
        bool spiritChanged = Mathf.Abs(currentSpirit - lastSpiritForHud) > passiveDrain + 0.001f;
        lastSpiritForHud = currentSpirit;
        bool running = hudMovement != null && hudMovement.isActiveAndEnabled && hudMovement.IsRunning;
        var combat=useVitalOrbHUD?PlayerRef.Get<PlayerCombat>():null;
        bool show = running || currentHealth < MaxHealth || currentStamina < MaxStamina || spiritChanged
            || (useVitalOrbHUD && (IsHungry || IsThirsty || IsSpiritLow || (combat!=null && combat.IsArmed)));
        if (show) statsHideTimer = hideDelay;
        else statsHideTimer = Mathf.Max(0f, statsHideTimer - Time.deltaTime);
        statsAlpha = Mathf.MoveTowards(statsAlpha, show || statsHideTimer > 0f ? 1f : 0f, fadeSpeed * Time.deltaTime);
        SetStatsAlpha(statsAlpha);
    }

    void SetStatsAlpha(float alpha)
    {
        float legacyAlpha=useVitalOrbHUD?0f:alpha;
        if (healthGroup != null) healthGroup.alpha = legacyAlpha;
        if (staminaGroup != null) staminaGroup.alpha = legacyAlpha;
        if (spiritGroup != null) spiritGroup.alpha = legacyAlpha;
        PresentVitalHUD(alpha);
    }

    public bool TryConsumeStamina(float cost)
    {
        if (currentStamina < cost) return false;
        currentStamina -= cost;
        return true;
    }

    public void TakeDamage(float amount) => TakeDamage(amount, null);

    public void TakeDamage(float amount, Vector3? attackerPosition) => TakeDamage(amount, attackerPosition, null);

    // hitSound = impact sound of the weapon that hit (enemy / boss); null = none (e.g. arrows play their own)
    public void TakeDamage(float amount, Vector3? attackerPosition, Sfx? hitSound) => TakeDamage(amount, attackerPosition, hitSound, false);

    public void TakeEnemyDamage(float amount, Vector3 attackerPosition, Sfx? hitSound = null) => TakeDamage(amount, attackerPosition, hitSound, true);

    void TakeDamage(float amount, Vector3? attackerPosition, Sfx? hitSound, bool enemyAttack)
    {
        // Dead, dodged it, in a finisher, using a sword ability (F) or praying at a shrine: no damage
        if (BossEncounterDirector.ProtectLocalPlayer || currentHealth <= 0f || PlayerDodge.IsInvulnerable || PlayerFinisher.IsActive || ShrineSystem.IsPraying
            || InSwordAbility()) return;

        bool blockedHit = false;
        if (IsBlocking(attackerPosition))
        {
            float cost = blockStaminaCost * HatPassives.Reduction(this,HatPassive.LowerBlockCost) * (PlayerEquipmentManager.PerkActive(BackpackPerk.FreeBlock) ? 0.5f : 1f);
            if (TryConsumeStamina(cost))
            {
                blockedHit = true;
                amount *= 1f - EffectiveBlockReduction;
                SoundManager.Play(Sfx.Block, transform.position + Vector3.up * 1.2f);
                Debug.Log("Blocked!");
                if (amount <= 0f) return;
            }
            else Debug.Log("Not enough stamina to block!");
        }

        float defense = PlayerEquipmentManager.Instance != null ? PlayerEquipmentManager.Instance.TotalDefense : 0f;
        float finalDamage = amount * defenseScaling / (defenseScaling + defense);
        if (ItemUpgrades.Active(OniSpirit.Gashadokuro)) finalDamage *= ItemUpgrades.GashadokuroDamageTaken; // Oni Stone perk

        if(!RuntimeValues.IsFinite(finalDamage) || finalDamage<=0)return;
        lastDamageTime=Time.time;HitRevision++;
        currentHealth = Mathf.Max(currentHealth - finalDamage, 0f);
        RecordBleedingHit(finalDamage,!blockedHit && (enemyAttack || attackerPosition.HasValue));

        // Blood flies away from the attacker
        Collider body = GetComponent<Collider>();
        Vector3 hitPoint = body != null ? body.bounds.center + Vector3.up * body.bounds.extents.y * 0.3f : transform.position + Vector3.up * 1.2f;
        Vector3 away = attackerPosition.HasValue ? hitPoint - attackerPosition.Value : -transform.forward;
        away.y = 0f;
        BloodEffects.Spawn(hitPoint, away.normalized + Vector3.up * 0.35f);
        if (hitSound.HasValue) SoundManager.Play(hitSound.Value, hitPoint);
        SoundManager.Play(currentHealth <= 0f ? Sfx.PlayerDeath : Sfx.PlayerHurt, hitPoint);
        Debug.Log($"Player took {finalDamage:0.#} damage ({amount} - defense {defense})");
        if (currentHealth <= 0f) { Die(enemyAttack); return; }

        // "Combat Hit" animation (state name set in PlayerCombat -> Get Hit)
        foreach (PlayerCombat combat in GetComponentsInChildren<PlayerCombat>())
            if (combat.enabled) combat.PlayHitReaction();
    }

    // The sword / small sword ability is a moment of full protection: from the press of F until it ends
    PlayerCombat ownCombat;
    bool InSwordAbility()
    {
        if (ownCombat == null) ownCombat = GetComponentInChildren<PlayerCombat>();
        return ownCombat != null && ownCombat.enabled && ownCombat.SwordAbilityProtects;
    }

    bool IsBlocking(Vector3? attackerPosition)
    {
        if (anim == null || !anim.GetBool("isBlocking")) return false;
        if (attackerPosition == null) return true;

        Vector3 toAttacker = attackerPosition.Value - transform.position;
        toAttacker.y = 0f;
        return Vector3.Angle(transform.forward, toAttacker) <= blockAngle * 0.5f;
    }

    void Die(bool killedByEnemy = false)
    {
        if (deathHandled) return;
        deathHandled = true;
        ClearSurvivalOnDeath();
        enemyDeathPenaltyApplied = killedByEnemy;
        if (killedByEnemy) ApplyEnemyDeathPenalty();
        Debug.Log("Player died!");
        EnemyAI_01.ResetBossesAfterPlayerDeath(); // the boss heals to 100%

        foreach (PlayerCombat combat in GetComponentsInChildren<PlayerCombat>())
        {
            combat.ResetCombatState();
            combat.enabled = false;
        }
        foreach (PlayerMovement movement in GetComponentsInChildren<PlayerMovement>()) movement.enabled = false;

        // Death animation (needs a "DieTrigger" parameter + Any State -> Death transition).
        // Nothing left over may pull the body back up: a queued attack/jump trigger or a held bool (gathering,
        // sitting, blocking) would otherwise win an Any State transition right after the death starts.
        if (anim != null)
            foreach (AnimatorControllerParameter p in anim.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Trigger && p.name != "DieTrigger") anim.ResetTrigger(p.nameHash);
                else if (p.type == AnimatorControllerParameterType.Bool && (p.name == "isGathering" || p.name == "isSitting" || p.name == "isBlocking" || p.name == "isAiming"))
                    anim.SetBool(p.nameHash, false);
            }
        if (HasAnimParameter("DieTrigger")) anim.SetTrigger("DieTrigger");

        // Death screen with "Wake up nearby" / "Respawn at tent"
        if (RespawnSystem.Instance != null) RespawnSystem.Instance.OnPlayerDied(this);
        else StartCoroutine(QuitAfterDelay());
    }

    void ApplyEnemyDeathPenalty()
    {
        currentSpirit = Mathf.Max(0f, currentSpirit) * 0.8f;
        PlayerInventoryManager inventory = PlayerInventoryManager.Instance;
        if (inventory == null) return;
        PlayerItemData coins = ItemDatabase.Find("coins");
        if (coins == null) return;
        int remainingLoss = inventory.CountItem(coins) / 5;
        // Work per stack, not per coin; refresh each affected slot only once.
        foreach (PlayerItemSlot slot in inventory.UsableSlots())
        {
            if (remainingLoss <= 0) break;
            if (slot.itemData != coins) continue;
            int removed = Mathf.Min(slot.amount, remainingLoss);
            int remaining = slot.amount - removed;
            if (remaining == 0) slot.ClearSlot();
            else slot.SetItem(coins, remaining);
            remainingLoss -= removed;
        }
    }

    // Brings the player back to life (called by RespawnSystem after moving the player)
    public void Revive(float healthPercent = 1f)
    {
        ClearSurvivalOnDeath();
        currentHealth = MaxHealth * Mathf.Clamp01(healthPercent);
        currentStamina = MaxStamina;
        currentHunger = Mathf.Max(currentHunger, MaxHunger * 0.3f);
        currentThirst = Mathf.Max(currentThirst, MaxThirst * 0.3f);
        // Enemy deaths keep the exact 20% loss, even below the old 30% respawn floor.
        if (!enemyDeathPenaltyApplied) currentSpirit = Mathf.Max(currentSpirit, MaxSpirit * 0.3f);
        deathHandled = false;
        enemyDeathPenaltyApplied = false;
        lastDamageTime = -999f;

        // Stand up again: the Animator goes back to its starting state (idle)
        if (anim != null)
        {
            anim.Rebind();
            anim.Update(0f);
        }

        foreach (PlayerMovement movement in GetComponentsInChildren<PlayerMovement>()) movement.enabled = true;
        foreach (PlayerCombat combat in GetComponentsInChildren<PlayerCombat>())
        {
            combat.enabled = true;
            combat.ResetCombatState(); // sword back in the sheath, out of combat mode (after the Animator was reset)
        }
        Debug.Log("Player respawned");
    }

    bool HasAnimParameter(string parameterName)
    {
        if (anim == null) return false;
        foreach (AnimatorControllerParameter p in anim.parameters)
            if (p.name == parameterName) return true;
        return false;
    }

    System.Collections.IEnumerator QuitAfterDelay()
    {
        yield return new WaitForSeconds(quitDelay);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}

public partial class PlayerStats
{
    SwordAbilityReserve swordReserve;
    public int SwordAbilityHits=>swordReserve.Hits;
    public bool SwordAbilityReady=>swordReserve.Ready;
    public void RestoreSwordAbilityHits(int value)=>swordReserve.Restore(value);
    public void RegisterSwordAbilityHit(){if(!IsDead)swordReserve.AddHit();}
    public bool SpendSwordAbility()=>!IsDead && swordReserve.Spend();
}

// Shared combat ownership. Death/loot events do not add special charge.
public static class CombatKillCredit
{
    public const ulong Nobody=ulong.MaxValue;
    public static ulong LocalOwner=>MultiplayerSession.IsOnline && NetworkManager.Singleton!=null
        ?NetworkManager.Singleton.LocalClientId:0;
}
// Called once for a positive authoritative health change, or its host confirmation on a client.
public static class CombatHitCredit
{
    public static void Award(ulong owner)
    {
        if(owner==CombatKillCredit.Nobody || owner!=CombatKillCredit.LocalOwner || WorldActivitySystem.Loading)return;
        PlayerRef.Stats?.RegisterSwordAbilityHit();
    }
}

public partial class PlayerStats
{
    [Header("Traveler vital orb")]
    [Tooltip("Split life/stamina sphere with a joined hunger, thirst and spirit housing.")]
    public bool useVitalOrbHUD=true;
    PlayerVitalHUD vitalHUD;
    void PresentVitalHUD(float alpha)
    {
        bool local=PlayerRef.Stats==this;
        if(!useVitalOrbHUD || !local){if(vitalHUD!=null)vitalHUD.Hide();return;}
        if(vitalHUD==null)vitalHUD=PlayerVitalHUD.Create();
        vitalHUD.Present(this,alpha);
    }
    void DestroyVitalHUD(){if(vitalHUD!=null)Destroy(vitalHUD.gameObject);vitalHUD=null;}
}
