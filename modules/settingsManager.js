/**
 * Settings Manager Module
 * Manages user preferences and caption settings
 */

class SettingsManager {
  constructor() {
    this.storageKey = 'magicsub_settings';
    this.defaultSettings = {
      fontSize: 24,
      fontColor: '#FFFFFF',
      bgColor: '#000000',
      opacity: 100,
      animation: 'fade',
      preset: 'custom',
      apiKey: '',
      language: 'en',
      lineBreakLength: 10,
      highlightColor: '#FFFF00',
      strokeWidth: 2,
      shadowOffset: 2,
      safeAreaPercent: 10
    };
    this.settings = this.loadSettings();
  }

  loadSettings() {
    try {
      const stored = localStorage.getItem(this.storageKey);
      if (stored) {
        return { ...this.defaultSettings, ...JSON.parse(stored) };
      }
    } catch (e) {
      console.warn('Could not load settings from localStorage:', e);
    }
    return { ...this.defaultSettings };
  }

  saveSettings(newSettings) {
    this.settings = { ...this.settings, ...newSettings };
    try {
      localStorage.setItem(this.storageKey, JSON.stringify(this.settings));
      return true;
    } catch (e) {
      console.error('Could not save settings:', e);
      return false;
    }
  }

  getSetting(key) {
    return this.settings[key];
  }

  getAllSettings() {
    return { ...this.settings };
  }

  applyPreset(presetName) {
    const presets = {
      tiktok: {
        fontSize: 32,
        fontColor: '#FFFFFF',
        bgColor: '#000000',
        opacity: 85,
        animation: 'pop',
        lineBreakLength: 8,
        highlightColor: '#FF6B9D'
      },
      reels: {
        fontSize: 28,
        fontColor: '#FFFFFF',
        bgColor: 'rgba(0, 0, 0, 0.6)',
        opacity: 80,
        animation: 'slide',
        lineBreakLength: 9,
        highlightColor: '#00D4FF'
      },
      shorts: {
        fontSize: 24,
        fontColor: '#FFFFFF',
        bgColor: '#000000',
        opacity: 90,
        animation: 'fade',
        lineBreakLength: 10,
        highlightColor: '#FFD700'
      }
    };

    if (presets[presetName]) {
      this.saveSettings({ ...presets[presetName], preset: presetName });
      return presets[presetName];
    }
    return null;
  }

  validateApiKey(apiKey) {
    // Basic validation: should start with 'sk-' and have reasonable length
    return apiKey && apiKey.startsWith('sk-') && apiKey.length > 20;
  }
}

// Export for use in main.js
if (typeof module !== 'undefined' && module.exports) {
  module.exports = SettingsManager;
}
