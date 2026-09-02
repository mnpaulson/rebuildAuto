// ==============================================================================
// RAGNAROK REBUILD - BOT CONFIGURATION FORM & SCHEMA ENGINE
// ==============================================================================

let currentConfigData = {};
let currentProfileTarget = '';
let activeConfigCategory = 'combat';
let currentConfigEditorMode = 'form'; // 'form' | 'json'
let itemRulesSearchFilter = '';
let itemRulesActionFilter = 'all'; // 'all' | 'Sell' | 'Store' | 'Keep'

// Initialize the editor with a profile's configuration
function initConfigEditor(profileName, config) {
  currentProfileTarget = profileName;
  currentConfigData = config || {};
  activeConfigCategory = 'combat';
  currentConfigEditorMode = 'form';
  itemRulesSearchFilter = '';
  itemRulesActionFilter = 'all';

  document.getElementById('config-profile-target').value = profileName;
  document.getElementById('config-modal-title').textContent = `Configuration - ${profileName}`;

  // Sync raw JSON textarea
  const rawArea = document.getElementById('config-json-editor');
  if (rawArea) {
    rawArea.value = JSON.stringify(currentConfigData, null, 2);
  }

  // Clear search input
  const searchInput = document.getElementById('config-search-input');
  if (searchInput) searchInput.value = '';

  // Render Tabs & Active Category
  renderConfigCategoryTabs();
  renderActiveCategoryForm();
  updateEditorModeDisplay();
}

// Render Sidebar Navigation Tabs
function renderConfigCategoryTabs() {
  const tabsContainer = document.getElementById('config-category-tabs');
  if (!tabsContainer) return;

  tabsContainer.innerHTML = CONFIG_CATEGORIES.map(cat => `
    <button type="button" 
            class="config-tab-btn ${activeConfigCategory === cat.id ? 'active' : ''}" 
            onclick="switchConfigCategory('${cat.id}')">
      <span>${cat.label}</span>
    </button>
  `).join('');
}

// Switch Active Category Tab
function switchConfigCategory(categoryId) {
  activeConfigCategory = categoryId;
  renderConfigCategoryTabs();
  renderActiveCategoryForm();
}

// Toggle between Visual Form View and Raw JSON View
function toggleConfigEditorMode(targetMode) {
  if (targetMode === currentConfigEditorMode) return;

  if (targetMode === 'json') {
    // Sync current form state to JSON textarea
    const rawArea = document.getElementById('config-json-editor');
    if (rawArea) {
      rawArea.value = JSON.stringify(currentConfigData, null, 2);
    }
  } else if (targetMode === 'form') {
    // Parse JSON textarea back to form state
    const rawArea = document.getElementById('config-json-editor');
    if (rawArea) {
      try {
        currentConfigData = JSON.parse(rawArea.value);
      } catch (err) {
        alert('Cannot switch to Visual Form: JSON has syntax errors.\n' + err.message);
        return;
      }
    }
    renderActiveCategoryForm();
  }

  currentConfigEditorMode = targetMode;
  updateEditorModeDisplay();
}

function updateEditorModeDisplay() {
  const formViewport = document.getElementById('config-form-viewport');
  const jsonViewport = document.getElementById('config-json-viewport');
  const btnForm = document.getElementById('btn-mode-form');
  const btnJson = document.getElementById('btn-mode-json');

  if (currentConfigEditorMode === 'form') {
    if (formViewport) formViewport.style.display = 'flex';
    if (jsonViewport) jsonViewport.style.display = 'none';
    if (btnForm) btnForm.classList.add('active');
    if (btnJson) btnJson.classList.remove('active');
  } else {
    if (formViewport) formViewport.style.display = 'none';
    if (jsonViewport) jsonViewport.style.display = 'flex';
    if (btnForm) btnForm.classList.remove('active');
    if (btnJson) btnJson.classList.add('active');
  }
}

// Render Settings for Active Category
function renderActiveCategoryForm() {
  const contentArea = document.getElementById('config-settings-container');
  if (!contentArea) return;

  const searchQuery = (document.getElementById('config-search-input')?.value || '').trim().toLowerCase();

  let fieldsToRender = CONFIG_SCHEMA;
  if (!searchQuery) {
    fieldsToRender = CONFIG_SCHEMA.filter(f => f.category === activeConfigCategory);
  } else {
    // If searching, search across all categories!
    fieldsToRender = CONFIG_SCHEMA.filter(f => 
      f.label.toLowerCase().includes(searchQuery) ||
      (f.description && f.description.toLowerCase().includes(searchQuery)) ||
      f.id.toLowerCase().includes(searchQuery)
    );
  }

  const categoryMeta = CONFIG_CATEGORIES.find(c => c.id === activeConfigCategory);

  let html = '';
  if (!searchQuery && categoryMeta) {
    html += `
      <div class="category-header">
        <h3>${categoryMeta.label}</h3>
        <p>${categoryMeta.description}</p>
      </div>
    `;
  } else if (searchQuery) {
    html += `
      <div class="category-header">
        <h3>Search Results (${fieldsToRender.length})</h3>
        <p>Showing settings matching "${searchQuery}" across all categories</p>
      </div>
    `;
  }

  if (fieldsToRender.length === 0) {
    html += `<div class="empty-settings-msg">No configuration options match your search.</div>`;
    contentArea.innerHTML = html;
    return;
  }

  html += `<div class="config-fields-list">`;
  fieldsToRender.forEach(field => {
    html += renderWidgetForField(field);
  });
  html += `</div>`;

  contentArea.innerHTML = html;

  // Initialize interactive widgets that need event listeners (e.g. tag lists)
  fieldsToRender.forEach(field => {
    if (field.type === 'tag-list') {
      initTagListWidget(field.id);
    }
  });
}

// Filter settings on search input
function onConfigSearchChanged() {
  renderActiveCategoryForm();
}

// --------------------------------------------------------------------------
// Widget Generators
// --------------------------------------------------------------------------

function renderWidgetForField(field) {
  const currentVal = currentConfigData[field.id] !== undefined ? currentConfigData[field.id] : field.default;

  switch (field.type) {
    case 'boolean':
      return `
        <div class="config-field-row boolean-row" id="field-${field.id}">
          <div class="field-info">
            <label class="field-title">${field.label}</label>
            <span class="field-desc">${field.description}</span>
          </div>
          <label class="tremor-toggle">
            <input type="checkbox" 
                   id="input-${field.id}" 
                   ${currentVal ? 'checked' : ''} 
                   onchange="onConfigFieldChanged('${field.id}', this.checked)">
            <span class="toggle-slider"></span>
          </label>
        </div>
      `;

    case 'percent':
    case 'number':
      const min = field.min ?? 0;
      const max = field.max ?? 100;
      const step = field.step ?? 1;
      const unit = field.unit ?? '';
      const numVal = Number(currentVal) || 0;

      return `
        <div class="config-field-row slider-row" id="field-${field.id}">
          <div class="field-info">
            <label class="field-title">${field.label}</label>
            <span class="field-desc">${field.description}</span>
          </div>
          <div class="slider-control-group">
            <input type="range" 
                   class="tremor-range" 
                   id="range-${field.id}" 
                   min="${min}" max="${max}" step="${step}" 
                   value="${numVal}" 
                   oninput="onSliderSync('${field.id}', this.value, '${unit}')">
            <div class="slider-value-box">
              <input type="number" 
                     class="slider-num-input" 
                     id="num-${field.id}" 
                     min="${min}" max="${max}" step="${step}" 
                     value="${numVal}" 
                     onchange="onNumberInputSync('${field.id}', this.value)">
              <span class="slider-unit">${unit}</span>
            </div>
          </div>
        </div>
      `;

    case 'select':
      const options = field.options || [];
      return `
        <div class="config-field-row select-row" id="field-${field.id}">
          <div class="field-info">
            <label class="field-title">${field.label}</label>
            <span class="field-desc">${field.description}</span>
          </div>
          <select class="tremor-select" 
                  id="input-${field.id}" 
                  onchange="onConfigFieldChanged('${field.id}', this.value)">
            ${options.map(opt => `
              <option value="${opt}" ${String(currentVal) === String(opt) ? 'selected' : ''}>${opt.replace(/_/g, ' ')}</option>
            `).join('')}
          </select>
        </div>
      `;

    case 'string':
      return `
        <div class="config-field-row text-row" id="field-${field.id}">
          <div class="field-info">
            <label class="field-title">${field.label}</label>
            <span class="field-desc">${field.description}</span>
          </div>
          <input type="text" 
                 class="tremor-input" 
                 id="input-${field.id}" 
                 value="${currentVal || ''}" 
                 onchange="onConfigFieldChanged('${field.id}', this.value)">
        </div>
      `;

    case 'tag-list':
      const tags = Array.isArray(currentVal) ? currentVal : [];
      return `
        <div class="config-field-group tag-list-group" id="field-${field.id}">
          <div class="field-info">
            <label class="field-title">${field.label}</label>
            <span class="field-desc">${field.description}</span>
          </div>
          <div class="tag-list-container">
            <div class="tag-chips-wrapper" id="chips-${field.id}">
              ${tags.map(tag => `
                <span class="tag-chip">
                  <span>${tag}</span>
                  <button type="button" class="tag-remove-btn" onclick="removeTagItem('${field.id}', '${tag}')">&times;</button>
                </span>
              `).join('')}
            </div>
            <div class="tag-add-wrapper">
              <input type="text" 
                     class="tag-add-input" 
                     id="tag-input-${field.id}" 
                     placeholder="${field.placeholder || 'Type item and press Enter...'}" 
                     onkeydown="onTagInputKeydown(event, '${field.id}')">
              <button type="button" class="btn btn-secondary btn-sm" onclick="addTagFromInput('${field.id}')">Add</button>
            </div>
          </div>
        </div>
      `;

    case 'stepper-table':
      return renderRestockTableWidget(field.id, currentVal);

    case 'item-rules-table':
      return renderItemRulesTableWidget(field.id, currentVal);

    case 'stat-plan-builder':
      return renderStatPlanBuilderWidget(field.id, currentVal);

    case 'skill-plan-builder':
      return renderSkillPlanBuilderWidget(field.id, currentVal);

    case 'skill-rules-builder':
      return renderSkillRulesBuilderWidget(field.id, currentVal);

    default:
      return '';
  }
}

// --------------------------------------------------------------------------
// Value Synchronizers
// --------------------------------------------------------------------------

function onConfigFieldChanged(fieldId, value) {
  currentConfigData[fieldId] = value;
}

function onSliderSync(fieldId, val, unit) {
  const numInput = document.getElementById(`num-${fieldId}`);
  if (numInput) numInput.value = val;
  currentConfigData[fieldId] = Number(val);
}

function onNumberInputSync(fieldId, val) {
  const rangeInput = document.getElementById(`range-${fieldId}`);
  if (rangeInput) rangeInput.value = val;
  currentConfigData[fieldId] = Number(val);
}

// --------------------------------------------------------------------------
// Tag List Widget Logic
// --------------------------------------------------------------------------

function initTagListWidget(fieldId) {
  // Setup if required
}

function onTagInputKeydown(e, fieldId) {
  if (e.key === 'Enter') {
    e.preventDefault();
    addTagFromInput(fieldId);
  }
}

function addTagFromInput(fieldId) {
  const input = document.getElementById(`tag-input-${fieldId}`);
  if (!input) return;

  const val = input.value.trim();
  if (!val) return;

  if (!Array.isArray(currentConfigData[fieldId])) {
    currentConfigData[fieldId] = [];
  }

  if (!currentConfigData[fieldId].includes(val)) {
    currentConfigData[fieldId].push(val);
    renderActiveCategoryForm();
  }

  input.value = '';
}

function removeTagItem(fieldId, tagValue) {
  if (!Array.isArray(currentConfigData[fieldId])) return;

  currentConfigData[fieldId] = currentConfigData[fieldId].filter(t => t !== tagValue);
  renderActiveCategoryForm();
}

// --------------------------------------------------------------------------
// Restock Targets Table Widget
// --------------------------------------------------------------------------

function renderRestockTableWidget(fieldId, restockMap) {
  const dict = restockMap || {};
  const entries = Object.entries(dict);

  return `
    <div class="config-field-group restock-group" id="field-${fieldId}">
      <div class="field-info">
        <label class="field-title">Supply Restock Targets</label>
        <span class="field-desc">Configures inventory target quotas maintained during town restock routine.</span>
      </div>

      <div class="restock-table-wrapper">
        <table class="config-table">
          <thead>
            <tr>
              <th>Supply Item Name</th>
              <th style="width: 140px;">Target Quantity</th>
              <th style="width: 60px;">Action</th>
            </tr>
          </thead>
          <tbody>
            ${entries.map(([name, count]) => `
              <tr>
                <td style="font-weight: 600; color: #f8fafc;">${name.replace(/_/g, ' ')}</td>
                <td>
                  <input type="number" 
                         class="config-table-input" 
                         value="${count}" 
                         min="0" max="1000" 
                         onchange="updateRestockQuantity('${name}', this.value)">
                </td>
                <td style="text-align: center;">
                  <button type="button" class="btn-delete-row" onclick="removeRestockItem('${name}')">&times;</button>
                </td>
              </tr>
            `).join('')}
            <tr class="add-row">
              <td>
                <input type="text" class="config-table-input" id="new-restock-name" placeholder="Item name (e.g. Red_Potion)...">
              </td>
              <td>
                <input type="number" class="config-table-input" id="new-restock-qty" value="50" min="1" max="1000">
              </td>
              <td style="text-align: center;">
                <button type="button" class="btn btn-secondary btn-sm" onclick="addNewRestockTarget()">Add</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  `;
}

function updateRestockQuantity(name, count) {
  if (!currentConfigData.RestockTargets) currentConfigData.RestockTargets = {};
  currentConfigData.RestockTargets[name] = parseInt(count, 10) || 0;
}

function removeRestockItem(name) {
  if (currentConfigData.RestockTargets && currentConfigData.RestockTargets[name] !== undefined) {
    delete currentConfigData.RestockTargets[name];
    renderActiveCategoryForm();
  }
}

function addNewRestockTarget() {
  const nameInput = document.getElementById('new-restock-name');
  const qtyInput = document.getElementById('new-restock-qty');
  if (!nameInput || !qtyInput) return;

  const name = nameInput.value.trim().replace(/\s+/g, '_');
  const qty = parseInt(qtyInput.value, 10) || 1;

  if (name) {
    if (!currentConfigData.RestockTargets) currentConfigData.RestockTargets = {};
    currentConfigData.RestockTargets[name] = qty;
    renderActiveCategoryForm();
  }
}

// --------------------------------------------------------------------------
// Item Rules Matrix Table Widget (Sell / Store / Keep)
// --------------------------------------------------------------------------

function renderItemRulesTableWidget(fieldId, rulesMap) {
  const rules = rulesMap || {};
  let entries = Object.entries(rules);

  // Apply search filter
  if (itemRulesSearchFilter) {
    const q = itemRulesSearchFilter.toLowerCase();
    entries = entries.filter(([name]) => name.toLowerCase().includes(q));
  }

  // Apply action filter
  if (itemRulesActionFilter !== 'all') {
    entries = entries.filter(([, action]) => action.toLowerCase() === itemRulesActionFilter.toLowerCase());
  }

  return `
    <div class="config-field-group item-rules-group" id="field-${fieldId}">
      <div class="field-info">
        <label class="field-title">Item Rules Management (${Object.keys(rules).length} items configured)</label>
        <span class="field-desc">Choose whether dropped or acquired items are sold to NPC vendors, deposited into Kafra storage, or kept in inventory.</span>
      </div>

      <div class="item-rules-toolbar">
        <div class="item-rules-search">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/></svg>
          <input type="text" 
                 placeholder="Filter items (e.g. Card, Herb, Potion)..." 
                 value="${itemRulesSearchFilter}" 
                 oninput="onItemRulesSearch(this.value)">
        </div>
        <div class="item-rules-filter-pills">
          <button type="button" class="filter-pill ${itemRulesActionFilter === 'all' ? 'active' : ''}" onclick="setItemRulesActionFilter('all')">All</button>
          <button type="button" class="filter-pill ${itemRulesActionFilter === 'Sell' ? 'active' : ''}" onclick="setItemRulesActionFilter('Sell')">Sell</button>
          <button type="button" class="filter-pill ${itemRulesActionFilter === 'Store' ? 'active' : ''}" onclick="setItemRulesActionFilter('Store')">Store</button>
          <button type="button" class="filter-pill ${itemRulesActionFilter === 'Keep' ? 'active' : ''}" onclick="setItemRulesActionFilter('Keep')">Keep</button>
        </div>
      </div>

      <div class="item-rules-table-wrapper">
        <table class="config-table">
          <thead>
            <tr>
              <th>Item Name</th>
              <th style="width: 220px; text-align: center;">Action Decision</th>
              <th style="width: 50px;">Delete</th>
            </tr>
          </thead>
          <tbody>
            ${entries.length > 0 ? entries.map(([itemName, action]) => `
              <tr>
                <td style="font-weight: 600; color: #f1f5f9;">${itemName}</td>
                <td style="text-align: center;">
                  <div class="action-toggle-group">
                    <button type="button" 
                            class="action-btn sell ${action === 'Sell' ? 'active' : ''}" 
                            onclick="setItemRuleAction('${itemName}', 'Sell')">Sell</button>
                    <button type="button" 
                            class="action-btn store ${action === 'Store' ? 'active' : ''}" 
                            onclick="setItemRuleAction('${itemName}', 'Store')">Store</button>
                    <button type="button" 
                            class="action-btn keep ${action === 'Keep' ? 'active' : ''}" 
                            onclick="setItemRuleAction('${itemName}', 'Keep')">Keep</button>
                  </div>
                </td>
                <td style="text-align: center;">
                  <button type="button" class="btn-delete-row" onclick="deleteItemRule('${itemName}')">&times;</button>
                </td>
              </tr>
            `).join('') : `
              <tr>
                <td colspan="3" style="text-align: center; color: var(--text-muted); padding: 24px;">No items match the current filter.</td>
              </tr>
            `}
            <tr class="add-row">
              <td>
                <input type="text" class="config-table-input" id="new-item-rule-name" placeholder="Item name (e.g. Iron Ore, Blue Herb)...">
              </td>
              <td style="text-align: center;">
                <select class="tremor-select" id="new-item-rule-action" style="padding: 4px 8px; font-size: 0.775rem;">
                  <option value="Sell">Sell to Vendor</option>
                  <option value="Store">Deposit to Storage</option>
                  <option value="Keep">Keep in Inventory</option>
                </select>
              </td>
              <td style="text-align: center;">
                <button type="button" class="btn btn-secondary btn-sm" onclick="addNewItemRule()">Add</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  `;
}

function onItemRulesSearch(query) {
  itemRulesSearchFilter = query;
  renderActiveCategoryForm();
}

function setItemRulesActionFilter(filter) {
  itemRulesActionFilter = filter;
  renderActiveCategoryForm();
}

function setItemRuleAction(itemName, action) {
  if (!currentConfigData.ItemRules) currentConfigData.ItemRules = {};
  currentConfigData.ItemRules[itemName] = action;
  renderActiveCategoryForm();
}

function deleteItemRule(itemName) {
  if (currentConfigData.ItemRules && currentConfigData.ItemRules[itemName] !== undefined) {
    delete currentConfigData.ItemRules[itemName];
    renderActiveCategoryForm();
  }
}

function addNewItemRule() {
  const nameInput = document.getElementById('new-item-rule-name');
  const actionSelect = document.getElementById('new-item-rule-action');
  if (!nameInput || !actionSelect) return;

  const name = nameInput.value.trim();
  const action = actionSelect.value;

  if (name) {
    if (!currentConfigData.ItemRules) currentConfigData.ItemRules = {};
    currentConfigData.ItemRules[name] = action;
    nameInput.value = '';
    renderActiveCategoryForm();
  }
}

// --------------------------------------------------------------------------
// Stat Build Plan Widget (Sequential Milestone Allocation)
// --------------------------------------------------------------------------

function renderStatPlanBuilderWidget(fieldId, planArray) {
  const plan = Array.isArray(planArray) ? planArray : [];
  const statsList = ['Str', 'Agi', 'Vit', 'Int', 'Dex', 'Luk'];

  return `
    <div class="config-field-group stat-plan-group" id="field-${fieldId}">
      <div class="field-info">
        <label class="field-title">Sequential Stat Point Allocation Plan (${plan.length} steps)</label>
        <span class="field-desc">The bot allocates available stat points to achieve each milestone in sequential order (#1, then #2, etc.).</span>
      </div>

      <div class="plan-table-wrapper">
        <table class="config-table plan-table">
          <thead>
            <tr>
              <th style="width: 50px;">Step</th>
              <th style="width: 140px;">Stat</th>
              <th>Target Value</th>
              <th style="width: 100px; text-align: center;">Order</th>
              <th style="width: 50px; text-align: center;">Delete</th>
            </tr>
          </thead>
          <tbody>
            ${plan.length > 0 ? plan.map((step, idx) => `
              <tr>
                <td><span class="step-badge">#${idx + 1}</span></td>
                <td>
                  <select class="tremor-select" style="padding: 4px 8px; font-weight: 700;" onchange="updateStatPlanStep(${idx}, 'Stat', this.value)">
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
                           style="width: 90px;" 
                           value="${step.Target || 1}" 
                           min="1" max="99" 
                           onchange="updateStatPlanStep(${idx}, 'Target', parseInt(this.value, 10) || 1)">
                    <span style="color: var(--text-muted); font-size: 0.75rem;">points</span>
                  </div>
                </td>
                <td style="text-align: center;">
                  <button type="button" class="btn-reorder" onclick="moveStatPlanStep(${idx}, -1)" ${idx === 0 ? 'disabled style="opacity:0.3; cursor:default;"' : ''}>▲</button>
                  <button type="button" class="btn-reorder" onclick="moveStatPlanStep(${idx}, 1)" ${idx === plan.length - 1 ? 'disabled style="opacity:0.3; cursor:default;"' : ''}>▼</button>
                </td>
                <td style="text-align: center;">
                  <button type="button" class="btn-delete-row" onclick="deleteStatPlanStep(${idx})">&times;</button>
                </td>
              </tr>
            `).join('') : `
              <tr>
                <td colspan="5" style="text-align: center; color: var(--text-muted); padding: 20px;">No stat milestones configured. Points will not be spent automatically.</td>
              </tr>
            `}
            <tr class="add-row">
              <td colspan="2">
                <select class="tremor-select" id="new-stat-picker" style="padding: 5px 10px; width: 100%;">
                  <option value="Dex">DEX (Dexterity)</option>
                  <option value="Str">STR (Strength)</option>
                  <option value="Agi">AGI (Agility)</option>
                  <option value="Vit">VIT (Vitality)</option>
                  <option value="Int">INT (Intelligence)</option>
                  <option value="Luk">LUK (Luck)</option>
                </select>
              </td>
              <td colspan="2">
                <input type="number" class="config-table-input" id="new-stat-target" value="20" min="1" max="99" placeholder="Target value...">
              </td>
              <td style="text-align: center;">
                <button type="button" class="btn btn-secondary btn-sm" onclick="addNewStatPlanStep()">+ Add</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  `;
}

function updateStatPlanStep(index, prop, value) {
  if (!Array.isArray(currentConfigData.StatBuildPlan)) currentConfigData.StatBuildPlan = [];
  if (currentConfigData.StatBuildPlan[index]) {
    currentConfigData.StatBuildPlan[index][prop] = value;
  }
}

function moveStatPlanStep(index, direction) {
  if (!Array.isArray(currentConfigData.StatBuildPlan)) return;
  const newIndex = index + direction;
  if (newIndex < 0 || newIndex >= currentConfigData.StatBuildPlan.length) return;

  const item = currentConfigData.StatBuildPlan.splice(index, 1)[0];
  currentConfigData.StatBuildPlan.splice(newIndex, 0, item);
  renderActiveCategoryForm();
}

function deleteStatPlanStep(index) {
  if (!Array.isArray(currentConfigData.StatBuildPlan)) return;
  currentConfigData.StatBuildPlan.splice(index, 1);
  renderActiveCategoryForm();
}

function addNewStatPlanStep() {
  const statSelect = document.getElementById('new-stat-picker');
  const targetInput = document.getElementById('new-stat-target');
  if (!statSelect || !targetInput) return;

  const stat = statSelect.value;
  const target = parseInt(targetInput.value, 10) || 1;

  if (!Array.isArray(currentConfigData.StatBuildPlan)) currentConfigData.StatBuildPlan = [];
  currentConfigData.StatBuildPlan.push({ Stat: stat, Target: target });
  renderActiveCategoryForm();
}

// --------------------------------------------------------------------------
// Skill Build Plan Widget (Sequential Skill Leveling)
// --------------------------------------------------------------------------

function renderSkillPlanBuilderWidget(fieldId, planArray) {
  const plan = Array.isArray(planArray) ? planArray : [];

  return `
    <div class="config-field-group skill-plan-group" id="field-${fieldId}">
      <div class="field-info">
        <label class="field-title">Sequential Skill Leveling Plan (${plan.length} steps)</label>
        <span class="field-desc">The bot levels up skills in this exact sequence as skill points are earned.</span>
      </div>

      <div class="plan-table-wrapper">
        <table class="config-table plan-table">
          <thead>
            <tr>
              <th style="width: 50px;">Step</th>
              <th>Skill Name</th>
              <th style="width: 140px;">Target Level</th>
              <th style="width: 100px; text-align: center;">Order</th>
              <th style="width: 50px; text-align: center;">Delete</th>
            </tr>
          </thead>
          <tbody>
            ${plan.length > 0 ? plan.map((step, idx) => `
              <tr>
                <td><span class="step-badge">#${idx + 1}</span></td>
                <td>
                  <input type="text" 
                         class="config-table-input" 
                         value="${step.Skill || ''}" 
                         placeholder="Skill name (e.g. BasicSkill, Bash)..." 
                         onchange="updateSkillPlanStep(${idx}, 'Skill', this.value)">
                </td>
                <td>
                  <div style="display: flex; align-items: center; gap: 6px;">
                    <span style="color: var(--text-muted); font-size: 0.75rem;">Level</span>
                    <input type="number" 
                           class="config-table-input" 
                           style="width: 60px;" 
                           value="${step.Target || 1}" 
                           min="1" max="10" 
                           onchange="updateSkillPlanStep(${idx}, 'Target', parseInt(this.value, 10) || 1)">
                  </div>
                </td>
                <td style="text-align: center;">
                  <button type="button" class="btn-reorder" onclick="moveSkillPlanStep(${idx}, -1)" ${idx === 0 ? 'disabled style="opacity:0.3; cursor:default;"' : ''}>▲</button>
                  <button type="button" class="btn-reorder" onclick="moveSkillPlanStep(${idx}, 1)" ${idx === plan.length - 1 ? 'disabled style="opacity:0.3; cursor:default;"' : ''}>▼</button>
                </td>
                <td style="text-align: center;">
                  <button type="button" class="btn-delete-row" onclick="deleteSkillPlanStep(${idx})">&times;</button>
                </td>
              </tr>
            `).join('') : `
              <tr>
                <td colspan="5" style="text-align: center; color: var(--text-muted); padding: 20px;">No skill leveling plan configured.</td>
              </tr>
            `}
            <tr class="add-row">
              <td colspan="2">
                <input type="text" class="config-table-input" id="new-skill-name" placeholder="Skill name (e.g. BasicSkill, DoubleAttack, Bash)...">
              </td>
              <td colspan="2">
                <input type="number" class="config-table-input" id="new-skill-target" value="10" min="1" max="10" placeholder="Target Lv (1-10)...">
              </td>
              <td style="text-align: center;">
                <button type="button" class="btn btn-secondary btn-sm" onclick="addNewSkillPlanStep()">+ Add</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  `;
}

function updateSkillPlanStep(index, prop, value) {
  if (!Array.isArray(currentConfigData.SkillBuildPlan)) currentConfigData.SkillBuildPlan = [];
  if (currentConfigData.SkillBuildPlan[index]) {
    currentConfigData.SkillBuildPlan[index][prop] = value;
  }
}

function moveSkillPlanStep(index, direction) {
  if (!Array.isArray(currentConfigData.SkillBuildPlan)) return;
  const newIndex = index + direction;
  if (newIndex < 0 || newIndex >= currentConfigData.SkillBuildPlan.length) return;

  const item = currentConfigData.SkillBuildPlan.splice(index, 1)[0];
  currentConfigData.SkillBuildPlan.splice(newIndex, 0, item);
  renderActiveCategoryForm();
}

function deleteSkillPlanStep(index) {
  if (!Array.isArray(currentConfigData.SkillBuildPlan)) return;
  currentConfigData.SkillBuildPlan.splice(index, 1);
  renderActiveCategoryForm();
}

function addNewSkillPlanStep() {
  const nameInput = document.getElementById('new-skill-name');
  const targetInput = document.getElementById('new-skill-target');
  if (!nameInput || !targetInput) return;

  const skill = nameInput.value.trim();
  const target = parseInt(targetInput.value, 10) || 1;

  if (skill) {
    if (!Array.isArray(currentConfigData.SkillBuildPlan)) currentConfigData.SkillBuildPlan = [];
    currentConfigData.SkillBuildPlan.push({ Skill: skill, Target: target });
    renderActiveCategoryForm();
  }
}

// --------------------------------------------------------------------------
// Combat Skill Rules & Rotation Widget
// --------------------------------------------------------------------------

function renderSkillRulesBuilderWidget(fieldId, rulesArray) {
  const rules = Array.isArray(rulesArray) ? rulesArray : [];

  return `
    <div class="config-field-group skill-rules-group" id="field-${fieldId}">
      <div class="field-info">
        <label class="field-title">Combat Skill Rules & Rotations (${rules.length} configured)</label>
        <span class="field-desc">Configure active combat skills, buffs, openers, and recovery thresholds used during battle.</span>
      </div>

      <div class="skill-rules-list">
        ${rules.length > 0 ? rules.map((rule, idx) => `
          <div class="skill-rule-card ${rule.Enabled !== false ? 'active' : 'disabled'}">
            <div class="rule-card-header">
              <div class="rule-card-title-group">
                <label class="tremor-toggle">
                  <input type="checkbox" ${rule.Enabled !== false ? 'checked' : ''} onchange="updateSkillRule(${idx}, 'Enabled', this.checked)">
                  <span class="toggle-slider"></span>
                </label>
                <input type="text" 
                       class="rule-skill-name-input" 
                       value="${rule.Skill || ''}" 
                       placeholder="Skill Name (e.g. Bash, SonicBlow)..." 
                       onchange="updateSkillRule(${idx}, 'Skill', this.value)">
                <span class="badge-pill ${rule.Enabled !== false ? 'badge-emerald' : 'badge-slate'}" style="font-size: 0.7rem;">
                  ${rule.Enabled !== false ? 'Active' : 'Disabled'}
                </span>
              </div>
              <button type="button" class="btn-delete-row" onclick="deleteSkillRule(${idx})">&times;</button>
            </div>

            <div class="rule-card-grid">
              <div class="rule-param">
                <label>Trigger Condition</label>
                <select class="tremor-select" onchange="updateSkillRule(${idx}, 'Trigger', this.value)">
                  <option value="Combat" ${rule.Trigger === 'Combat' ? 'selected' : ''}>Combat (Spammed in battle)</option>
                  <option value="Opener" ${rule.Trigger === 'Opener' ? 'selected' : ''}>Opener (Cast once on engage)</option>
                  <option value="BuffMaintenance" ${rule.Trigger === 'BuffMaintenance' ? 'selected' : ''}>Buff Maintenance (Maintain active)</option>
                  <option value="HpBelowPercent" ${rule.Trigger === 'HpBelowPercent' ? 'selected' : ''}>Emergency Heal (HP Below %)</option>
                  <option value="MobCluster" ${rule.Trigger === 'MobCluster' ? 'selected' : ''}>Mob Cluster (AOE when surrounded)</option>
                </select>
              </div>

              <div class="rule-param">
                <label>Target</label>
                <select class="tremor-select" onchange="updateSkillRule(${idx}, 'Target', this.value)">
                  <option value="Enemy" ${rule.Target === 'Enemy' ? 'selected' : ''}>Target Enemy</option>
                  <option value="Self" ${rule.Target === 'Self' ? 'selected' : ''}>Self Cast</option>
                  <option value="Ground" ${rule.Target === 'Ground' ? 'selected' : ''}>Ground Target</option>
                </select>
              </div>

              <div class="rule-param">
                <label>Min SP Reserve %</label>
                <input type="number" class="config-table-input" value="${rule.MinSpPercent ?? 20}" min="0" max="100" onchange="updateSkillRule(${idx}, 'MinSpPercent', parseInt(this.value, 10) || 0)">
              </div>

              <div class="rule-param">
                <label>Cooldown (s)</label>
                <input type="number" class="config-table-input" value="${rule.CooldownSeconds ?? 1.0}" min="0.1" max="60" step="0.1" onchange="updateSkillRule(${idx}, 'CooldownSeconds', parseFloat(this.value) || 1.0)">
              </div>

              ${rule.Trigger === 'HpBelowPercent' ? `
                <div class="rule-param">
                  <label>Trigger HP Below %</label>
                  <input type="number" class="config-table-input" value="${rule.HpBelowPercent ?? 60}" min="1" max="99" onchange="updateSkillRule(${idx}, 'HpBelowPercent', parseInt(this.value, 10) || 60)">
                </div>
              ` : ''}

              ${rule.Trigger === 'MobCluster' ? `
                <div class="rule-param">
                  <label>Min Enemies Around</label>
                  <input type="number" class="config-table-input" value="${rule.MinEnemiesInRange ?? 3}" min="1" max="20" onchange="updateSkillRule(${idx}, 'MinEnemiesInRange', parseInt(this.value, 10) || 3)">
                </div>
              ` : ''}
            </div>
          </div>
        `).join('') : `
          <div style="text-align: center; color: var(--text-muted); padding: 24px; border: 1px dashed var(--border-card); border-radius: var(--radius-md);">
            No combat skills configured. Bot will use standard normal auto-attacks.
          </div>
        `}

        <div style="display: flex; justify-content: flex-end; margin-top: 10px;">
          <button type="button" class="btn btn-secondary btn-sm" onclick="addNewSkillRule()">+ Add Combat Skill Rule</button>
        </div>
      </div>
    </div>
  `;
}

function updateSkillRule(index, prop, value) {
  if (!Array.isArray(currentConfigData.SkillRules)) currentConfigData.SkillRules = [];
  if (currentConfigData.SkillRules[index]) {
    currentConfigData.SkillRules[index][prop] = value;
    if (prop === 'Trigger') {
      renderActiveCategoryForm();
    }
  }
}

function deleteSkillRule(index) {
  if (!Array.isArray(currentConfigData.SkillRules)) return;
  currentConfigData.SkillRules.splice(index, 1);
  renderActiveCategoryForm();
}

function addNewSkillRule() {
  if (!Array.isArray(currentConfigData.SkillRules)) currentConfigData.SkillRules = [];
  currentConfigData.SkillRules.push({
    Skill: 'Bash',
    Level: 0,
    Target: 'Enemy',
    Placement: 'DirectOnEnemy',
    Trigger: 'Combat',
    HpBelowPercent: 0,
    MinSpPercent: 20,
    MinTargetHp: 100,
    MinEnemiesInRange: 1,
    CooldownSeconds: 1.2,
    TargetMonsters: [],
    Enabled: true
  });
  renderActiveCategoryForm();
}

// --------------------------------------------------------------------------
// Save Configuration Handler
// --------------------------------------------------------------------------

function saveBotConfiguration() {
  const profile = document.getElementById('config-profile-target').value;
  if (!profile) return;

  let payload = '';

  if (currentConfigEditorMode === 'json') {
    const raw = document.getElementById('config-json-editor').value;
    try {
      JSON.parse(raw); // validate
      payload = raw;
    } catch (err) {
      alert('Cannot save: Invalid JSON syntax.\n' + err.message);
      return;
    }
  } else {
    payload = JSON.stringify(currentConfigData, null, 2);
  }

  fetch(`/api/bot/${encodeURIComponent(profile)}/config`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: payload
  })
    .then(r => r.json())
    .then(data => {
      if (data.success) {
        closeConfigModal();
      } else {
        alert(data.error || 'Failed to save configuration');
      }
    })
    .catch(err => alert('Save failed: ' + err.message));
}
