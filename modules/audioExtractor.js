/**
 * Audio Extractor Module
 * Exports audio from Premiere Pro timeline as WAV
 */

class AudioExtractor {
  constructor(premiereAPI = null) {
    this.premiereAPI = premiereAPI;
    this.audioContext = null;
    this.audioBuffer = null;
  }

  /**
   * Extract audio from timeline between In and Out points
   */
  async extractFromTimeline(inTime, outTime) {
    try {
      if (!this.premiereAPI) {
        return {
          success: false,
          error: 'Premiere API not initialized'
        };
      }

      // In a real implementation, this would use Premiere's export capabilities
      // For now, return a placeholder

      return {
        success: true,
        message: 'Audio extraction prepared',
        duration: outTime - inTime,
        inTime: inTime,
        outTime: outTime
      };
    } catch (e) {
      return {
        success: false,
        error: e.message
      };
    }
  }

  /**
   * Export audio to WAV file
   */
  async exportToWAV(audioData, filename = 'export.wav') {
    try {
      if (!audioData) {
        throw new Error('No audio data provided');
      }

      // Create WAV file
      const wavBlob = this.createWAVBlob(audioData);

      // Create download link
      const url = URL.createObjectURL(wavBlob);
      const link = document.createElement('a');
      link.href = url;
      link.download = filename;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      URL.revokeObjectURL(url);

      return {
        success: true,
        filename: filename,
        size: wavBlob.size
      };
    } catch (e) {
      return {
        success: false,
        error: e.message
      };
    }
  }

  /**
   * Create WAV file blob from audio data
   */
  createWAVBlob(audioData, sampleRate = 48000, channels = 2) {
    try {
      // Determine audio data type
      let audioArray;
      
      if (audioData instanceof Blob) {
        return audioData; // Already a blob
      } else if (audioData instanceof ArrayBuffer) {
        audioArray = new Uint8Array(audioData);
      } else if (Array.isArray(audioData)) {
        audioArray = new Uint8Array(audioData);
      } else {
        throw new Error('Unsupported audio data format');
      }

      // Create WAV header
      const header = this.createWAVHeader(
        audioArray.length,
        channels,
        sampleRate
      );

      // Combine header and audio data
      const wavData = new Uint8Array(header.length + audioArray.length);
      wavData.set(header);
      wavData.set(audioArray, header.length);

      return new Blob([wavData], { type: 'audio/wav' });
    } catch (e) {
      console.error('Error creating WAV blob:', e);
      return null;
    }
  }

  /**
   * Create WAV file header
   */
  createWAVHeader(audioLength, channels = 2, sampleRate = 48000) {
    const header = new ArrayBuffer(44);
    const view = new DataView(header);

    // WAV header structure
    const byteRate = sampleRate * channels * 2; // 16-bit
    const blockAlign = channels * 2;

    // RIFF chunk
    this.writeString(view, 0, 'RIFF');
    view.setUint32(4, 36 + audioLength, true);
    this.writeString(view, 8, 'WAVE');

    // fmt sub-chunk
    this.writeString(view, 12, 'fmt ');
    view.setUint32(16, 16, true); // Subchunk1Size
    view.setUint16(20, 1, true);  // AudioFormat (1 = PCM)
    view.setUint16(22, channels, true);
    view.setUint32(24, sampleRate, true);
    view.setUint32(28, byteRate, true);
    view.setUint16(32, blockAlign, true);
    view.setUint16(34, 16, true); // BitsPerSample

    // data sub-chunk
    this.writeString(view, 36, 'data');
    view.setUint32(40, audioLength, true);

    return new Uint8Array(header);
  }

  /**
   * Write string to DataView
   */
  writeString(view, offset, string) {
    for (let i = 0; i < string.length; i++) {
      view.setUint8(offset + i, string.charCodeAt(i));
    }
  }

  /**
   * Record audio from browser (alternative method)
   */
  async recordBrowserAudio(duration = 30) {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      this.audioContext = new (window.AudioContext || window.webkitAudioContext)();

      const mediaRecorder = new MediaRecorder(stream);
      const audioChunks = [];

      return new Promise((resolve) => {
        mediaRecorder.ondataavailable = (event) => {
          audioChunks.push(event.data);
        };

        mediaRecorder.onstop = async () => {
          const audioBlob = new Blob(audioChunks, { type: 'audio/wav' });
          stream.getTracks().forEach(track => track.stop());

          resolve({
            success: true,
            blob: audioBlob,
            duration: duration
          });
        };

        mediaRecorder.start();
        setTimeout(() => mediaRecorder.stop(), duration * 1000);
      });
    } catch (e) {
      return {
        success: false,
        error: e.message
      };
    }
  }

  /**
   * Validate audio file
   */
  validateAudioFile(file) {
    const supportedFormats = ['audio/wav', 'audio/mpeg', 'audio/mp3', 'audio/ogg', 'audio/flac'];
    
    if (!file) return { valid: false, error: 'No file provided' };
    
    // Check MIME type
    if (supportedFormats.includes(file.type)) {
      return {
        valid: true,
        format: file.type,
        name: file.name,
        size: file.size
      };
    }

    // Check extension
    const extension = file.name.split('.').pop().toLowerCase();
    if (['wav', 'mp3', 'ogg', 'flac', 'm4a'].includes(extension)) {
      return {
        valid: true,
        format: extension,
        name: file.name,
        size: file.size
      };
    }

    return { valid: false, error: 'Unsupported audio format' };
  }

  /**
   * Get audio file info
   */
  async getAudioInfo(file) {
    try {
      const validation = this.validateAudioFile(file);
      if (!validation.valid) {
        return validation;
      }

      // Try to read audio duration
      const audioUrl = URL.createObjectURL(file);
      const audio = new Audio();
      
      return new Promise((resolve) => {
        audio.onloadedmetadata = () => {
          URL.revokeObjectURL(audioUrl);
          resolve({
            valid: true,
            format: validation.format,
            name: file.name,
            size: file.size,
            duration: audio.duration
          });
        };

        audio.onerror = () => {
          URL.revokeObjectURL(audioUrl);
          resolve(validation);
        };

        audio.src = audioUrl;
      });
    } catch (e) {
      return { valid: false, error: e.message };
    }
  }

  /**
   * Convert audio to different format (placeholder)
   */
  async convertAudioFormat(audioFile, targetFormat = 'wav') {
    // In a real app, would use FFmpeg.js or similar
    console.warn(`Audio conversion to ${targetFormat} requires backend processing`);
    
    return {
      success: false,
      message: 'Audio conversion requires backend processing',
      recommendation: 'Use Premiere\'s native export for format conversion'
    };
  }

  /**
   * Get supported export formats
   */
  getSupportedFormats() {
    return [
      {
        name: 'WAV',
        extension: '.wav',
        mimeType: 'audio/wav',
        quality: 'lossless',
        recommended: true
      },
      {
        name: 'MP3',
        extension: '.mp3',
        mimeType: 'audio/mpeg',
        quality: 'lossy',
        recommended: false
      },
      {
        name: 'AAC',
        extension: '.aac',
        mimeType: 'audio/aac',
        quality: 'lossy',
        recommended: false
      }
    ];
  }

  /**
   * Calculate file size
   */
  calculateFileSize(duration, bitRate = 320, format = 'wav') {
    // bitRate in kbps
    return (duration * bitRate * 1000) / 8; // bytes
  }
}

if (typeof module !== 'undefined' && module.exports) {
  module.exports = AudioExtractor;
}
