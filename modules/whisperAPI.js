/**
 * Whisper API Module
 * Handles speech-to-text transcription using OpenAI's Whisper API
 */

class WhisperAPI {
  constructor(apiKey = '') {
    this.apiKey = apiKey;
    this.apiEndpoint = 'https://api.openai.com/v1/audio/transcriptions';
    this.model = 'whisper-1';
  }

  /**
   * Set API key
   */
  setApiKey(apiKey) {
    this.apiKey = apiKey;
  }

  /**
   * Test API connectivity
   */
  async testConnection() {
    try {
      if (!this.apiKey) {
        return { success: false, error: 'API key not set' };
      }

      // Test with a simple request
      const response = await fetch(this.apiEndpoint, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${this.apiKey}`
        },
        body: new FormData() // Empty form to test auth
      });

      if (response.status === 400 || response.status === 401) {
        return { success: false, error: 'Invalid API key' };
      }

      return { success: true, message: 'API connection successful' };
    } catch (e) {
      return { success: false, error: e.message };
    }
  }

  /**
   * Transcribe audio file
   */
  async transcribeAudio(audioFile, language = 'en') {
    try {
      if (!this.apiKey) {
        throw new Error('API key not set');
      }

      if (!audioFile) {
        throw new Error('No audio file provided');
      }

      const formData = new FormData();
      formData.append('file', audioFile);
      formData.append('model', this.model);
      formData.append('language', this.getLanguageCode(language));

      const response = await fetch(this.apiEndpoint, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${this.apiKey}`
        },
        body: formData
      });

      if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error?.message || `API error: ${response.status}`);
      }

      const result = await response.json();
      
      return {
        success: true,
        text: result.text,
        language: language
      };
    } catch (e) {
      return {
        success: false,
        error: e.message
      };
    }
  }

  /**
   * Transcribe with word-level timestamps
   * Note: Requires verbose_json parameter in actual API
   */
  async transcribeWithTimestamps(audioFile, language = 'en') {
    try {
      if (!this.apiKey) {
        throw new Error('API key not set');
      }

      if (!audioFile) {
        throw new Error('No audio file provided');
      }

      const formData = new FormData();
      formData.append('file', audioFile);
      formData.append('model', this.model);
      formData.append('language', this.getLanguageCode(language));
      formData.append('response_format', 'verbose_json');
      formData.append('timestamp_granularities', ['segment', 'word']);

      const response = await fetch(this.apiEndpoint, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${this.apiKey}`
        },
        body: formData
      });

      if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error?.message || `API error: ${response.status}`);
      }

      const result = await response.json();

      // Parse word-level data
      const words = [];
      if (result.segments && Array.isArray(result.segments)) {
        result.segments.forEach(segment => {
          if (segment.words && Array.isArray(segment.words)) {
            segment.words.forEach(word => {
              words.push({
                text: word.word?.trim() || '',
                start: word.start || 0,
                end: word.end || 0
              });
            });
          }
        });
      }

      return {
        success: true,
        text: result.text || '',
        words: words,
        language: language
      };
    } catch (e) {
      return {
        success: false,
        error: e.message,
        words: []
      };
    }
  }

  /**
   * Convert language name to code
   */
  getLanguageCode(language) {
    const codes = {
      'tr': 'tr',
      'türkçe': 'tr',
      'english': 'en',
      'en': 'en',
      'español': 'es',
      'es': 'es',
      'français': 'fr',
      'fr': 'fr',
      'deutsch': 'de',
      'de': 'de'
    };
    return codes[language.toLowerCase()] || 'en';
  }

  /**
   * Parse transcription into words with timestamps
   */
  parseTranscript(transcript) {
    if (!transcript) return [];
    
    // Simple word parsing - assumes transcript is already timestamped
    const words = transcript.split(/\s+/);
    return words.map((word, index) => ({
      text: word,
      index: index,
      start: index * 0.5, // Approximate timing
      end: (index + 1) * 0.5
    }));
  }

  /**
   * Validate audio file format
   */
  validateAudioFile(file) {
    const supportedFormats = ['audio/wav', 'audio/mp3', 'audio/mpeg', 'audio/ogg', 'audio/flac'];
    
    if (!file) return false;
    
    // Check by MIME type
    if (supportedFormats.includes(file.type)) {
      return true;
    }

    // Check by filename extension
    const filename = file.name || '';
    const extension = filename.split('.').pop().toLowerCase();
    const supportedExtensions = ['wav', 'mp3', 'ogg', 'flac', 'm4a'];
    
    return supportedExtensions.includes(extension);
  }

  /**
   * Get API usage limits info
   */
  getApiInfo() {
    return {
      model: this.model,
      endpoint: this.apiEndpoint,
      supportedLanguages: [
        'English', 'Türkçe', 'Español', 'Français', 'Deutsch',
        'Português', 'Italiano', 'русский', '日本語', '中文'
      ],
      maxFileSize: '25MB',
      supportedFormats: ['.wav', '.mp3', '.ogg', '.flac', '.m4a']
    };
  }
}

if (typeof module !== 'undefined' && module.exports) {
  module.exports = WhisperAPI;
}
