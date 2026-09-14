/**
 * Add Bot Modal Controller
 * Handles account credentials, character creation attributes, starting stat budget (33 points),
 * job presets, and sequential stat/skill build targets.
 */

let addBotAccountsCache = [];
let addBotStats = { str: 5, agi: 5, vit: 5, int: 5, dex: 5, luk: 8 };
let addBotStatPlan = [];
let addBotSkillPlan = [];
let addBotActiveTab = 'stats'; // 'stats' or 'skills'

const JOB_ARCHETYPE_PRESETS = {
  Archer: {
    stats: { str: 5, agi: 9, vit: 5, int: 4, dex: 9, luk: 1 },
    statPlan: [
      { Stat: 'Dex', Target: 30 },
      { Stat: 'Agi', Target: 30 },
      { Stat: 'Dex', Target: 50 },
      { Stat: 'Agi', Target: 60 },
      { Stat: 'Dex', Target: 70 },
      { Stat: 'Agi', Target: 80 },
      { Stat: 'Dex', Target: 90 }
    ],
    skillPlan: [
      { Skill: "Owl's Eye", Target: 10 },
      { Skill: "Vulture's Eye", Target: 10 },
      { Skill: "Improve Concentration", Target: 10 },
      { Skill: "Double Strafe", Target: 10 },
      { Skill: "Arrow Shower", Target: 9 }
    ]
  },
  Mage: {
    stats: { str: 1, agi: 5, vit: 5, int: 9, dex: 9, luk: 4 },
    statPlan: [
      { Stat: 'Int', Target: 30 },
      { Stat: 'Dex', Target: 20 },
      { Stat: 'Int', Target: 50 },
      { Stat: 'Dex', Target: 40 },
      { Stat: 'Int', Target: 70 },
      { Stat: 'Dex', Target: 60 },
      { Stat: 'Int', Target: 90 }
    ],
    skillPlan: [
      { Skill: "Increase SP Recovery", Target: 10 },
      { Skill: "Cold Bolt", Target: 5 },
      { Skill: "Frost Diver", Target: 10 },
      { Skill: "Fire Bolt", Target: 10 },
      { Skill: "Sight", Target: 1 },
      { Skill: "Fire Ball", Target: 5 },
      { Skill: "Fire Wall", Target: 10 }
    ]
  },
  Swordsman: {
    stats: { str: 9, agi: 9, vit: 5, int: 1, dex: 8, luk: 1 },
    statPlan: [
      { Stat: 'Dex', Target: 20 },
      { Stat: 'Str', Target: 30 },
      { Stat: 'Agi', Target: 40 },
      { Stat: 'Str', Target: 50 },
      { Stat: 'Agi', Target: 60 },
      { Stat: 'Str', Target: 70 },
      { Stat: 'Agi', Target: 80 }
    ],
    skillPlan: [
      { Skill: "Sword Mastery", Target: 10 },
      { Skill: "Improved HP Recovery", Target: 10 },
      { Skill: "Bash", Target: 10 },
      { Skill: "Magnum Break", Target: 10 },
      { Skill: "Endure", Target: 9 }
    ]
  },
  Acolyte: {
    stats: { str: 1, agi: 1, vit: 9, int: 9, dex: 9, luk: 4 },
    statPlan: [
      { Stat: 'Int', Target: 30 },
      { Stat: 'Dex', Target: 20 },
      { Stat: 'Vit', Target: 30 },
      { Stat: 'Int', Target: 60 },
      { Stat: 'Dex', Target: 50 },
      { Stat: 'Vit', Target: 50 },
      { Stat: 'Int', Target: 80 }
    ],
    skillPlan: [
      { Skill: "Heal", Target: 10 },
      { Skill: "Increase Agility", Target: 10 },
      { Skill: "Blessing", Target: 10 },
      { Skill: "Divine Protection", Target: 5 },
      { Skill: "Demon Bane", Target: 5 },
      { Skill: "Ruwach", Target: 1 },
      { Skill: "Teleport", Target: 2 },
      { Skill: "Warp Portal", Target: 4 },
      { Skill: "Pneuma", Target: 1 }
    ]
  },
  Thief: {
    stats: { str: 8, agi: 9, vit: 4, int: 1, dex: 9, luk: 2 },
    statPlan: [
      { Stat: 'Dex', Target: 20 },
      { Stat: 'Agi', Target: 30 },
      { Stat: 'Str', Target: 30 },
      { Stat: 'Agi', Target: 50 },
      { Stat: 'Str', Target: 50 },
      { Stat: 'Agi', Target: 70 },
      { Stat: 'Str', Target: 70 }
    ],
    skillPlan: [
      { Skill: "Double Attack", Target: 10 },
      { Skill: "Improve Dodge", Target: 10 },
      { Skill: "Steal", Target: 10 },
      { Skill: "Hiding", Target: 10 },
      { Skill: "Envenom", Target: 9 }
    ]
  },
  Merchant: {
    stats: { str: 9, agi: 8, vit: 6, int: 1, dex: 8, luk: 1 },
    statPlan: [
      { Stat: 'Dex', Target: 20 },
      { Stat: 'Str', Target: 30 },
      { Stat: 'Agi', Target: 30 },
      { Stat: 'Str', Target: 50 },
      { Stat: 'Agi', Target: 50 },
      { Stat: 'Str', Target: 70 },
      { Stat: 'Agi', Target: 70 }
    ],
    skillPlan: [
      { Skill: "Enlarge Weight Limit", Target: 10 },
      { Skill: "Discount", Target: 10 },
      { Skill: "Overcharge", Target: 10 },
      { Skill: "Push Cart", Target: 10 },
      { Skill: "Item Appraisal", Target: 1 },
      { Skill: "Mammonite", Target: 8 }
    ]
  },
  Novice: {
    stats: { str: 5, agi: 5, vit: 5, int: 5, dex: 5, luk: 8 },
    statPlan: [
      { Stat: 'Dex', Target: 15 },
      { Stat: 'Str', Target: 15 }
    ],
    skillPlan: [
      { Skill: "Basic Mastery", Target: 9 },
      { Skill: "First Aid", Target: 1 }
    ]
  }
};

/**
 * Open the Add Bot Modal and fetch accounts.
 */
async function openAddBotModal() {
  const modal = document.getElementById('add-bot-modal');
  if (!modal) return;

  clearAddBotError();
  document.getElementById('addbot-char-name').value = '';
  document.getElementById('addbot-gender-male').checked = true;

  // Default target job: Archer or Novice
  const targetJobSelect = document.getElementById('addbot-target-job');
  if (targetJobSelect) {
    targetJobSelect.value = 'Archer';
    applyJobArchetypePreset('Archer', false);
  }

  modal.classList.add('open');
  populateSkillsDatalist();
  await loadAddBotAccounts();
}

/**
 * Populate skills datalist for autocomplete
 */
function populateSkillsDatalist() {
  const datalist = document.getElementById('addbot-skills-datalist');
  if (!datalist) return;
  const skills = window.ALL_REBUILD_SKILLS || [];
  datalist.innerHTML = skills.map(s => `<option value="${s.name}">${s.job} - ${s.name}</option>`).join('');
}

/**
 * Close the modal.
 */
function closeAddBotModal() {
  const modal = document.getElementById('add-bot-modal');
  if (modal) modal.classList.remove('open');
}

/**
 * Fetch existing accounts from server to populate dropdown.
 */
async function loadAddBotAccounts() {
  const select = document.getElementById('addbot-account-select');
  if (!select) return;

  try {
    const res = await fetch('/api/accounts');
    if (res.ok) {
      addBotAccountsCache = await res.json();
    }
  } catch (err) {
    console.error('Failed to load accounts:', err);
  }

  select.innerHTML = '';

  if (addBotAccountsCache && addBotAccountsCache.length > 0) {
    addBotAccountsCache.forEach(acc => {
      const freeSlots = 3 - (acc.characters ? acc.characters.length : 0);
      const opt = document.createElement('option');
      opt.value = acc.accountId;
      opt.textContent = `${acc.accountId} (${freeSlots > 0 ? freeSlots + ' slot' + (freeSlots > 1 ? 's' : '') + ' free' : 'Full (3/3)'})`;
      opt.dataset.slotsFree = freeSlots;
      if (freeSlots <= 0) opt.disabled = true;
      select.appendChild(opt);
    });
  }

  // Add option for creating a brand new account
  const newAccOpt = document.createElement('option');
  newAccOpt.value = '__new__';
  newAccOpt.textContent = '+ Create New Account...';
  select.appendChild(newAccOpt);

  // Default selection
  const availableAcc = addBotAccountsCache.find(a => (3 - (a.characters ? a.characters.length : 0)) > 0);
  if (availableAcc) {
    select.value = availableAcc.accountId;
  } else {
    select.value = '__new__';
  }

  onAccountSelectionChanged();
}

/**
 * Handle account dropdown change.
 */
function onAccountSelectionChanged() {
  const select = document.getElementById('addbot-account-select');
  const accIdInput = document.getElementById('addbot-account-id');
  const passInput = document.getElementById('addbot-password');
  const slotHint = document.getElementById('addbot-slot-hint');

  if (!select || !accIdInput || !passInput) return;

  const selectedVal = select.value;
  if (selectedVal === '__new__') {
    accIdInput.disabled = false;
    accIdInput.value = '';
    accIdInput.placeholder = 'e.g. BotAccount03';
    passInput.disabled = false;
    passInput.value = '';
    passInput.placeholder = 'Account Password';
    if (slotHint) slotHint.textContent = 'Will create Slot 0 on new account.';
  } else {
    const acc = addBotAccountsCache.find(a => a.accountId.toLowerCase() === selectedVal.toLowerCase());
    accIdInput.disabled = true;
    accIdInput.value = selectedVal;
    passInput.disabled = true;
    passInput.value = '••••••••'; // Existing accounts manage passwords safely
    passInput.placeholder = '(Kept from accounts.json)';

    if (slotHint) {
      if (acc) {
        const nextSlot = acc.nextAvailableSlot >= 0 ? acc.nextAvailableSlot : 'None';
        const charCount = acc.characters ? acc.characters.length : 0;
        slotHint.textContent = `Character will be placed in next open slot: Slot ${nextSlot} (${charCount}/3 characters).`;
      } else {
        slotHint.textContent = '';
      }
    }
  }
}

/**
 * Handle Target Job selection to prepopulate recommended stats and builds.
 */
function onTargetJobChanged() {
  const select = document.getElementById('addbot-target-job');
  if (!select) return;
  applyJobArchetypePreset(select.value, true);
}

/**
 * Apply job preset stats, stat plan, and skill plan.
 */
function applyJobArchetypePreset(jobName, notify = true) {
  const preset = JOB_ARCHETYPE_PRESETS[jobName] || JOB_ARCHETYPE_PRESETS.Novice;

  // Clone stats
  addBotStats = { ...preset.stats };
  renderAddBotStats();

  // Clone build plans
  addBotStatPlan = preset.statPlan ? JSON.parse(JSON.stringify(preset.statPlan)) : [];
  addBotSkillPlan = preset.skillPlan ? JSON.parse(JSON.stringify(preset.skillPlan)) : [];

  renderAddBotBuildPlanTables();
}

/**
 * Stat Stepper & Budget Management
 */
function updateStartingStat(statKey, delta) {
  const currentVal = addBotStats[statKey] || 1;
  const newVal = Math.min(9, Math.max(1, currentVal + delta));

  // Check if incrementing exceeds 33 points
  if (delta > 0) {
    const totalCurrent = calculateStatsTotal();
    if (totalCurrent >= 33 && newVal > currentVal) {
      return; // Cannot exceed budget
    }
  }

  addBotStats[statKey] = newVal;
  renderAddBotStats();
}

function onStartingStatInput(statKey, rawVal) {
  let val = parseInt(rawVal, 10);
  if (isNaN(val)) val = 1;
  val = Math.min(9, Math.max(1, val));
  addBotStats[statKey] = val;
  renderAddBotStats();
}

function calculateStatsTotal() {
  return (addBotStats.str || 0) +
         (addBotStats.agi || 0) +
         (addBotStats.vit || 0) +
         (addBotStats.int || 0) +
         (addBotStats.dex || 0) +
         (addBotStats.luk || 0);
}

function renderAddBotStats() {
  const stats = ['str', 'agi', 'vit', 'int', 'dex', 'luk'];
  stats.forEach(s => {
    const input = document.getElementById(`addbot-stat-${s}`);
    if (input) input.value = addBotStats[s] || 1;
  });

  const total = calculateStatsTotal();
  const remaining = 33 - total;
  const badge = document.getElementById('addbot-budget-badge');
  const btnCreate = document.getElementById('btn-submit-add-bot');

  if (badge) {
    if (remaining === 0) {
      badge.className = 'badge-pill badge-emerald';
      badge.innerHTML = `<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><polyline points="20 6 9 17 4 12"/></svg> 33 / 33 Points Allocated`;
    } else if (remaining > 0) {
      badge.className = 'badge-pill badge-amber';
      badge.textContent = `${remaining} Point${remaining > 1 ? 's' : ''} Remaining`;
    } else {
      badge.className = 'badge-pill badge-rose';
      badge.textContent = `Over Budget by ${Math.abs(remaining)}`;
    }
  }

  if (btnCreate) {
    btnCreate.disabled = remaining !== 0;
  }
}

/**
 * Tab switching for Build Targets
 */
function switchAddBotTab(tabName) {
  addBotActiveTab = tabName;
  const btnStats = document.getElementById('addbot-tab-btn-stats');
  const btnSkills = document.getElementById('addbot-tab-btn-skills');
  const panelStats = document.getElementById('addbot-tab-panel-stats');
  const panelSkills = document.getElementById('addbot-tab-panel-skills');

  if (btnStats) btnStats.classList.toggle('active', tabName === 'stats');
  if (btnSkills) btnSkills.classList.toggle('active', tabName === 'skills');
  if (panelStats) panelStats.style.display = tabName === 'stats' ? 'block' : 'none';
  if (panelSkills) panelSkills.style.display = tabName === 'skills' ? 'block' : 'none';
}

/**
 * Render Stat & Skill Build Plan Tables
 */
function renderAddBotBuildPlanTables() {
  renderAddBotStatTable();
  renderAddBotSkillTable();
}

function renderAddBotStatTable() {
  const tbody = document.getElementById('addbot-stat-table-body');
  if (!tbody) return;

  const statsList = ['Str', 'Agi', 'Vit', 'Int', 'Dex', 'Luk'];

  if (addBotStatPlan.length === 0) {
    tbody.innerHTML = `
      <tr>
        <td colspan="5" style="text-align: center; color: var(--text-muted); padding: 16px;">
          No sequential milestones configured. Add milestones below.
        </td>
      </tr>
    `;
    return;
  }

  tbody.innerHTML = addBotStatPlan.map((step, idx) => `
    <tr>
      <td><span class="step-badge">#${idx + 1}</span></td>
      <td>
        <select class="tremor-select" style="padding: 4px 8px; font-weight: 700; width: 100%;" onchange="updateAddBotStatStep(${idx}, 'Stat', this.value)">
          ${statsList.map(s => `
            <option value="${s}" ${step.Stat && step.Stat.toLowerCase() === s.toLowerCase() ? 'selected' : ''}>${s.toUpperCase()}</option>
          `).join('')}
        </select>
      </td>
      <td>
        <div style="display: flex; align-items: center; gap: 8px;">
          <span style="color: var(--text-muted); font-size: 0.75rem;">Reach</span>
          <input type="number" 
                 class="config-table-input" 
                 style="width: 80px;" 
                 value="${step.Target || 1}" 
                 min="1" max="99" 
                 onchange="updateAddBotStatStep(${idx}, 'Target', parseInt(this.value, 10) || 1)">
          <span style="color: var(--text-muted); font-size: 0.75rem;">points</span>
        </div>
      </td>
      <td style="text-align: center;">
        <button type="button" class="btn-reorder" onclick="moveAddBotStatStep(${idx}, -1)" ${idx === 0 ? 'disabled style="opacity:0.3; cursor:default;"' : ''}>▲</button>
        <button type="button" class="btn-reorder" onclick="moveAddBotStatStep(${idx}, 1)" ${idx === addBotStatPlan.length - 1 ? 'disabled style="opacity:0.3; cursor:default;"' : ''}>▼</button>
      </td>
      <td style="text-align: center;">
        <button type="button" class="btn-delete-row" onclick="deleteAddBotStatStep(${idx})">&times;</button>
      </td>
    </tr>
  `).join('');
}

function updateAddBotStatStep(idx, prop, val) {
  if (addBotStatPlan[idx]) {
    addBotStatPlan[idx][prop] = val;
  }
}

function moveAddBotStatStep(idx, direction) {
  const newIdx = idx + direction;
  if (newIdx < 0 || newIdx >= addBotStatPlan.length) return;
  const item = addBotStatPlan.splice(idx, 1)[0];
  addBotStatPlan.splice(newIdx, 0, item);
  renderAddBotStatTable();
}

function deleteAddBotStatStep(idx) {
  addBotStatPlan.splice(idx, 1);
  renderAddBotStatTable();
}

function addNewAddBotStatStep() {
  const statSelect = document.getElementById('addbot-new-stat-picker');
  const targetInput = document.getElementById('addbot-new-stat-target');
  if (!statSelect || !targetInput) return;

  const stat = statSelect.value;
  const target = parseInt(targetInput.value, 10) || 20;

  addBotStatPlan.push({ Stat: stat, Target: target });
  renderAddBotStatTable();
}

/**
 * Skill Build Plan Builder
 */
function renderAddBotSkillTable() {
  const tbody = document.getElementById('addbot-skill-table-body');
  if (!tbody) return;

  const skills = window.ALL_REBUILD_SKILLS || [];

  if (addBotSkillPlan.length === 0) {
    tbody.innerHTML = `
      <tr>
        <td colspan="5" style="text-align: center; color: var(--text-muted); padding: 16px;">
          No skill progression configured. Add skills below.
        </td>
      </tr>
    `;
    return;
  }

  tbody.innerHTML = addBotSkillPlan.map((step, idx) => `
    <tr>
      <td><span class="step-badge">#${idx + 1}</span></td>
      <td>
        <input type="text" 
               class="config-table-input" 
               style="width: 100%; font-weight: 600;" 
               value="${step.Skill || ''}" 
               placeholder="Skill name..."
               list="addbot-skills-datalist"
               onchange="updateAddBotSkillStep(${idx}, 'Skill', this.value)">
      </td>
      <td>
        <div style="display: flex; align-items: center; gap: 8px;">
          <span style="color: var(--text-muted); font-size: 0.75rem;">Level</span>
          <input type="number" 
                 class="config-table-input" 
                 style="width: 70px;" 
                 value="${step.Target || 1}" 
                 min="1" max="10" 
                 onchange="updateAddBotSkillStep(${idx}, 'Target', parseInt(this.value, 10) || 1)">
        </div>
      </td>
      <td style="text-align: center;">
        <button type="button" class="btn-reorder" onclick="moveAddBotSkillStep(${idx}, -1)" ${idx === 0 ? 'disabled style="opacity:0.3; cursor:default;"' : ''}>▲</button>
        <button type="button" class="btn-reorder" onclick="moveAddBotSkillStep(${idx}, 1)" ${idx === addBotSkillPlan.length - 1 ? 'disabled style="opacity:0.3; cursor:default;"' : ''}>▼</button>
      </td>
      <td style="text-align: center;">
        <button type="button" class="btn-delete-row" onclick="deleteAddBotSkillStep(${idx})">&times;</button>
      </td>
    </tr>
  `).join('');
}

function updateAddBotSkillStep(idx, prop, val) {
  if (addBotSkillPlan[idx]) {
    addBotSkillPlan[idx][prop] = val;
  }
}

function moveAddBotSkillStep(idx, direction) {
  const newIdx = idx + direction;
  if (newIdx < 0 || newIdx >= addBotSkillPlan.length) return;
  const item = addBotSkillPlan.splice(idx, 1)[0];
  addBotSkillPlan.splice(newIdx, 0, item);
  renderAddBotSkillTable();
}

function deleteAddBotSkillStep(idx) {
  addBotSkillPlan.splice(idx, 1);
  renderAddBotSkillTable();
}

function addNewAddBotSkillStep() {
  const nameInput = document.getElementById('addbot-new-skill-name');
  const levelInput = document.getElementById('addbot-new-skill-level');
  if (!nameInput || !levelInput) return;

  const skill = nameInput.value.trim();
  const target = parseInt(levelInput.value, 10) || 1;

  if (!skill) return;

  addBotSkillPlan.push({ Skill: skill, Target: target });
  nameInput.value = '';
  renderAddBotSkillTable();
}

/**
 * Form Submission
 */
async function submitAddBot() {
  clearAddBotError();

  const select = document.getElementById('addbot-account-select');
  const accIdInput = document.getElementById('addbot-account-id');
  const passInput = document.getElementById('addbot-password');
  const charNameInput = document.getElementById('addbot-char-name');
  const isMale = document.getElementById('addbot-gender-male').checked;
  const jobSelect = document.getElementById('addbot-target-job');
  const btnSubmit = document.getElementById('btn-submit-add-bot');

  const isNewAccount = select.value === '__new__';
  const accountId = (isNewAccount ? accIdInput.value : select.value).trim();
  const password = passInput.value.trim();
  const charName = charNameInput.value.trim();
  const gender = isMale ? 'Male' : 'Female';
  const targetJob = jobSelect ? jobSelect.value : 'Novice';

  // Client-side validations
  if (!accountId) {
    showAddBotError('Please provide an Account ID.');
    accIdInput.focus();
    return;
  }

  if (isNewAccount && !password) {
    showAddBotError('Please enter a password for the new account.');
    passInput.focus();
    return;
  }

  if (!charName) {
    showAddBotError('Please enter a Character Name.');
    charNameInput.focus();
    return;
  }

  if (charName.length < 2 || charName.length > 30) {
    showAddBotError('Character name must be between 2 and 30 characters.');
    charNameInput.focus();
    return;
  }

  const statTotal = calculateStatsTotal();
  if (statTotal !== 33) {
    showAddBotError(`Starting stats must equal exactly 33 points (currently allocated: ${statTotal}).`);
    return;
  }

  const startingStatsArr = [
    addBotStats.str || 1,
    addBotStats.agi || 1,
    addBotStats.vit || 1,
    addBotStats.int || 1,
    addBotStats.dex || 1,
    addBotStats.luk || 1
  ];

  const payload = {
    accountId: accountId,
    password: isNewAccount ? password : '',
    isNewAccount: isNewAccount,
    characterName: charName,
    characterSlot: -1, // Auto-assign next open slot
    gender: gender,
    startingStats: startingStatsArr,
    targetJob: targetJob,
    statBuildPlan: addBotStatPlan,
    skillBuildPlan: addBotSkillPlan
  };

  btnSubmit.disabled = true;
  btnSubmit.textContent = 'Creating Bot...';

  try {
    const res = await fetch('/api/fleet/add-bot', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    const data = await res.json();
    if (res.ok && data.success) {
      closeAddBotModal();
      // Trigger UI refresh
      if (typeof fetchFleetOverview === 'function') {
        fetchFleetOverview();
      }
    } else {
      showAddBotError(data.error || 'Failed to create bot profile.');
    }
  } catch (err) {
    showAddBotError('Network error while creating bot: ' + err.message);
  } finally {
    btnSubmit.disabled = false;
    btnSubmit.textContent = 'Create Bot';
  }
}

function showAddBotError(msg) {
  const errorEl = document.getElementById('addbot-error-banner');
  if (errorEl) {
    errorEl.textContent = msg;
    errorEl.style.display = 'block';
  }
}

function clearAddBotError() {
  const errorEl = document.getElementById('addbot-error-banner');
  if (errorEl) {
    errorEl.textContent = '';
    errorEl.style.display = 'none';
  }
}

// Global expose
window.openAddBotModal = openAddBotModal;
window.closeAddBotModal = closeAddBotModal;
window.onAccountSelectionChanged = onAccountSelectionChanged;
window.onTargetJobChanged = onTargetJobChanged;
window.applyJobArchetypePreset = applyJobArchetypePreset;
window.updateStartingStat = updateStartingStat;
window.onStartingStatInput = onStartingStatInput;
window.switchAddBotTab = switchAddBotTab;
window.updateAddBotStatStep = updateAddBotStatStep;
window.moveAddBotStatStep = moveAddBotStatStep;
window.deleteAddBotStatStep = deleteAddBotStatStep;
window.addNewAddBotStatStep = addNewAddBotStatStep;
window.updateAddBotSkillStep = updateAddBotSkillStep;
window.moveAddBotSkillStep = moveAddBotSkillStep;
window.deleteAddBotSkillStep = deleteAddBotSkillStep;
window.addNewAddBotSkillStep = addNewAddBotSkillStep;
window.submitAddBot = submitAddBot;
