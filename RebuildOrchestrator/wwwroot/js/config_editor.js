// ==============================================================================
// RAGNAROK REBUILD - BOT CONFIGURATION FORM & SCHEMA ENGINE
// ==============================================================================

let currentConfigData = {};
let currentProfileTarget = '';
let currentBotInfo = null;
let activeConfigCategory = 'combat';
let currentConfigEditorMode = 'form'; // 'form' | 'json'
let itemRulesSearchFilter = '';
let itemRulesActionFilter = 'all'; // 'all' | 'Sell' | 'Store' | 'Keep'

// Initialize the editor with a profile's configuration
function initConfigEditor(profileName, config, botInfo) {
  currentProfileTarget = profileName;
  currentConfigData = config || {};
  currentBotInfo = botInfo || null;
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
      let isFieldDisabled = false;
      let fieldTooltip = '';
      if (field.id === 'IsDistributor') {
        const job = currentBotInfo?.status?.jobName || currentBotInfo?.Status?.JobName;
        if (job && job !== 'Merchant' && job !== 'Blacksmith') {
          isFieldDisabled = true;
          fieldTooltip = 'Only available for Merchant bots.';
        }
      }
      return `
        <div class="config-field-row boolean-row" id="field-${field.id}" ${isFieldDisabled ? `title="${fieldTooltip}" style="opacity: 0.6;"` : ''}>
          <div class="field-info">
            <label class="field-title">${field.label}${isFieldDisabled ? ' <span style="font-size: 0.75rem; color: var(--accent-amber);">(Merchant only)</span>' : ''}</label>
            <span class="field-desc">${field.description}</span>
          </div>
          <label class="tremor-toggle" ${isFieldDisabled ? `title="${fieldTooltip}" style="cursor: not-allowed;"` : ''}>
            <input type="checkbox" 
                   id="input-${field.id}" 
                   ${currentVal ? 'checked' : ''} 
                   ${isFieldDisabled ? 'disabled' : ''} 
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

    case 'vend-consumables-table':
      return renderVendConsumablesTableWidget(field.id, currentVal);

    case 'item-rules-table':
      return renderItemRulesTableWidget(field.id, currentVal);

    case 'stat-plan-builder':
      return renderStatPlanBuilderWidget(field.id, currentVal);

    case 'skill-plan-builder':
      return renderSkillPlanBuilderWidget(field.id, currentVal);

    case 'skill-rules-builder':
      return renderSkillRulesBuilderWidget(field.id, currentVal);

    case 'equipment-targets-builder':
      return renderEquipmentTargetsWidget(field.id, currentVal);

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

function syncJsonIfVisible() {
  const rawArea = document.getElementById('config-json-editor');
  if (rawArea) {
    rawArea.value = JSON.stringify(currentConfigData, null, 2);
  }
}

function renderRestockTableWidget(fieldId, restockMap) {
  const dict = restockMap || {};
  const entries = Object.entries(dict);
  const essentialList = currentConfigData.EssentialSupplies || [];

  return `
    <div class="config-field-group restock-group" id="field-${fieldId}">
      <div class="field-info">
        <label class="field-title">Supply Restock Targets</label>
        <span class="field-desc">Configures inventory target quotas maintained during town restock routine. Marking an item <strong>Essential</strong> causes the bot to immediately return to town if its supply drops to zero.</span>
      </div>

      <div class="restock-table-wrapper">
        <table class="config-table">
          <thead>
            <tr>
              <th>Supply Item Name</th>
              <th style="width: 140px;">Target Quantity</th>
              <th style="width: 90px; text-align: center;">Essential</th>
              <th style="width: 60px; text-align: center;">Action</th>
            </tr>
          </thead>
          <tbody>
            ${entries.map(([name, count]) => {
              const isEssential = essentialList.some(s => s.toLowerCase() === name.toLowerCase() || s.toLowerCase() === name.replace(/_/g, ' ').toLowerCase());
              return `
              <tr>
                <td style="font-weight: 600; color: #f8fafc;">${name.replace(/_/g, ' ')}</td>
                <td>
                  <input type="number" 
                         class="config-table-input" 
                         value="${count}" 
                         min="0" max="10000" 
                         onchange="updateRestockQuantity('${name}', this.value)">
                </td>
                <td style="text-align: center;">
                  <input type="checkbox" 
                         class="essential-checkbox" 
                         title="Mark as essential supply (triggers return to town when 0)"
                         ${isEssential ? 'checked' : ''} 
                         onchange="toggleRestockEssential('${name}', this.checked)">
                </td>
                <td style="text-align: center;">
                  <button type="button" class="btn-delete-row" onclick="removeRestockItem('${name}')">&times;</button>
                </td>
              </tr>
            `}).join('')}
            <tr class="add-row">
              <td>
                <input type="text" class="config-table-input" id="new-restock-name" placeholder="Item name (e.g. Red_Potion)...">
              </td>
              <td>
                <input type="number" class="config-table-input" id="new-restock-qty" value="50" min="1" max="10000">
              </td>
              <td style="text-align: center;">
                <input type="checkbox" 
                       class="essential-checkbox" 
                       id="new-restock-essential" 
                       title="Mark as essential supply">
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
  syncJsonIfVisible();
}

function toggleRestockEssential(name, isEssential) {
  if (!currentConfigData.EssentialSupplies) currentConfigData.EssentialSupplies = [];
  const norm = name.toLowerCase();
  const normSpace = name.replace(/_/g, ' ').toLowerCase();

  currentConfigData.EssentialSupplies = currentConfigData.EssentialSupplies.filter(
    s => s.toLowerCase() !== norm && s.toLowerCase() !== normSpace
  );

  if (isEssential) {
    currentConfigData.EssentialSupplies.push(name);
  }
  syncJsonIfVisible();
}

function removeRestockItem(name) {
  if (currentConfigData.RestockTargets && currentConfigData.RestockTargets[name] !== undefined) {
    delete currentConfigData.RestockTargets[name];
  }
  if (currentConfigData.EssentialSupplies) {
    const norm = name.toLowerCase();
    const normSpace = name.replace(/_/g, ' ').toLowerCase();
    currentConfigData.EssentialSupplies = currentConfigData.EssentialSupplies.filter(
      s => s.toLowerCase() !== norm && s.toLowerCase() !== normSpace
    );
  }
  syncJsonIfVisible();
  renderActiveCategoryForm();
}

function addNewRestockTarget() {
  const nameInput = document.getElementById('new-restock-name');
  const qtyInput = document.getElementById('new-restock-qty');
  const essentialInput = document.getElementById('new-restock-essential');
  if (!nameInput || !qtyInput) return;

  const name = nameInput.value.trim().replace(/\s+/g, '_');
  const qty = parseInt(qtyInput.value, 10) || 1;
  const isEssential = essentialInput ? essentialInput.checked : false;

  if (name) {
    if (!currentConfigData.RestockTargets) currentConfigData.RestockTargets = {};
    currentConfigData.RestockTargets[name] = qty;

    if (isEssential) {
      if (!currentConfigData.EssentialSupplies) currentConfigData.EssentialSupplies = [];
      const norm = name.toLowerCase();
      if (!currentConfigData.EssentialSupplies.some(s => s.toLowerCase() === norm)) {
        currentConfigData.EssentialSupplies.push(name);
      }
    }
    syncJsonIfVisible();
    renderActiveCategoryForm();
  }
}

// --------------------------------------------------------------------------
// Vended Consumables Table Widget
// --------------------------------------------------------------------------

function getVendConsumableTargetsMap() {
  if (currentConfigData.VendConsumableTargets && typeof currentConfigData.VendConsumableTargets === 'object' && !Array.isArray(currentConfigData.VendConsumableTargets)) {
    return currentConfigData.VendConsumableTargets;
  }
  const defaultTarget = currentConfigData.VendingTargetCartStock || 100;
  const map = {};
  if (Array.isArray(currentConfigData.VendConsumables)) {
    currentConfigData.VendConsumables.forEach(item => {
      map[item] = defaultTarget;
    });
  } else if (currentConfigData.VendConsumables && typeof currentConfigData.VendConsumables === 'object') {
    Object.assign(map, currentConfigData.VendConsumables);
  }
  currentConfigData.VendConsumableTargets = map;
  return map;
}

function getVendConsumableMinStockMap() {
  if (currentConfigData.VendConsumableMinStock && typeof currentConfigData.VendConsumableMinStock === 'object' && !Array.isArray(currentConfigData.VendConsumableMinStock)) {
    return currentConfigData.VendConsumableMinStock;
  }
  const map = {
    "Silver_Arrow": 2000,
    "Red_Potion": 10,
    "Concentration_Potion": 5,
    "Awakening_Potion": 5,
    "Butterfly_Wing": 10,
    "Fly_Wing": 50
  };
  currentConfigData.VendConsumableMinStock = map;
  return map;
}

function syncVendConsumablesList() {
  if (currentConfigData.VendConsumableTargets) {
    currentConfigData.VendConsumables = Object.keys(currentConfigData.VendConsumableTargets);
  }
}

function renderVendConsumablesTableWidget(fieldId, currentVal) {
  const dict = getVendConsumableTargetsMap();
  const minMap = getVendConsumableMinStockMap();
  const entries = Object.entries(dict);

  return `
    <div class="config-field-group restock-group" id="field-${fieldId}">
      <div class="field-info">
        <label class="field-title">Vended Consumables & Target Stock (1z)</label>
        <span class="field-desc">Configures 1 Zeny consumable supplies stocked in pushcart and vended to fleet members line-by-line with target quantities and restock thresholds.</span>
      </div>

      <div class="restock-table-wrapper">
        <table class="config-table">
          <thead>
            <tr>
              <th>Vended Item Name</th>
              <th style="width: 140px;">Target Stock (Cart)</th>
              <th style="width: 150px;">Min Stock (Restock Below)</th>
              <th style="width: 60px; text-align: center;">Action</th>
            </tr>
          </thead>
          <tbody>
            ${entries.map(([name, count]) => {
              const minVal = minMap[name] !== undefined ? minMap[name] : Math.floor(count / 2);
              return `
              <tr>
                <td style="font-weight: 600; color: #f8fafc;">${name.replace(/_/g, ' ')}</td>
                <td>
                  <input type="number" 
                         class="config-table-input" 
                         value="${count}" 
                         min="1" max="10000" 
                         onchange="updateVendConsumableQuantity('${name}', this.value)">
                </td>
                <td>
                  <input type="number" 
                         class="config-table-input" 
                         value="${minVal}" 
                         min="0" max="10000" 
                         onchange="updateVendConsumableMinStock('${name}', this.value)">
                </td>
                <td style="text-align: center;">
                  <button type="button" class="btn-delete-row" onclick="removeVendConsumableItem('${name}')">&times;</button>
                </td>
              </tr>
            `}).join('')}
            <tr class="add-row">
              <td>
                <input type="text" class="config-table-input" id="new-vend-name" placeholder="Item name (e.g. Silver_Arrow)..." onkeydown="if(event.key==='Enter') addNewVendConsumableTarget()">
              </td>
              <td>
                <input type="number" class="config-table-input" id="new-vend-qty" value="100" min="1" max="10000" placeholder="Target" onkeydown="if(event.key==='Enter') addNewVendConsumableTarget()">
              </td>
              <td>
                <input type="number" class="config-table-input" id="new-vend-min" value="50" min="0" max="10000" placeholder="Min Stock" onkeydown="if(event.key==='Enter') addNewVendConsumableTarget()">
              </td>
              <td style="text-align: center;">
                <button type="button" class="btn btn-secondary btn-sm" onclick="addNewVendConsumableTarget()">Add</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  `;
}

function updateVendConsumableQuantity(name, count) {
  const dict = getVendConsumableTargetsMap();
  dict[name] = parseInt(count, 10) || 1;
  syncVendConsumablesList();
  syncJsonIfVisible();
}

function updateVendConsumableMinStock(name, minVal) {
  const minMap = getVendConsumableMinStockMap();
  minMap[name] = parseInt(minVal, 10) || 0;
  syncVendConsumablesList();
  syncJsonIfVisible();
}

function removeVendConsumableItem(name) {
  const dict = getVendConsumableTargetsMap();
  delete dict[name];
  const minMap = getVendConsumableMinStockMap();
  delete minMap[name];
  syncVendConsumablesList();
  syncJsonIfVisible();
  renderActiveCategoryForm();
}

function addNewVendConsumableTarget() {
  const nameInput = document.getElementById('new-vend-name');
  const qtyInput = document.getElementById('new-vend-qty');
  const minInput = document.getElementById('new-vend-min');
  if (!nameInput || !qtyInput) return;

  const rawName = nameInput.value.trim();
  if (!rawName) return;
  const name = rawName.replace(/\s+/g, '_');
  const qty = parseInt(qtyInput.value, 10) || 100;
  const minQty = minInput && minInput.value ? (parseInt(minInput.value, 10) || Math.floor(qty / 2)) : Math.floor(qty / 2);

  const dict = getVendConsumableTargetsMap();
  dict[name] = qty;
  const minMap = getVendConsumableMinStockMap();
  minMap[name] = minQty;
  syncVendConsumablesList();
  syncJsonIfVisible();
  renderActiveCategoryForm();
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
              <th style="width: 110px; text-align: center;">Max Count</th>
              <th style="width: 50px; text-align: center;">Delete</th>
            </tr>
          </thead>
          <tbody>
            ${entries.length > 0 ? entries.map(([itemName, action]) => {
              const maxVal = (currentConfigData.ItemRuleLimits && currentConfigData.ItemRuleLimits[itemName] > 0) 
                ? currentConfigData.ItemRuleLimits[itemName] 
                : '';
              return `
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
                    <input type="number" 
                           class="config-table-input" 
                           style="width: 75px; text-align: center; margin: 0 auto;" 
                           placeholder="∞" 
                           min="0"
                           value="${maxVal}" 
                           onchange="setItemRuleLimit('${itemName}', this.value)">
                  </td>
                  <td style="text-align: center;">
                    <button type="button" class="btn-delete-row" onclick="deleteItemRule('${itemName}')">&times;</button>
                  </td>
                </tr>
              `;
            }).join('') : `
              <tr>
                <td colspan="4" style="text-align: center; color: var(--text-muted); padding: 24px;">No items match the current filter.</td>
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
                <input type="number" class="config-table-input" id="new-item-rule-max" placeholder="∞ (opt)" min="0" style="width: 75px; text-align: center; margin: 0 auto;">
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

function setItemRuleLimit(itemName, maxCountStr) {
  if (!currentConfigData.ItemRuleLimits) currentConfigData.ItemRuleLimits = {};
  const val = parseInt(maxCountStr, 10);
  if (!isNaN(val) && val > 0) {
    currentConfigData.ItemRuleLimits[itemName] = val;
  } else {
    delete currentConfigData.ItemRuleLimits[itemName];
  }
}

function deleteItemRule(itemName) {
  if (currentConfigData.ItemRules && currentConfigData.ItemRules[itemName] !== undefined) {
    delete currentConfigData.ItemRules[itemName];
  }
  if (currentConfigData.ItemRuleLimits && currentConfigData.ItemRuleLimits[itemName] !== undefined) {
    delete currentConfigData.ItemRuleLimits[itemName];
  }
  renderActiveCategoryForm();
}

function addNewItemRule() {
  const nameInput = document.getElementById('new-item-rule-name');
  const actionSelect = document.getElementById('new-item-rule-action');
  const maxInput = document.getElementById('new-item-rule-max');
  if (!nameInput || !actionSelect) return;

  const name = nameInput.value.trim();
  const action = actionSelect.value;
  const maxVal = maxInput ? parseInt(maxInput.value, 10) : 0;

  if (name) {
    if (!currentConfigData.ItemRules) currentConfigData.ItemRules = {};
    currentConfigData.ItemRules[name] = action;
    if (!isNaN(maxVal) && maxVal > 0) {
      if (!currentConfigData.ItemRuleLimits) currentConfigData.ItemRuleLimits = {};
      currentConfigData.ItemRuleLimits[name] = maxVal;
    }
    nameInput.value = '';
    if (maxInput) maxInput.value = '';
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
// Ragnarok Rebuild Skills Database & Autocorrect Registry
// --------------------------------------------------------------------------

const ALL_REBUILD_SKILLS = [
  // Novice
  { name: "Basic Mastery", job: "Novice", enum: "BasicMastery", aliases: ["Basic Skill", "Basic"] },
  { name: "First Aid", job: "Novice", enum: "FirstAid", aliases: [] },

  // Swordsman & Knight
  { name: "Sword Mastery", job: "Swordsman", enum: "SwordMastery", aliases: ["1H Sword Mastery"] },
  { name: "Two-Hand Sword Mastery", job: "Swordsman", enum: "TwoHandSwordMastery", aliases: ["2H Sword Mastery", "2HSwordMastery"] },
  { name: "Improved HP Recovery", job: "Swordsman", enum: "IncreasedHPRecovery", aliases: ["Increase HP Recovery", "Increased HP Recovery", "HP Recovery", "HPR"] },
  { name: "Bash", job: "Swordsman", enum: "Bash", aliases: [] },
  { name: "Magnum Break", job: "Swordsman", enum: "MagnumBreak", aliases: ["MB"] },
  { name: "Provoke", job: "Swordsman", enum: "Provoke", aliases: [] },
  { name: "Endure", job: "Swordsman", enum: "Endure", aliases: [] },
  { name: "Charge Attack", job: "Swordsman", enum: "ChargeAttack", aliases: [] },
  { name: "Two-Hand Quicken", job: "Swordsman", enum: "TwoHandQuicken", aliases: ["2HQ", "2H Quicken"] },
  { name: "Bowling Bash", job: "Knight", enum: "BowlingBash", aliases: ["BB"] },
  { name: "Counter Attack", job: "Knight", enum: "CounterAttack", aliases: ["Auto Counter"] },
  { name: "Pierce", job: "Knight", enum: "Pierce", aliases: [] },
  { name: "Spear Stab", job: "Knight", enum: "SpearStab", aliases: [] },
  { name: "Brandish Spear", job: "Knight", enum: "BrandishSpear", aliases: ["Brandish"] },
  { name: "Spear Boomerang", job: "Knight", enum: "SpearBoomerang", aliases: [] },
  { name: "Spear Mastery", job: "Knight", enum: "SpearMastery", aliases: [] },
  { name: "Peco Peco Riding", job: "Knight", enum: "PecoPecoRiding", aliases: ["Peco Riding"] },
  { name: "Cavalier Mastery", job: "Knight", enum: "CavalierMastery", aliases: [] },

  // Archer & Hunter
  { name: "Owl's Eye", job: "Archer", enum: "OwlEye", aliases: ["Owl Eye", "Owls Eye", "OwlEye"] },
  { name: "Vulture's Eye", job: "Archer", enum: "VultureEye", aliases: ["Vulture Eye", "Vultures Eye", "VultureEye"] },
  { name: "Double Strafe", job: "Archer", enum: "DoubleStrafe", aliases: ["DS"] },
  { name: "Arrow Shower", job: "Archer", enum: "ArrowShower", aliases: ["AS"] },
  { name: "Improve Concentration", job: "Archer", enum: "ImproveConcentration", aliases: ["Improved Concentration", "Attention Concentrate", "Concentration"] },
  { name: "Charge Arrow", job: "Archer", enum: "ChargeArrow", aliases: ["Arrow Repel"] },
  { name: "Beast Bane", job: "Hunter", enum: "BeastBane", aliases: [] },
  { name: "Falcon Mastery", job: "Hunter", enum: "FalconMastery", aliases: [] },
  { name: "Blitz Beat", job: "Hunter", enum: "BlitzBeat", aliases: [] },
  { name: "Steel Crow", job: "Hunter", enum: "SteelCrow", aliases: [] },
  { name: "Ankle Snare", job: "Hunter", enum: "AnkleSnare", aliases: [] },
  { name: "Land Mine", job: "Hunter", enum: "LandMine", aliases: [] },
  { name: "Remove Trap", job: "Hunter", enum: "RemoveTrap", aliases: [] },
  { name: "Spring Trap", job: "Hunter", enum: "SpringTrap", aliases: [] },
  { name: "Skid Trap", job: "Hunter", enum: "SkidTrap", aliases: [] },
  { name: "Freezing Trap", job: "Hunter", enum: "FreezingTrap", aliases: [] },
  { name: "Sandman", job: "Hunter", enum: "Sandman", aliases: [] },
  { name: "Blast Mine", job: "Hunter", enum: "BlastMine", aliases: [] },
  { name: "Claymore Trap", job: "Hunter", enum: "ClaymoreTrap", aliases: [] },
  { name: "Talkie Box", job: "Hunter", enum: "TalkieBox", aliases: [] },
  { name: "Phantasmic Arrow", job: "Hunter", enum: "PhantasmicArrow", aliases: [] },

  // Mage & Wizard
  { name: "Improved Spiritual Recovery", job: "Mage", enum: "IncreaseSPRecovery", aliases: ["Increase SP Recovery", "Increased SP Recovery", "SP Recovery", "SPR", "IncreaseSPRecovery"] },
  { name: "Fire Bolt", job: "Mage", enum: "FireBolt", aliases: ["FB"] },
  { name: "Fireball", job: "Mage", enum: "FireBall", aliases: ["Fire Ball", "FireBall"] },
  { name: "Fire Wall", job: "Mage", enum: "FireWall", aliases: ["FW"] },
  { name: "Cold Bolt", job: "Mage", enum: "ColdBolt", aliases: ["CB"] },
  { name: "Frost Diver", job: "Mage", enum: "FrostDiver", aliases: ["FD"] },
  { name: "Lightning Bolt", job: "Mage", enum: "LightningBolt", aliases: ["LB"] },
  { name: "Thunderstorm", job: "Mage", enum: "ThunderStorm", aliases: ["Thunder Storm", "ThunderStorm", "TS"] },
  { name: "Napalm Beat", job: "Mage", enum: "NapalmBeat", aliases: ["NB"] },
  { name: "Soul Strike", job: "Mage", enum: "SoulStrike", aliases: ["SS"] },
  { name: "Safety Wall", job: "Mage", enum: "SafetyWall", aliases: ["SW"] },
  { name: "Stone Curse", job: "Mage", enum: "StoneCurse", aliases: ["SC"] },
  { name: "Sight", job: "Mage", enum: "Sight", aliases: [] },
  { name: "Energy Coat", job: "Mage", enum: "EnergyCoat", aliases: ["EC"] },
  { name: "Earth Spike", job: "Wizard", enum: "EarthSpike", aliases: [] },
  { name: "Heaven's Drive", job: "Wizard", enum: "HeavensDrive", aliases: ["Heavens Drive", "HD"] },
  { name: "Water Ball", job: "Wizard", enum: "WaterBall", aliases: ["Waterball", "WB"] },
  { name: "Jupitel Thunder", job: "Wizard", enum: "JupitelThunder", aliases: ["JT"] },
  { name: "Lord of Vermilion", job: "Wizard", enum: "LordOfVermilion", aliases: ["LoV"] },
  { name: "Meteor Storm", job: "Wizard", enum: "MeteorStorm", aliases: ["MS"] },
  { name: "Storm Gust", job: "Wizard", enum: "StormGust", aliases: ["SG"] },
  { name: "Quagmire", job: "Wizard", enum: "Quagmire", aliases: ["QM"] },
  { name: "Frost Nova", job: "Wizard", enum: "FrostNova", aliases: ["FN"] },
  { name: "Fire Pillar", job: "Wizard", enum: "FirePillar", aliases: ["FP"] },
  { name: "Ice Wall", job: "Wizard", enum: "IceWall", aliases: ["IW"] },
  { name: "Sightrasher", job: "Wizard", enum: "Sightrasher", aliases: [] },
  { name: "Sense", job: "Wizard", enum: "Sense", aliases: [] },

  // Thief & Assassin
  { name: "Double Attack", job: "Thief", enum: "DoubleAttack", aliases: ["DA"] },
  { name: "Improve Dodge", job: "Thief", enum: "ImproveDodge", aliases: ["Improved Dodge", "Increase Dodge", "Dodge"] },
  { name: "Envenom", job: "Thief", enum: "Envenom", aliases: [] },
  { name: "Detoxify", job: "Thief", enum: "Detoxify", aliases: ["Detox"] },
  { name: "Steal", job: "Thief", enum: "Steal", aliases: [] },
  { name: "Hiding", job: "Thief", enum: "Hiding", aliases: ["Hide"] },
  { name: "Back Slide", job: "Thief", enum: "BackSlide", aliases: ["Backslide"] },
  { name: "Sand Attack", job: "Thief", enum: "SandAttack", aliases: [] },
  { name: "Stone Fling", job: "Thief", enum: "ThrowStone", aliases: ["Throw Stone", "ThrowStone"] },
  { name: "Find Stone", job: "Thief", enum: "FindStone", aliases: ["Pick Stone", "PickStone"] },
  { name: "Sonic Blow", job: "Assassin", enum: "SonicBlow", aliases: ["SB"] },
  { name: "Enchant Poison", job: "Assassin", enum: "EnchantPoison", aliases: ["EP"] },
  { name: "Cloaking", job: "Assassin", enum: "Cloaking", aliases: ["Cloak"] },
  { name: "Katar Mastery", job: "Assassin", enum: "KatarMastery", aliases: [] },
  { name: "Right-Hand Mastery", job: "Assassin", enum: "RightHandMastery", aliases: ["Right Hand Mastery", "Righthand Mastery"] },
  { name: "Left-Hand Mastery", job: "Assassin", enum: "LeftHandMastery", aliases: ["Left Hand Mastery", "Lefthand Mastery"] },
  { name: "Grimtooth", job: "Assassin", enum: "Grimtooth", aliases: ["Grim"] },
  { name: "Poison React", job: "Assassin", enum: "PoisonReact", aliases: [] },
  { name: "Venom Dust", job: "Assassin", enum: "VenomDust", aliases: [] },
  { name: "Venom Splasher", job: "Assassin", enum: "VenomSplasher", aliases: [] },
  { name: "Sonic Acceleration", job: "Assassin", enum: "SonicAcceleration", aliases: [] },
  { name: "Venom Knife", job: "Assassin", enum: "VenomKnife", aliases: [] },

  // Acolyte & Priest
  { name: "Divine Protection", job: "Acolyte", enum: "DivineProtection", aliases: ["DP"] },
  { name: "Demon Bane", job: "Acolyte", enum: "DemonBane", aliases: ["DB"] },
  { name: "Heal", job: "Acolyte", enum: "Heal", aliases: [] },
  { name: "Angelus", job: "Acolyte", enum: "Angelus", aliases: [] },
  { name: "Blessing", job: "Acolyte", enum: "Blessing", aliases: ["Bless"] },
  { name: "Increase Agility", job: "Acolyte", enum: "IncreaseAgility", aliases: ["Increase Agi", "Inc Agi", "IncAgi", "Agi Up", "IA"] },
  { name: "Decrease Agility", job: "Acolyte", enum: "DecreaseAgility", aliases: ["Decrease Agi", "Dec Agi", "DecAgi"] },
  { name: "Cure", job: "Acolyte", enum: "Cure", aliases: [] },
  { name: "Ruwach", job: "Acolyte", enum: "Ruwach", aliases: [] },
  { name: "Teleport", job: "Acolyte", enum: "Teleport", aliases: ["Tele"] },
  { name: "Return", job: "Acolyte", enum: "Return", aliases: ["Teleport2", "Warp to Save Point"] },
  { name: "Warp Portal", job: "Acolyte", enum: "WarpPortal", aliases: ["Warp", "Memo"] },
  { name: "Pneuma", job: "Acolyte", enum: "Pneuma", aliases: [] },
  { name: "Holy Light", job: "Acolyte", enum: "HolyLight", aliases: ["HL"] },
  { name: "Signum Crusis", job: "Acolyte", enum: "SignumCrusis", aliases: ["Signum Crucis", "SignumCrucis"] },
  { name: "Aqua Benedicta", job: "Acolyte", enum: "AquaBenedicta", aliases: ["Holy Water", "Make Holy Water"] },
  { name: "Resurrection", job: "Priest", enum: "Resurrection", aliases: ["Resurrect", "Res"] },
  { name: "Sanctuary", job: "Priest", enum: "Sanctuary", aliases: ["Sanc"] },
  { name: "Kyrie Eleison", job: "Priest", enum: "KyrieEleison", aliases: ["Kyrie", "KE"] },
  { name: "Magnificat", job: "Priest", enum: "Magnificat", aliases: ["Magni"] },
  { name: "Gloria", job: "Priest", enum: "Gloria", aliases: [] },
  { name: "Impositio Manus", job: "Priest", enum: "ImpositioManus", aliases: ["Impositio", "Impo"] },
  { name: "Suffragium", job: "Priest", enum: "Suffragium", aliases: ["Suffra"] },
  { name: "Aspersio", job: "Priest", enum: "Aspersio", aliases: ["Asper"] },
  { name: "Benedictio Sanctissimi Sacramenti", job: "Priest", enum: "Benedicto", aliases: ["Benedictio", "Benedicto", "BSS"] },
  { name: "Lex Aeterna", job: "Priest", enum: "LexAeterna", aliases: ["LA"] },
  { name: "Lex Divina", job: "Priest", enum: "LexDivina", aliases: ["LD"] },
  { name: "Turn Undead", job: "Priest", enum: "TurnUndead", aliases: ["TU"] },
  { name: "Status Recovery", job: "Priest", enum: "StatusRecovery", aliases: ["Recovery"] },
  { name: "Magnus Exorcismus", job: "Priest", enum: "MagnusExorcismus", aliases: ["ME"] },

  // Merchant & Blacksmith
  { name: "Enlarge Weight Limit", job: "Merchant", enum: "EnlargeWeightLimit", aliases: ["Increase Weight Limit", "EWL", "IWL"] },
  { name: "Discount", job: "Merchant", enum: "Discount", aliases: ["DC"] },
  { name: "Overcharge", job: "Merchant", enum: "Overcharge", aliases: ["OC"] },
  { name: "Push Cart", job: "Merchant", enum: "PushCart", aliases: [] },
  { name: "Vending", job: "Merchant", enum: "Vending", aliases: ["Vend"] },
  { name: "Item Appraisal", job: "Merchant", enum: "ItemAppraisal", aliases: ["Weapon Appraisal", "WeaponAppraisal", "Appraise", "Identify"] },
  { name: "Mammonite", job: "Merchant", enum: "Mammonite", aliases: ["Mammo"] },
  { name: "Crazy Uproar", job: "Merchant", enum: "CrazyUproar", aliases: ["Loud Voice", "LoudVoice"] },
  { name: "Cart Revolution", job: "Merchant", enum: "CartRevolution", aliases: ["CR"] },
  { name: "Hammer Fall", job: "Merchant", enum: "HammerFall", aliases: ["HF"] },
  { name: "Adrenaline Rush", job: "Blacksmith", enum: "AdrenalineRush", aliases: ["AR"] },
  { name: "Weapon Perfection", job: "Blacksmith", enum: "WeaponPerfection", aliases: ["WP"] },
  { name: "Power Thrust", job: "Blacksmith", enum: "PowerThrust", aliases: ["PT"] },
  { name: "Maximize Power", job: "Blacksmith", enum: "MaximizePower", aliases: ["MP"] },
  { name: "Skin Tempering", job: "Blacksmith", enum: "SkinTempering", aliases: [] }
];

window.ALL_REBUILD_SKILLS = ALL_REBUILD_SKILLS;

const SKILL_LOOKUP_MAP = {};
ALL_REBUILD_SKILLS.forEach(s => {
  const normKey = (str) => String(str).toLowerCase().replace(/[^a-z0-9]/g, '');
  SKILL_LOOKUP_MAP[normKey(s.name)] = s;
  SKILL_LOOKUP_MAP[normKey(s.enum)] = s;
  if (s.aliases) {
    s.aliases.forEach(a => {
      SKILL_LOOKUP_MAP[normKey(a)] = s;
    });
  }
});

function normalizeSkillName(raw) {
  if (!raw) return '';
  const key = String(raw).toLowerCase().replace(/[^a-z0-9]/g, '');
  if (SKILL_LOOKUP_MAP[key]) {
    return SKILL_LOOKUP_MAP[key].name;
  }
  return raw.trim();
}

function resolveSkillInfo(raw) {
  if (!raw) return null;
  const key = String(raw).toLowerCase().replace(/[^a-z0-9]/g, '');
  return SKILL_LOOKUP_MAP[key] || null;
}

// --------------------------------------------------------------------------
// Skill Build Plan Widget (Sequential Skill Leveling)
// --------------------------------------------------------------------------

function renderSkillPlanBuilderWidget(fieldId, planArray) {
  const plan = Array.isArray(planArray) ? planArray : [];

  return `
    <datalist id="ro-all-skills-datalist">
      ${ALL_REBUILD_SKILLS.map(s => `<option value="${s.name}">${s.job} (${s.enum})</option>`).join('')}
    </datalist>

    <div class="config-field-group skill-plan-group" id="field-${fieldId}">
      <div class="field-info">
        <label class="field-title">Sequential Skill Leveling Plan (${plan.length} steps)</label>
        <span class="field-desc">The bot levels up skills in this exact sequence as skill points are earned. Skill names autocorrect automatically to official definitions.</span>
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
            ${plan.length > 0 ? plan.map((step, idx) => {
              const res = resolveSkillInfo(step.Skill);
              return `
              <tr>
                <td><span class="step-badge">#${idx + 1}</span></td>
                <td>
                  <input type="text" 
                         class="config-table-input" 
                         list="ro-all-skills-datalist"
                         value="${step.Skill || ''}" 
                         placeholder="Skill name (e.g. Improved Spiritual Recovery, Bash)..." 
                         onchange="updateSkillPlanStep(${idx}, 'Skill', this.value)">
                  ${res ? `<span class="badge-pill badge-emerald" style="font-size: 0.65rem; margin-top: 3px; display: inline-block;">✓ ${res.job}</span>` : (step.Skill ? `<span class="badge-pill badge-amber" style="font-size: 0.65rem; margin-top: 3px; display: inline-block;">? Custom / Raw</span>` : '')}
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
            `;}).join('') : `
              <tr>
                <td colspan="5" style="text-align: center; color: var(--text-muted); padding: 20px;">No skill leveling plan configured.</td>
              </tr>
            `}
            <tr class="add-row">
              <td colspan="2">
                <input type="text" class="config-table-input" id="new-skill-name" list="ro-all-skills-datalist" placeholder="Skill name (e.g. Improved Spiritual Recovery, Bash)...">
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
    if (prop === 'Skill') {
      value = normalizeSkillName(value);
    }
    currentConfigData.SkillBuildPlan[index][prop] = value;
    if (prop === 'Skill') {
      renderActiveCategoryForm();
    }
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

  const skill = normalizeSkillName(nameInput.value.trim());
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
              <div class="rule-card-title-group" style="display: flex; align-items: center; gap: 8px; flex-wrap: wrap;">
                <label class="tremor-toggle">
                  <input type="checkbox" ${rule.Enabled !== false ? 'checked' : ''} onchange="updateSkillRule(${idx}, 'Enabled', this.checked)">
                  <span class="toggle-slider"></span>
                </label>
                <input type="text" 
                       class="rule-skill-name-input" 
                       list="ro-all-skills-datalist"
                       value="${rule.Skill || ''}" 
                       placeholder="Skill Name (e.g. Bash, SonicBlow)..." 
                       onchange="updateSkillRule(${idx}, 'Skill', this.value)">
                ${(() => {
                  const rRes = resolveSkillInfo(rule.Skill);
                  return rRes ? `<span class="badge-pill badge-blue" style="font-size: 0.7rem;">✓ ${rRes.job}</span>` : '';
                })()}
                <span class="badge-pill ${rule.Enabled !== false ? 'badge-emerald' : 'badge-slate'}" style="font-size: 0.7rem;">
                  ${rule.Enabled !== false ? 'Active' : 'Disabled'}
                </span>
              </div>
              <button type="button" class="btn-delete-row" onclick="deleteSkillRule(${idx})">&times;</button>
            </div>

            <div class="rule-card-grid">
              ${(() => {
                const isHeal = (rule.Skill || '').trim().toLowerCase() === 'heal';
                if (isHeal) {
                  return `
                    <div class="rule-param">
                      <label>Target</label>
                      <select class="tremor-select" onchange="updateSkillRule(${idx}, 'Target', this.value)">
                        <option value="Party" ${rule.Target === 'Party' || !rule.Target ? 'selected' : ''}>Party</option>
                        <option value="Self" ${rule.Target === 'Self' ? 'selected' : ''}>Self Only</option>
                      </select>
                    </div>
                    <div class="rule-param">
                      <label>Heal to HP %</label>
                      <input type="number" class="config-table-input" value="${rule.HpBelowPercent ?? 80}" min="1" max="99" onchange="updateSkillRule(${idx}, 'HpBelowPercent', parseInt(this.value, 10) || 80)">
                    </div>
                    <div class="rule-param">
                      <label>Min SP Reserve %</label>
                      <input type="number" class="config-table-input" value="${rule.MinSpPercent ?? 20}" min="0" max="100" onchange="updateSkillRule(${idx}, 'MinSpPercent', parseInt(this.value, 10) || 0)">
                    </div>
                    <div class="rule-param">
                      <label>Cooldown (s)</label>
                      <input type="number" class="config-table-input" value="${rule.CooldownSeconds ?? 1.0}" min="0.1" max="60" step="0.1" onchange="updateSkillRule(${idx}, 'CooldownSeconds', parseFloat(this.value) || 1.0)">
                    </div>
                  `;
                }

                return `
                  <div class="rule-param">
                    <label>Trigger Condition</label>
                    <select class="tremor-select" onchange="updateSkillRule(${idx}, 'Trigger', this.value)">
                      <option value="Combat" ${rule.Trigger === 'Combat' ? 'selected' : ''}>Combat (Spammed in battle)</option>
                      <option value="Opener" ${rule.Trigger === 'Opener' ? 'selected' : ''}>Opener (Cast once on engage)</option>
                      <option value="BuffMaintenance" ${rule.Trigger === 'BuffMaintenance' ? 'selected' : ''}>Buff Maintenance (Maintain active)</option>
                      <option value="PartyBuff" ${rule.Trigger === 'PartyBuff' ? 'selected' : ''}>Party Buff (Maintain on party members)</option>
                      <option value="HpBelowPercent" ${rule.Trigger === 'HpBelowPercent' ? 'selected' : ''}>Emergency Heal (HP Below %)</option>
                      <option value="MobCluster" ${rule.Trigger === 'MobCluster' ? 'selected' : ''}>Mob Cluster (AOE when surrounded)</option>
                    </select>
                  </div>

                  ${rule.Trigger !== 'PartyBuff' ? `
                  <div class="rule-param">
                    <label>Target</label>
                    <select class="tremor-select" onchange="updateSkillRule(${idx}, 'Target', this.value)">
                      <option value="Enemy" ${rule.Target === 'Enemy' ? 'selected' : ''}>Target Enemy</option>
                      <option value="Self" ${rule.Target === 'Self' ? 'selected' : ''}>Self Cast</option>
                      <option value="Ground" ${rule.Target === 'Ground' ? 'selected' : ''}>Ground Target</option>
                      <option value="Party" ${rule.Target === 'Party' ? 'selected' : ''}>Party Member</option>
                    </select>
                  </div>
                  ` : ''}

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
                `;
              })()}
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
    if (prop === 'Skill') {
      const normalized = normalizeSkillName(value);
      currentConfigData.SkillRules[index][prop] = normalized;
      if (normalized.trim().toLowerCase() === 'heal') {
        currentConfigData.SkillRules[index].Trigger = 'HpBelowPercent';
        if (currentConfigData.SkillRules[index].Target !== 'Self' && currentConfigData.SkillRules[index].Target !== 'Party') {
          currentConfigData.SkillRules[index].Target = 'Party';
        }
        if (!currentConfigData.SkillRules[index].HpBelowPercent) {
          currentConfigData.SkillRules[index].HpBelowPercent = 80;
        }
      }
      renderActiveCategoryForm();
      return;
    }
    if (prop === 'Trigger' && value === 'PartyBuff') {
      currentConfigData.SkillRules[index].Target = 'Party';
    }
    currentConfigData.SkillRules[index][prop] = value;
    if (prop === 'Trigger' || prop === 'Target') {
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
// Equipment Targets & Upgrades Widget
// --------------------------------------------------------------------------

const KNOWN_SHOP_EQUIPMENT = [
  // Weapons - Axes (Alberta)
  { name: 'Battle Axe', basePrice: 5400, minLevel: 1, rank: 1, slot: 'Weapon' },
  { name: 'Hammer', basePrice: 15500, minLevel: 16, rank: 2, slot: 'Weapon' },
  { name: 'Buster', basePrice: 34000, minLevel: 24, rank: 2, slot: 'Weapon' },
  { name: 'Two-Handed Axe', basePrice: 55000, minLevel: 30, rank: 3, slot: 'Weapon' },
  { name: 'Axe', basePrice: 500, minLevel: 1, rank: 1, slot: 'Weapon' },

  // Weapons - 1H Swords & Spears (Prontera)
  { name: 'Sword', basePrice: 100, minLevel: 1, rank: 1, slot: 'Weapon' },
  { name: 'Falchion', basePrice: 1500, minLevel: 1, rank: 1, slot: 'Weapon' },
  { name: 'Blade', basePrice: 2900, minLevel: 1, rank: 1, slot: 'Weapon' },
  { name: 'Rapier', basePrice: 10000, minLevel: 14, rank: 2, slot: 'Weapon' },
  { name: 'Scimiter', basePrice: 17000, minLevel: 14, rank: 2, slot: 'Weapon' },
  { name: 'Ring Pommel Saber', basePrice: 24000, minLevel: 18, rank: 2, slot: 'Weapon' },
  { name: 'Tsurugi', basePrice: 51000, minLevel: 27, rank: 3, slot: 'Weapon' },
  { name: 'Haedonggum', basePrice: 50000, minLevel: 27, rank: 3, slot: 'Weapon' },
  { name: 'Saber', basePrice: 49000, minLevel: 27, rank: 3, slot: 'Weapon' },
  { name: 'Flamberge', basePrice: 60000, minLevel: 40, rank: 3, slot: 'Weapon' },
  { name: 'Javelin', basePrice: 150, minLevel: 1, rank: 1, slot: 'Weapon' },
  { name: 'Spear', basePrice: 1700, minLevel: 1, rank: 1, slot: 'Weapon' },
  { name: 'Pike', basePrice: 3450, minLevel: 1, rank: 1, slot: 'Weapon' },
  { name: 'Guisarme', basePrice: 13000, minLevel: 14, rank: 2, slot: 'Weapon' },
  { name: 'Glaive', basePrice: 20000, minLevel: 14, rank: 2, slot: 'Weapon' },
  { name: 'Partizan', basePrice: 27000, minLevel: 18, rank: 2, slot: 'Weapon' },
  { name: 'Trident', basePrice: 51000, minLevel: 27, rank: 3, slot: 'Weapon' },
  { name: 'Halberd', basePrice: 54000, minLevel: 27, rank: 3, slot: 'Weapon' },
  { name: 'Lance', basePrice: 60000, minLevel: 40, rank: 3, slot: 'Weapon' },

  // Weapons - 2H Swords (Izlude)
  { name: 'Katana', basePrice: 2000, minLevel: 1, rank: 1, slot: 'Weapon' },
  { name: 'Bastard Sword', basePrice: 22500, minLevel: 20, rank: 2, slot: 'Weapon' },
  { name: 'Slayer', basePrice: 34000, minLevel: 24, rank: 2, slot: 'Weapon' },
  { name: 'Two-Handed Sword', basePrice: 60000, minLevel: 33, rank: 3, slot: 'Weapon' },
  { name: 'Broad Sword', basePrice: 65000, minLevel: 48, rank: 3, slot: 'Weapon' },

  // Weapons - Maces (Prontera Church)
  { name: 'Club', basePrice: 100, minLevel: 1, rank: 1, slot: 'Weapon' },
  { name: 'Mace', basePrice: 2500, minLevel: 1, rank: 1, slot: 'Weapon' },
  { name: 'Smasher', basePrice: 9000, minLevel: 14, rank: 2, slot: 'Weapon' },
  { name: 'Flail', basePrice: 16000, minLevel: 14, rank: 2, slot: 'Weapon' },
  { name: 'Chain', basePrice: 23000, minLevel: 18, rank: 2, slot: 'Weapon' },
  { name: 'Morning Star', basePrice: 43000, minLevel: 27, rank: 3, slot: 'Weapon' },

  // Weapons - Bows (Payon)
  { name: 'Bow', basePrice: 1000, minLevel: 1, rank: 1, slot: 'Weapon' },
  { name: 'Composite Bow', basePrice: 2500, minLevel: 1, rank: 1, slot: 'Weapon' },
  { name: 'Great Bow', basePrice: 10000, minLevel: 18, rank: 2, slot: 'Weapon' },
  { name: 'Cross Bow', basePrice: 17000, minLevel: 18, rank: 2, slot: 'Weapon' },
  { name: 'Arbalest Bow', basePrice: 48000, minLevel: 33, rank: 3, slot: 'Weapon' },
  { name: 'Gakkung Bow', basePrice: 42000, minLevel: 33, rank: 3, slot: 'Weapon' },
  { name: 'Hunter Bow', basePrice: 64000, minLevel: 55, rank: 3, slot: 'Weapon' },

  // Weapons - Daggers & Katars (Morroc)
  { name: 'Knife', basePrice: 50, minLevel: 1, rank: 1, slot: 'Weapon' },
  { name: 'Cutter', basePrice: 1250, minLevel: 1, rank: 1, slot: 'Weapon' },
  { name: 'Main Gauche', basePrice: 2400, minLevel: 1, rank: 1, slot: 'Weapon' },
  { name: 'Dirk', basePrice: 8500, minLevel: 12, rank: 2, slot: 'Weapon' },
  { name: 'Dagger', basePrice: 14000, minLevel: 12, rank: 2, slot: 'Weapon' },
  { name: 'Stiletto', basePrice: 19500, minLevel: 12, rank: 2, slot: 'Weapon' },
  { name: 'Gladius', basePrice: 43000, minLevel: 24, rank: 3, slot: 'Weapon' },
  { name: 'Damascus', basePrice: 49000, minLevel: 24, rank: 3, slot: 'Weapon' },
  { name: 'Jur', basePrice: 28000, minLevel: 18, rank: 2, slot: 'Weapon' },
  { name: 'Katar', basePrice: 41000, minLevel: 24, rank: 3, slot: 'Weapon' },
  { name: 'Jamadhar', basePrice: 37000, minLevel: 24, rank: 3, slot: 'Weapon' },

  // Weapons - Staves & Rods (Geffen)
  { name: 'Rod', basePrice: 50, minLevel: 1, rank: 1, slot: 'Weapon' },
  { name: 'Wand', basePrice: 2500, minLevel: 1, rank: 1, slot: 'Weapon' },
  { name: 'Staff', basePrice: 9500, minLevel: 12, rank: 2, slot: 'Weapon' },
  { name: 'Arc Wand', basePrice: 45000, minLevel: 24, rank: 3, slot: 'Weapon' },

  // Armor, Shields, Garments, Boots
  { name: 'Guard', basePrice: 500, minLevel: 1, rank: 0, slot: 'Shield' },
  { name: 'Buckler', basePrice: 14000, minLevel: 1, rank: 0, slot: 'Shield' },
  { name: 'Shield', basePrice: 56000, minLevel: 1, rank: 0, slot: 'Shield' },
  { name: 'Sandals', basePrice: 400, minLevel: 1, rank: 0, slot: 'Footgear' },
  { name: 'Shoes', basePrice: 3500, minLevel: 1, rank: 0, slot: 'Footgear' },
  { name: 'Boots', basePrice: 18000, minLevel: 1, rank: 0, slot: 'Footgear' },
  { name: 'Hood', basePrice: 1000, minLevel: 1, rank: 0, slot: 'Garment' },
  { name: 'Muffler', basePrice: 5000, minLevel: 1, rank: 0, slot: 'Garment' },
  { name: 'Manteau', basePrice: 32000, minLevel: 1, rank: 0, slot: 'Garment' },
  { name: 'Cotton Shirt', basePrice: 10, minLevel: 1, rank: 0, slot: 'Armor' },
  { name: 'Jacket', basePrice: 200, minLevel: 1, rank: 0, slot: 'Armor' },
  { name: 'Adventurer\'s Suit', basePrice: 1000, minLevel: 1, rank: 0, slot: 'Armor' },
  { name: 'Wooden Mail', basePrice: 5500, minLevel: 14, rank: 0, slot: 'Armor' },
  { name: 'Mantle', basePrice: 10000, minLevel: 14, rank: 0, slot: 'Armor' },
  { name: 'Coat', basePrice: 11000, minLevel: 14, rank: 0, slot: 'Armor' },
  { name: 'Padded Armor', basePrice: 28000, minLevel: 18, rank: 0, slot: 'Armor' },
  { name: 'Chain Mail', basePrice: 65000, minLevel: 24, rank: 0, slot: 'Armor' },
  { name: 'Full Plate', basePrice: 80000, minLevel: 40, rank: 0, slot: 'Armor' },
  { name: 'Silk Robe', basePrice: 8000, minLevel: 1, rank: 0, slot: 'Armor' },
  { name: 'Silver Robe', basePrice: 7000, minLevel: 18, rank: 0, slot: 'Armor' },
  { name: 'Saint\'s Robe', basePrice: 54000, minLevel: 24, rank: 0, slot: 'Armor' },
  { name: 'Tights', basePrice: 71000, minLevel: 30, rank: 0, slot: 'Armor' },
  { name: 'Thief Clothes', basePrice: 74000, minLevel: 30, rank: 0, slot: 'Armor' }
];

function calculateEquipmentTargetZeny(item, refine) {
  const base = item.basePrice || 0;
  if (refine <= 0) {
    return Math.round(base * 1.5);
  }
  if (item.rank === 1) {
    return base + (refine * 250);
  } else if (item.rank === 2) {
    return Math.round(base + (refine * 1107.15));
  } else if (item.rank === 3) {
    return Math.round(base + (refine * 3928.57));
  } else {
    return base + (refine * 2000);
  }
}

function onNewEquipmentInputChanged() {
  const nameInput = document.getElementById('new-equip-name');
  const refineSelect = document.getElementById('new-equip-refine');
  const levelInput = document.getElementById('new-equip-level');
  const zenyInput = document.getElementById('new-equip-zeny');
  if (!nameInput || !refineSelect || !levelInput || !zenyInput) return;

  const itemName = nameInput.value.trim();
  const refine = parseInt(refineSelect.value, 10) || 0;
  const match = KNOWN_SHOP_EQUIPMENT.find(
    e => e.name.toLowerCase() === itemName.toLowerCase() || e.name.replace(/ /g, '_').toLowerCase() === itemName.toLowerCase()
  );

  if (match) {
    levelInput.value = match.minLevel;
    zenyInput.value = calculateEquipmentTargetZeny(match, refine);
  }
}

function renderEquipmentTargetsWidget(fieldId, targetsArray) {
  const targets = Array.isArray(targetsArray) ? targetsArray : [];

  return `
    <div class="config-field-group equipment-targets-group" id="field-${fieldId}">
      <div class="field-info">
        <label class="field-title">Equipment Targets & Upgrades (${targets.length} goals)</label>
        <span class="field-desc">The bot automatically purchases equipment from NPC vendors and upgrades it at Hollgrehenn / Dietrich in Prontera. Targets are evaluated goal-oriented (aiming for the highest achieved milestone whose level, zeny, and ore requirements are met).</span>
      </div>

      <div class="plan-table-wrapper">
        <table class="config-table plan-table">
          <thead>
            <tr>
              <th style="width: 50px;">Order</th>
              <th>Target Equipment Item</th>
              <th style="width: 100px; text-align: center;">Refine Target</th>
              <th style="width: 100px; text-align: center;">Min Level</th>
              <th style="width: 130px; text-align: right;">Min Zeny</th>
              <th style="width: 80px; text-align: center;">Reorder</th>
              <th style="width: 50px; text-align: center;">Delete</th>
            </tr>
          </thead>
          <tbody>
            ${targets.length > 0 ? targets.map((t, idx) => `
              <tr>
                <td><span class="step-badge">#${idx + 1}</span></td>
                <td>
                  <input type="text" 
                         list="known-equipment-datalist"
                         class="config-table-input" 
                         style="font-weight: 600; color: #f8fafc;" 
                         value="${t.ItemName || ''}" 
                         placeholder="Item Name..."
                         onchange="updateEquipmentTarget(${idx}, 'ItemName', this.value)">
                </td>
                <td style="text-align: center;">
                  <select class="tremor-select" style="padding: 4px 6px; font-weight: 600;" onchange="updateEquipmentTarget(${idx}, 'TargetRefineLevel', parseInt(this.value, 10) || 0)">
                    ${[0,1,2,3,4,5,6,7,8,9,10].map(r => `
                      <option value="${r}" ${t.TargetRefineLevel === r ? 'selected' : ''}>+${r}</option>
                    `).join('')}
                  </select>
                </td>
                <td style="text-align: center;">
                  <input type="number" 
                         class="config-table-input" 
                         style="text-align: center; width: 70px;" 
                         value="${t.MinLevel || 1}" 
                         min="1" max="99" 
                         onchange="updateEquipmentTarget(${idx}, 'MinLevel', parseInt(this.value, 10) || 1)">
                </td>
                <td style="text-align: right;">
                  <div style="display: inline-flex; align-items: center; gap: 4px;">
                    <input type="number" 
                           class="config-table-input" 
                           style="text-align: right; width: 100px; color: #fbbf24; font-weight: 600;" 
                           value="${t.MinZeny || 0}" 
                           min="0" step="500" 
                           onchange="updateEquipmentTarget(${idx}, 'MinZeny', parseInt(this.value, 10) || 0)">
                    <span style="color: var(--text-muted); font-size: 0.75rem;">z</span>
                  </div>
                </td>
                <td style="text-align: center;">
                  <button type="button" class="btn-reorder" onclick="moveEquipmentTarget(${idx}, -1)" ${idx === 0 ? 'disabled style="opacity:0.3; cursor:default;"' : ''}>▲</button>
                  <button type="button" class="btn-reorder" onclick="moveEquipmentTarget(${idx}, 1)" ${idx === targets.length - 1 ? 'disabled style="opacity:0.3; cursor:default;"' : ''}>▼</button>
                </td>
                <td style="text-align: center;">
                  <button type="button" class="btn-delete-row" onclick="deleteEquipmentTarget(${idx})">&times;</button>
                </td>
              </tr>
            `).join('') : `
              <tr>
                <td colspan="7" style="text-align: center; color: var(--text-muted); padding: 20px;">No equipment targets configured. Bot will not auto-purchase or refine equipment.</td>
              </tr>
            `}
            <tr class="add-row">
              <td><span style="color: var(--text-muted); font-size: 0.75rem;">New:</span></td>
              <td>
                <input type="text" 
                       id="new-equip-name" 
                       list="known-equipment-datalist"
                       class="config-table-input" 
                       placeholder="Select or type equipment (e.g. Two-Handed Axe)..."
                       oninput="onNewEquipmentInputChanged()">
              </td>
              <td style="text-align: center;">
                <select class="tremor-select" id="new-equip-refine" style="padding: 4px 6px; font-weight: 600;" onchange="onNewEquipmentInputChanged()">
                  ${[0,1,2,3,4,5,6,7,8,9,10].map(r => `
                    <option value="${r}">+${r}</option>
                  `).join('')}
                </select>
              </td>
              <td style="text-align: center;">
                <input type="number" class="config-table-input" id="new-equip-level" value="1" min="1" max="99" style="text-align: center; width: 70px;">
              </td>
              <td style="text-align: right;">
                <div style="display: inline-flex; align-items: center; gap: 4px;">
                  <input type="number" class="config-table-input" id="new-equip-zeny" value="0" min="0" step="500" style="text-align: right; width: 100px; color: #fbbf24; font-weight: 600;">
                  <span style="color: var(--text-muted); font-size: 0.75rem;">z</span>
                </div>
              </td>
              <td colspan="2" style="text-align: center;">
                <button type="button" class="btn btn-secondary btn-sm" onclick="addNewEquipmentTarget()">+ Add Target</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <datalist id="known-equipment-datalist">
        ${KNOWN_SHOP_EQUIPMENT.map(item => `
          <option value="${item.name}">${item.slot} | Lv ${item.minLevel} | ${item.basePrice.toLocaleString()}z</option>
        `).join('')}
      </datalist>
    </div>
  `;
}

function updateEquipmentTarget(index, prop, value) {
  if (!Array.isArray(currentConfigData.EquipmentTargets)) currentConfigData.EquipmentTargets = [];
  if (currentConfigData.EquipmentTargets[index]) {
    currentConfigData.EquipmentTargets[index][prop] = value;
    syncJsonIfVisible();
  }
}

function moveEquipmentTarget(index, direction) {
  if (!Array.isArray(currentConfigData.EquipmentTargets)) return;
  const list = currentConfigData.EquipmentTargets;
  const targetIdx = index + direction;
  if (targetIdx < 0 || targetIdx >= list.length) return;

  const temp = list[index];
  list[index] = list[targetIdx];
  list[targetIdx] = temp;
  syncJsonIfVisible();
  renderActiveCategoryForm();
}

function deleteEquipmentTarget(index) {
  if (!Array.isArray(currentConfigData.EquipmentTargets)) return;
  currentConfigData.EquipmentTargets.splice(index, 1);
  syncJsonIfVisible();
  renderActiveCategoryForm();
}

function addNewEquipmentTarget() {
  const nameInput = document.getElementById('new-equip-name');
  const refineSelect = document.getElementById('new-equip-refine');
  const levelInput = document.getElementById('new-equip-level');
  const zenyInput = document.getElementById('new-equip-zeny');
  if (!nameInput) return;

  const itemName = nameInput.value.trim();
  if (!itemName) {
    alert('Please enter or select an equipment item name.');
    return;
  }

  const refine = refineSelect ? (parseInt(refineSelect.value, 10) || 0) : 0;
  const minLvl = levelInput ? (parseInt(levelInput.value, 10) || 1) : 1;
  const minZ = zenyInput ? (parseInt(zenyInput.value, 10) || 0) : 0;

  if (!Array.isArray(currentConfigData.EquipmentTargets)) {
    currentConfigData.EquipmentTargets = [];
  }

  currentConfigData.EquipmentTargets.push({
    ItemName: itemName,
    TargetRefineLevel: refine,
    MinLevel: minLvl,
    MinZeny: minZ
  });

  syncJsonIfVisible();
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
