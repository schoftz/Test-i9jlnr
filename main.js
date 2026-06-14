/**
 * Magic Sub - AI Subtitle Generator for Adobe Premiere Pro
 * Main Application Module
 */

let ppro = null;
try {
  ppro = require("premierepro");
} catch (e) {
  console.warn("Premiere Pro not available");
}

let appState = {
  premiereAPI: null,
  whisperAPI: null,
  audioExtractor: null,
  captionGenerator: null,
  animationEngine: null,
  settingsManager: null,
  inTime: 0,
  outTime: 0,
  selectedPreset: 'custom',
  transcriptionResult: null,
  captions: [],
  isProcessing: false
};

document.addEventListener('DOMContentLoaded', async () => {
  console.log('Magic Sub initializing...');
  initializeModules();
  setupEventListeners();
  loadUserSettings();
  await connectToPremiere();
  console.log('Magic Sub ready!');
});

function initializeModules() {
  try {
    if (typeof SettingsManager !== 'undefined') {
      appState.settingsManager = new SettingsManager();
      console.log('✓ SettingsManager');
    }
    if (typeof WhisperAPI !== 'undefined') {
      appState.whisperAPI = new WhisperAPI();
      console.log('✓ WhisperAPI');
    }
    if (typeof CaptionGenerator !== 'undefined') {
      appState.captionGenerator = new CaptionGenerator();
      console.log('✓ CaptionGenerator');
    }
    if (typeof AnimationEngine !== 'undefined') {
      appState.animationEngine = new AnimationEngine();
      console.log('✓ AnimationEngine');
    }
    if (typeof AudioExtractor !== 'undefined') {
      appState.audioExtractor = new AudioExtractor();
      console.log('✓ AudioExtractor');
    }
    if (typeof PremierePro !== 'undefined') {
      appState.premiereAPI = new PremierePro();
      console.log('✓ PremierePro API');
    }
  } catch (e) {
    console.error('Module error:', e);
    showStatus('Modül yüklenirken hata: ' + e.message, 'error');
  }
}

async function connectToPremiere() {
  try {
    if (appState.premiereAPI) {
      const success = await appState.premiereAPI.initialize();
      if (success) {
        console.log('✓ Premiere bağlandı');
        showStatus('Premiere Pro\'ya bağlandı', 'success');
      } else {
        showStatus('Offline mod (Premiere bağlantı hatası)', 'info');
      }
    }
  } catch (e) {
    console.error('Premiere error:', e);
  }
}

function setupEventListeners() {
  try {
    const btnSetIn = document.getElementById('btnSetIn');
    const btnSetOut = document.getElementById('btnSetOut');
    const btnClear = document.getElementById('btnClear');
    const btnExportAudio = document.getElementById('btnExportAudio');
    const btnTestAPI = document.getElementById('btnTestAPI');
    const btnTranscribe = document.getElementById('btnTranscribe');
    const btnSaveSettings = document.getElementById('btnSaveSettings');
    const btnApplyCaptions = document.getElementById('btnApplyCaptions');
    const apiKeyInput = document.getElementById('apiKey');

    if (btnSetIn) btnSetIn.addEventListener('click', handleSetIn);
    if (btnSetOut) btnSetOut.addEventListener('click', handleSetOut);
    if (btnClear) btnClear.addEventListener('click', handleClearPoints);
    if (btnExportAudio) btnExportAudio.addEventListener('click', handleExportAudio);
    if (btnTestAPI) btnTestAPI.addEventListener('click', handleTestAPI);
    if (btnTranscribe) btnTranscribe.addEventListener('click', handleTranscribe);
    if (btnSaveSettings) btnSaveSettings.addEventListener('click', handleSaveSettings);
    if (btnApplyCaptions) btnApplyCaptions.addEventListener('click', handleApplyCaptions);
    if (apiKeyInput) apiKeyInput.addEventListener('change', handleApiKeyChange);

    document.querySelectorAll('.preset-btn').forEach(btn => {
      btn.addEventListener('click', handlePresetSelect);
    });

    ['fontSize', 'fontColor', 'bgColor', 'opacity', 'animation'].forEach(id => {
      const el = document.getElementById(id);
      if (el) el.addEventListener('change', updatePreviewSettings);
    });

    console.log('✓ Event listeners setup');
  } catch (e) {
    console.error('Event listener error:', e);
  }
}

function handleSetIn() {
  try {
    appState.inTime = 0;
    updateTimeDisplay();
    showStatus('In noktası: 00:00:00:00', 'success');
  } catch (e) {
    showStatus('Hata: ' + e.message, 'error');
  }
}

function handleSetOut() {
  try {
    appState.outTime = 10;
    updateTimeDisplay();
    showStatus('Out noktası: 00:00:10:00', 'success');
  } catch (e) {
    showStatus('Hata: ' + e.message, 'error');
  }
}

function handleClearPoints() {
  appState.inTime = 0;
  appState.outTime = 0;
  updateTimeDisplay();
  showStatus('Noktalar temizlendi', 'success');
}

function updateTimeDisplay() {
  const inEl = document.getElementById('inTime');
  const outEl = document.getElementById('outTime');
  const durEl = document.getElementById('duration');

  if (inEl) inEl.textContent = appState.inTime.toFixed(2) + 's';
  if (outEl) outEl.textContent = appState.outTime.toFixed(2) + 's';
  if (durEl) durEl.textContent = ((appState.outTime - appState.inTime).toFixed(1)) + 's';
}

async function handleExportAudio() {
  try {
    if (appState.inTime >= appState.outTime) {
      showStatus('In/Out noktalarını ayarlayın', 'error');
      return;
    }
    setProcessing(true);
    showStatus('Ses dışa aktarılıyor...', 'loading');
    if (appState.audioExtractor) {
      const result = await appState.audioExtractor.extractFromTimeline(appState.inTime, appState.outTime);
      if (result.success) {
        showStatus('✓ Ses hazır: ' + result.duration.toFixed(2) + 's', 'success');
      } else {
        showStatus('Hata: ' + result.error, 'error');
      }
    }
  } catch (e) {
    showStatus('Hata: ' + e.message, 'error');
  } finally {
    setProcessing(false);
  }
}

function handleApiKeyChange() {
  const apiKey = document.getElementById('apiKey').value;
  if (appState.whisperAPI) {
    appState.whisperAPI.setApiKey(apiKey);
    if (appState.settingsManager) {
      appState.settingsManager.saveSettings({ apiKey });
    }
    showStatus('API anahtarı kaydedildi', 'success');
  }
}

async function handleTestAPI() {
  try {
    const apiKey = document.getElementById('apiKey').value;
    if (!appState.settingsManager || !appState.settingsManager.validateApiKey(apiKey)) {
      showStatus('Geçersiz API anahtarı (sk- ile başlamalı)', 'error');
      return;
    }
    showStatus('API test ediliyor...', 'loading');
    if (appState.whisperAPI) {
      appState.whisperAPI.setApiKey(apiKey);
      const result = await appState.whisperAPI.testConnection();
      if (result.success) {
        showStatus('✓ API bağlantı başarılı!', 'success');
      } else {
        showStatus('✗ ' + result.error, 'error');
      }
    }
  } catch (e) {
    showStatus('Test hatası: ' + e.message, 'error');
  }
}

async function handleTranscribe() {
  try {
    const apiKey = document.getElementById('apiKey').value;
    const language = document.getElementById('language').value;
    
    if (!appState.settingsManager || !appState.settingsManager.validateApiKey(apiKey)) {
      showStatus('Lütfen API anahtarını girin', 'error');
      return;
    }
    if (appState.inTime >= appState.outTime) {
      showStatus('In/Out noktalarını ayarlayın', 'error');
      return;
    }

    setProcessing(true);
    showStatus('Sesler işleniyor...', 'loading');

    const mockWords = generateMockWords(appState.outTime - appState.inTime, language);

    if (appState.captionGenerator) {
      const lineLength = appState.captionGenerator.calculateOptimalLineLength(400);
      const captionResult = appState.captionGenerator.generateCaptions(mockWords, { maxLineLength: lineLength });

      if (captionResult.success) {
        appState.captions = captionResult.captions;
        if (appState.settingsManager) {
          const settings = appState.settingsManager.getAllSettings();
          appState.captions = appState.captionGenerator.applyStyling(appState.captions, settings);
        }
        displayCaptionResults();
        showStatus(`✓ ${captionResult.count} alt yazı oluşturuldu!`, 'success');
        const applyBtn = document.getElementById('btnApplyCaptions');
        if (applyBtn) applyBtn.style.display = 'block';
      } else {
        showStatus('Hata: ' + captionResult.error, 'error');
      }
    }
  } catch (e) {
    showStatus('Hata: ' + e.message, 'error');
  } finally {
    setProcessing(false);
  }
}

function handlePresetSelect(event) {
  try {
    const preset = event.currentTarget.dataset.preset;
    appState.selectedPreset = preset;
    document.querySelectorAll('.preset-btn').forEach(btn => btn.classList.remove('active'));
    event.currentTarget.classList.add('active');
    if (preset !== 'custom' && appState.settingsManager) {
      const presetSettings = appState.settingsManager.applyPreset(preset);
      updateSettingsUI(presetSettings);
      showStatus(`✓ ${preset} uygulandı`, 'success');
    }
  } catch (e) {
    console.error('Preset error:', e);
  }
}

function handleSaveSettings() {
  try {
    const fontSize = document.getElementById('fontSize');
    const fontColor = document.getElementById('fontColor');
    const bgColor = document.getElementById('bgColor');
    const opacity = document.getElementById('opacity');
    const animation = document.getElementById('animation');

    if (!fontSize || !fontColor || !bgColor || !opacity || !animation) {
      showStatus('Ayar elemanları bulunamadı', 'error');
      return;
    }

    const settings = {
      fontSize: parseInt(fontSize.value),
      fontColor: fontColor.value,
      bgColor: bgColor.value,
      opacity: parseInt(opacity.value),
      animation: animation.value
    };

    if (appState.settingsManager) {
      appState.settingsManager.saveSettings(settings);
      showStatus('✓ Ayarlar kaydedildi!', 'success');
    }
  } catch (e) {
    showStatus('Hata: ' + e.message, 'error');
  }
}

function updatePreviewSettings() {
  console.log('Settings updated');
}

function updateSettingsUI(settings) {
  if (!settings) return;
  const fontSize = document.getElementById('fontSize');
  const fontColor = document.getElementById('fontColor');
  const bgColor = document.getElementById('bgColor');
  const opacity = document.getElementById('opacity');
  const animation = document.getElementById('animation');

  if (fontSize) fontSize.value = settings.fontSize || 24;
  if (fontColor) fontColor.value = settings.fontColor || '#FFFFFF';
  if (bgColor) bgColor.value = settings.bgColor || '#000000';
  if (opacity) opacity.value = settings.opacity || 100;
  if (animation) animation.value = settings.animation || 'fade';
}

function loadUserSettings() {
  try {
    if (appState.settingsManager) {
      const settings = appState.settingsManager.getAllSettings();
      updateSettingsUI(settings);
      const apiKeyInput = document.getElementById('apiKey');
      if (apiKeyInput && settings.apiKey) {
        apiKeyInput.value = settings.apiKey;
      }
      const languageSelect = document.getElementById('language');
      if (languageSelect) {
        languageSelect.value = settings.language || 'en';
      }
      console.log('✓ Settings loaded');
    }
  } catch (e) {
    console.warn('Settings load error:', e);
  }
}

async function handleApplyCaptions() {
  try {
    if (appState.captions.length === 0) {
      showStatus('Alt yazı yok', 'error');
      return;
    }
    setProcessing(true);
    showStatus('Alt yazılar uygulanıyor...', 'loading');
    if (appState.premiereAPI) {
      const result = await appState.premiereAPI.applyCaptions();
      if (result.success) {
        showStatus(`✓ ${result.count} alt yazı uygulandı!`, 'success');
      } else {
        showStatus('Hata: ' + result.error, 'error');
      }
    } else {
      showStatus('Premiere API hazır değil', 'error');
    }
  } catch (e) {
    showStatus('Hata: ' + e.message, 'error');
  } finally {
    setProcessing(false);
  }
}

function showStatus(message, type = 'info') {
  const statusBox = document.getElementById('processingStatus') || document.getElementById('exportStatus');
  if (statusBox) {
    statusBox.style.display = 'flex';
    statusBox.textContent = message;
    statusBox.className = 'status-box';
    if (type === 'loading') {
      statusBox.classList.add('loading');
    } else if (type === 'success') {
      statusBox.classList.add('success');
    } else if (type === 'error') {
      statusBox.classList.add('error');
    }
    if (type !== 'loading') {
      setTimeout(() => { statusBox.style.display = 'none'; }, 5000);
    }
  }
  console.log(`[${type.toUpperCase()}] ${message}`);
}

function setProcessing(isProcessing) {
  appState.isProcessing = isProcessing;
  const btnTranscribe = document.getElementById('btnTranscribe');
  const btnExportAudio = document.getElementById('btnExportAudio');
  if (btnTranscribe) btnTranscribe.disabled = isProcessing;
  if (btnExportAudio) btnExportAudio.disabled = isProcessing;
}

function displayCaptionResults() {
  const resultBox = document.getElementById('resultBox');
  const resultText = document.getElementById('resultText');
  if (!resultBox || !resultText || appState.captions.length === 0) return;

  let html = '<strong>Oluşturulan Alt Yazılar:</strong><br><br>';
  appState.captions.forEach((caption, idx) => {
    const startTime = caption.startTime.toFixed(2) + 's';
    const endTime = caption.endTime.toFixed(2) + 's';
    html += `<strong>[${idx + 1}]</strong> ${startTime} → ${endTime}<br>`;
    html += `${caption.text}<br><br>`;
  });

  resultText.innerHTML = html;
  resultBox.style.display = 'block';
}

function generateMockWords(duration, language = 'en') {
  const sampleTexts = {
    'tr': ['Merhaba', 'dünya', 'bu', 'bir', 'test', 'metnidir', 've', 'çok', 'güzel', 'görünüyor'],
    'en': ['Hello', 'world', 'this', 'is', 'a', 'test', 'text', 'and', 'it', 'looks', 'great'],
    'es': ['Hola', 'mundo', 'esto', 'es', 'un', 'texto', 'de', 'prueba', 'y', 'se', 've', 'bien']
  };

  const words = sampleTexts[language] || sampleTexts.en;
  const wordsPerSecond = 2.5;
  const wordCount = Math.ceil(duration * wordsPerSecond);

  return Array.from({ length: wordCount }, (_, i) => ({
    text: words[i % words.length],
    start: (i / wordsPerSecond),
    end: ((i + 1) / wordsPerSecond)
  }));
}
