// ==============================================================================
// RAGNAROK REBUILD - BOT CONFIGURATION SCHEMA SPECIFICATION
// ==============================================================================
// This file serves as the single source of truth for the Orchestrator config UI.
// To add a new setting to the config menu:
//   1. Add the property to BotConfigData in C# (RebuildBotPlugin/BotConfig.cs)
//   2. Add one descriptor object to CONFIG_SCHEMA below under the desired category.
// ==============================================================================

const CONFIG_CATEGORIES = [
  { id: 'combat', label: '⚔️ Combat & Movement', description: 'Attack, target radius, map destination, and wander controls' },
  { id: 'survival', label: '🛡️ Survival & Recovery', description: 'Potion triggers, emergency wings, sit recovery, and buffs' },
  { id: 'town', label: '🎒 Town & Inventory', description: 'Restock limits, weight thresholds, and loot storage/vendor rules' },
  { id: 'monsters', label: '👾 Monsters & Targeting', description: 'Priority monsters, whitelists, blacklists, and avoidance' },
  { id: 'progression', label: '📈 Progression & Class', description: 'Auto stat/skill allocation, job change, and bard rewards' },
  { id: 'system', label: '⚙️ System & Low-Spec', description: 'Reconnect handling, logging, FPS caps, and low-spec mode' }
];

const CONFIG_SCHEMA = [
  // --------------------------------------------------------------------------
  // Category: Combat & Movement
  // --------------------------------------------------------------------------
  {
    id: 'Enabled',
    label: 'Master Bot Automation',
    description: 'Master switch enabling or disabling all autonomous bot behaviors.',
    category: 'combat',
    type: 'boolean',
    default: true
  },
  {
    id: 'AutoAttack',
    label: 'Auto Attack',
    description: 'Automatically targets and engages nearby attackable monsters.',
    category: 'combat',
    type: 'boolean',
    default: true
  },
  {
    id: 'AutoLoot',
    label: 'Auto Loot',
    description: 'Walks to and collects item drops after or during combat.',
    category: 'combat',
    type: 'boolean',
    default: true
  },
  {
    id: 'AutoWander',
    label: 'Auto Wander / Explore',
    description: 'Wanders toward cold sectors and explores when no monsters are in range.',
    category: 'combat',
    type: 'boolean',
    default: true
  },
  {
    id: 'AutoTravel',
    label: 'Auto Travel To Target Map',
    description: 'Navigates and takes Kafra teleports/warps to reach the hunting map.',
    category: 'combat',
    type: 'boolean',
    default: true
  },
  {
    id: 'TargetMap',
    label: 'Hunting Target Map',
    description: 'Map ID for autonomous hunting (e.g. prt_fild08, pay_fild08, moc_fild07).',
    category: 'combat',
    type: 'string',
    default: 'prt_fild08'
  },
  {
    id: 'SearchRadius',
    label: 'Monster Search Radius',
    description: 'Distance (in tiles) to scan for candidate targets.',
    category: 'combat',
    type: 'number',
    min: 5,
    max: 40,
    step: 1,
    unit: 'tiles',
    default: 18.0
  },
  {
    id: 'AttackCooldownSeconds',
    label: 'Attack Action Cooldown',
    description: 'Minimum delay between combat packet dispatches.',
    category: 'combat',
    type: 'number',
    min: 0.1,
    max: 2.0,
    step: 0.05,
    unit: 's',
    default: 0.4
  },
  {
    id: 'LootCooldownSeconds',
    label: 'Loot Collection Cooldown',
    description: 'Delay between pickup attempts on dropped items.',
    category: 'combat',
    type: 'number',
    min: 0.1,
    max: 2.0,
    step: 0.05,
    unit: 's',
    default: 0.3
  },
  {
    id: 'WanderCooldownSeconds',
    label: 'Wander Step Interval',
    description: 'Time between generating exploratory wander steps when idle.',
    category: 'combat',
    type: 'number',
    min: 1.0,
    max: 15.0,
    step: 0.5,
    unit: 's',
    default: 4.0
  },
  {
    id: 'WanderRadius',
    label: 'Local Wander Radius',
    description: 'Local tile search radius when picking immediate wander positions.',
    category: 'combat',
    type: 'number',
    min: 2,
    max: 25,
    step: 1,
    unit: 'tiles',
    default: 8
  },
  {
    id: 'AvoidPortalsWhileWandering',
    label: 'Avoid Warp Portals While Wandering',
    description: 'Prevents accidentally stepping into map transitions while hunting.',
    category: 'combat',
    type: 'boolean',
    default: true
  },
  {
    id: 'PortalSafetyRadius',
    label: 'Portal Clearance Radius',
    description: 'Buffer distance maintained around map portals to avoid boundary traps.',
    category: 'combat',
    type: 'number',
    min: 1.0,
    max: 15.0,
    step: 0.5,
    unit: 'tiles',
    default: 5.0
  },
  {
    id: 'SkillRules',
    label: 'Combat Skill Rules & Rotations',
    description: 'Autonomous combat casting rotation, openers, buffs, emergency heals, and AOE mob cluster skills.',
    category: 'combat',
    type: 'skill-rules-builder',
    default: []
  },

  // --------------------------------------------------------------------------
  // Category: Survival & Recovery
  // --------------------------------------------------------------------------
  {
    id: 'AutoPotion',
    label: 'Auto Consume HP Potions',
    description: 'Uses inventory recovery potions when HP drops below threshold.',
    category: 'survival',
    type: 'boolean',
    default: true
  },
  {
    id: 'HpPotionPercent',
    label: 'HP Potion Trigger %',
    description: 'Consumes an HP potion when health drops below this percentage.',
    category: 'survival',
    type: 'percent',
    min: 10,
    max: 95,
    step: 1,
    unit: '%',
    default: 70
  },
  {
    id: 'EmergencyFlyWingOnLowHp',
    label: 'Emergency Fly Wing on Critical HP',
    description: 'Uses a Fly Wing to escape combat when HP reaches dangerous levels.',
    category: 'survival',
    type: 'boolean',
    default: true
  },
  {
    id: 'EmergencyFlyWingHpPercent',
    label: 'Emergency Wing HP Trigger %',
    description: 'HP threshold at which emergency teleportation triggers.',
    category: 'survival',
    type: 'percent',
    min: 5,
    max: 60,
    step: 1,
    unit: '%',
    default: 20
  },
  {
    id: 'FlyWingCooldownSeconds',
    label: 'Fly Wing Cooldown',
    description: 'Enforces a safety cooldown between consecutive Fly Wing teleports.',
    category: 'survival',
    type: 'number',
    min: 0.5,
    max: 5.0,
    step: 0.1,
    unit: 's',
    default: 1.5
  },
  {
    id: 'AutoSitToRecover',
    label: 'Auto Sit to Natural Recover',
    description: 'Commands character to sit when HP is low and standing idle.',
    category: 'survival',
    type: 'boolean',
    default: true
  },
  {
    id: 'SitHpPercent',
    label: 'Sit HP Threshold %',
    description: 'Sits down to regenerate HP when below this percentage.',
    category: 'survival',
    type: 'percent',
    min: 10,
    max: 75,
    step: 1,
    unit: '%',
    default: 30
  },
  {
    id: 'StandHpPercent',
    label: 'Stand Up HP Threshold %',
    description: 'Stands back up and resumes hunting once HP recovers to this percentage.',
    category: 'survival',
    type: 'percent',
    min: 50,
    max: 100,
    step: 1,
    unit: '%',
    default: 90
  },
  {
    id: 'AutoAspdPotion',
    label: 'Auto Maintain ASPD Buff Potion',
    description: 'Maintains Awakening, Concentration, or Berserk potion attack speed buffs.',
    category: 'survival',
    type: 'boolean',
    default: true
  },
  {
    id: 'AspdPotionPreference',
    label: 'ASPD Potion Preference',
    description: 'Selects the preferred potion tier or auto-detects based on job/level.',
    category: 'survival',
    type: 'select',
    options: ['Auto', 'Concentration_Potion', 'Awakening_Potion', 'Berserk_Potion'],
    default: 'Auto'
  },

  // --------------------------------------------------------------------------
  // Category: Town Routine & Loot
  // --------------------------------------------------------------------------
  {
    id: 'AutoReturnToBaseOnWeight',
    label: 'Return to Town on Overweight',
    description: 'Teleports to town via Butterfly Wing when inventory weight exceeds limit.',
    category: 'town',
    type: 'boolean',
    default: true
  },
  {
    id: 'ReturnToBaseWeightPercent',
    label: 'Overweight Trigger %',
    description: 'Weight percentage (usually 85–90%) that triggers town routine.',
    category: 'town',
    type: 'percent',
    min: 50,
    max: 95,
    step: 1,
    unit: '%',
    default: 90
  },
  {
    id: 'AutoReturnOnOutOfHpItems',
    label: 'Return to Town on Zero Supplies',
    description: 'Initiates town restock if out of potions or essential ammo.',
    category: 'town',
    type: 'boolean',
    default: true
  },
  {
    id: 'AutoRestock',
    label: 'Auto Restock from Kafra / Vendors',
    description: 'Purchases or withdraws necessary consumable supplies in town.',
    category: 'town',
    type: 'boolean',
    default: true
  },
  {
    id: 'AutoRestockOnLowSupplies',
    label: 'Restock Proactively on Low Supplies',
    description: 'Restocks supplies during any town visit if inventory is below target counts.',
    category: 'town',
    type: 'boolean',
    default: true
  },
  {
    id: 'AutoEquipBestArrow',
    label: 'Auto Equip Highest Tier Arrow',
    description: 'Equips best available elemental or standard arrows automatically for archers.',
    category: 'town',
    type: 'boolean',
    default: true
  },
  {
    id: 'MinArrowCount',
    label: 'Minimum Arrow Quota',
    description: 'Triggers town arrow restock if inventory arrow count drops below this.',
    category: 'town',
    type: 'number',
    min: 10,
    max: 500,
    step: 10,
    unit: 'arrows',
    default: 30
  },
  {
    id: 'AutoEquipEmptySlots',
    label: 'Auto Equip Gear on Empty Slots',
    description: 'Automatically equips newly acquired equipment if corresponding slot is empty.',
    category: 'town',
    type: 'boolean',
    default: true
  },
  {
    id: 'RestockTargets',
    label: 'Supply Restock Quotas',
    description: 'Target inventory quantities for potions, wings, and ammo during town routine.',
    category: 'town',
    type: 'stepper-table',
    default: {
      "Fly_Wing": 100,
      "Butterfly_Wing": 5,
      "Red_Potion": 50,
      "Concentration_Potion": 3
    }
  },
  {
    id: 'ItemRules',
    label: 'Item Management Rules (Sell / Store / Keep)',
    description: 'Configures whether items are sold to NPC vendors, deposited in Kafra storage, or kept in inventory.',
    category: 'town',
    type: 'item-rules-table',
    default: {}
  },

  // --------------------------------------------------------------------------
  // Category: Monsters & Targeting
  // --------------------------------------------------------------------------
  {
    id: 'PrioritizeAggressiveMonsters',
    label: 'Prioritize Aggressive Monsters',
    description: 'Attacks monsters that are actively hitting the player or casting spells first.',
    category: 'monsters',
    type: 'boolean',
    default: true
  },
  {
    id: 'AutoAvoidMonsters',
    label: 'Avoid Dangerous Monsters',
    description: 'Uses a Fly Wing or flees when dangerous monsters from the Monster Avoidance List appear nearby.',
    category: 'monsters',
    type: 'boolean',
    default: true
  },
  {
    id: 'PriorityMonsterList',
    label: 'High Priority Target Names',
    description: 'Targets these monsters with highest priority before any others on the map.',
    category: 'monsters',
    type: 'tag-list',
    placeholder: 'Add monster name (e.g. Thief Bug, Poring)...',
    default: []
  },
  {
    id: 'TargetMonsterWhitelist',
    label: 'Target Whitelist (Exclusive)',
    description: 'If populated, ONLY monsters listed here will ever be targeted.',
    category: 'monsters',
    type: 'tag-list',
    placeholder: 'Add whitelist monster name...',
    default: []
  },
  {
    id: 'TargetMonsterBlacklist',
    label: 'Target Blacklist (Ignore)',
    description: 'Bot will completely ignore these monsters and never attack them.',
    category: 'monsters',
    type: 'tag-list',
    placeholder: 'Add blacklist monster name...',
    default: []
  },
  {
    id: 'MonsterAvoidanceList',
    label: 'Dangerous Monsters to Flee From',
    description: 'Bot immediately runs away or uses a Fly Wing if these monsters appear.',
    category: 'monsters',
    type: 'tag-list',
    placeholder: 'Add dangerous monster (e.g. Ghostring, Baphomet)...',
    default: []
  },

  // --------------------------------------------------------------------------
  // Category: Progression & Class
  // --------------------------------------------------------------------------
  {
    id: 'AutoStatAllocation',
    label: 'Auto Allocate Stat Points',
    description: 'Automatically distributes status points according to character build goals.',
    category: 'progression',
    type: 'boolean',
    default: true
  },
  {
    id: 'StatBuildPlan',
    label: 'Sequential Stat Point Allocation Plan',
    description: 'Ordered list of stat milestone targets to fulfill step-by-step as points become available.',
    category: 'progression',
    type: 'stat-plan-builder',
    default: []
  },
  {
    id: 'AutoSkillAllocation',
    label: 'Auto Allocate Skill Points',
    description: 'Automatically learns and levels skills according to skill build goals.',
    category: 'progression',
    type: 'boolean',
    default: true
  },
  {
    id: 'SkillBuildPlan',
    label: 'Sequential Skill Leveling Plan',
    description: 'Ordered list of skill point allocation targets to level up step-by-step.',
    category: 'progression',
    type: 'skill-plan-builder',
    default: []
  },
  {
    id: 'AutoJobChange',
    label: 'Auto Complete First Job Change',
    description: 'Visits guild NPC and changes from Novice to target 1st class upon Job 10.',
    category: 'progression',
    type: 'boolean',
    default: true
  },
  {
    id: 'TargetJob',
    label: 'Target 1st Class Job',
    description: 'Job class to transition into when reaching Job Level 10.',
    category: 'progression',
    type: 'select',
    options: ['Swordman', 'Archer', 'Mage', 'Thief', 'Merchant', 'Acolyte'],
    default: 'Swordman'
  },
  {
    id: 'AutoClaimBardGifts',
    label: 'Auto Claim Bard Quest Gifts',
    description: 'Automatically speaks with Roaming Bards in towns to collect free rewards.',
    category: 'progression',
    type: 'boolean',
    default: true
  },

  // --------------------------------------------------------------------------
  // Category: System & Low-Spec
  // --------------------------------------------------------------------------
  {
    id: 'VerboseLogging',
    label: 'Verbose Debug Logging',
    description: 'Outputs detailed diagnostic telemetry into bot.log and terminal.',
    category: 'system',
    type: 'boolean',
    default: false
  },
  {
    id: 'AutoRespawn',
    label: 'Auto Respawn on Death',
    description: 'Automatically clicks Return to Save Point upon character death.',
    category: 'system',
    type: 'boolean',
    default: true
  },
  {
    id: 'AutoReconnect',
    label: 'Auto Reconnect on Disconnect',
    description: 'Automatically handles login screen and re-enters the game on server disconnects.',
    category: 'system',
    type: 'boolean',
    default: true
  },
  {
    id: 'AutoReconnectDelaySeconds',
    label: 'Reconnect Cooldown Delay',
    description: 'Wait time before attempting login reconnection.',
    category: 'system',
    type: 'number',
    min: 1.0,
    max: 30.0,
    step: 0.5,
    unit: 's',
    default: 4.0
  },
  {
    id: 'MaxReconnectAttempts',
    label: 'Max Reconnect Attempts',
    description: 'Stops attempting reconnection if server rejects this many consecutive times.',
    category: 'system',
    type: 'number',
    min: 1,
    max: 50,
    step: 1,
    default: 10
  },
  {
    id: 'PreferredCharacterSlot',
    label: 'Preferred Character Slot',
    description: 'Slot index on character select screen (-1 uses account profile default).',
    category: 'system',
    type: 'number',
    min: -1,
    max: 5,
    step: 1,
    default: -1
  },
  {
    id: 'LowSpecMode',
    label: 'Enable Low-Spec GPU Mode',
    description: 'Culls camera render layers and limits overhead to minimize GPU usage.',
    category: 'system',
    type: 'boolean',
    default: false
  },
  {
    id: 'TargetFrameRate',
    label: 'Target Framerate Cap',
    description: 'Caps Unity Application.targetFrameRate (10 FPS recommended for background bots).',
    category: 'system',
    type: 'number',
    min: 5,
    max: 60,
    step: 5,
    unit: 'FPS',
    default: 10
  },
  {
    id: 'MuteAudioInLowSpec',
    label: 'Mute Audio in Low-Spec Mode',
    description: 'Disables Unity audio listeners to eliminate audio thread CPU cycles.',
    category: 'system',
    type: 'boolean',
    default: true
  },
  {
    id: 'DisableRenderingInLowSpec',
    label: '0% GPU Culling in Low-Spec',
    description: 'Completely disables camera rendering pipeline for near-zero GPU utilization.',
    category: 'system',
    type: 'boolean',
    default: true
  }
];
