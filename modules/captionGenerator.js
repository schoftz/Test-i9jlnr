/**
 * Caption Generator Module
 * Generates synchronized captions from word-level transcripts
 */

class CaptionGenerator {
  constructor() {
    this.maxLineLength = 10; // default words per line
    this.captions = [];
  }

  /**
   * Generate captions from word-level transcript
   */
  generateCaptions(words, options = {}) {
    this.maxLineLength = options.maxLineLength || 10;
    
    if (!words || words.length === 0) {
      return { success: false, error: 'No words provided' };
    }

    try {
      const captions = [];
      let currentCaption = {
        words: [],
        text: '',
        startTime: 0,
        endTime: 0
      };

      words.forEach((word, index) => {
        // Add word to current caption
        currentCaption.words.push(word);
        currentCaption.text = currentCaption.words.map(w => w.text).join(' ');

        // Set start time from first word
        if (currentCaption.words.length === 1) {
          currentCaption.startTime = word.start;
        }

        // Update end time
        currentCaption.endTime = word.end;

        // Check if we should break to new caption
        const shouldBreak = 
          currentCaption.words.length >= this.maxLineLength ||
          (index === words.length - 1); // last word

        if (shouldBreak) {
          captions.push({ ...currentCaption });
          currentCaption = {
            words: [],
            text: '',
            startTime: 0,
            endTime: 0
          };
        }
      });

      this.captions = captions;

      return {
        success: true,
        count: captions.length,
        captions: captions
      };
    } catch (e) {
      return {
        success: false,
        error: e.message
      };
    }
  }

  /**
   * Generate captions by sentence/phrase
   */
  generateByPhrases(transcript, words, options = {}) {
    try {
      // Split by common punctuation
      const sentences = transcript.split(/(?<=[.!?])\s+/);
      const captions = [];

      let wordIndex = 0;

      sentences.forEach(sentence => {
        const sentenceWords = sentence.trim().split(/\s+/);
        const captionWords = [];

        // Collect words for this sentence
        for (let i = 0; i < sentenceWords.length && wordIndex < words.length; i++) {
          if (words[wordIndex]) {
            captionWords.push(words[wordIndex]);
            wordIndex++;
          }
        }

        // Break sentence into subtitle-sized chunks
        const chunks = this.breakIntoChunks(captionWords, options.maxLineLength || 5);
        
        chunks.forEach(chunk => {
          if (chunk.length > 0) {
            captions.push({
              words: chunk,
              text: chunk.map(w => w.text).join(' '),
              startTime: chunk[0].start,
              endTime: chunk[chunk.length - 1].end
            });
          }
        });
      });

      this.captions = captions;

      return {
        success: true,
        count: captions.length,
        captions: captions
      };
    } catch (e) {
      return {
        success: false,
        error: e.message
      };
    }
  }

  /**
   * Break words into chunks
   */
  breakIntoChunks(words, maxWordsPerChunk = 5) {
    const chunks = [];
    let currentChunk = [];

    words.forEach(word => {
      currentChunk.push(word);

      if (currentChunk.length >= maxWordsPerChunk) {
        chunks.push([...currentChunk]);
        currentChunk = [];
      }
    });

    if (currentChunk.length > 0) {
      chunks.push(currentChunk);
    }

    return chunks;
  }

  /**
   * Add highlight info to captions
   */
  addHighlights(captions, options = {}) {
    return captions.map((caption, index) => ({
      ...caption,
      highlight: {
        enabled: options.highlightEnabled !== false,
        currentWordIndex: 0,
        color: options.highlightColor || '#FFFF00'
      }
    }));
  }

  /**
   * Apply styling to captions
   */
  applyStyling(captions, styling = {}) {
    return captions.map(caption => ({
      ...caption,
      style: {
        fontSize: styling.fontSize || 24,
        fontColor: styling.fontColor || '#FFFFFF',
        bgColor: styling.bgColor || '#000000',
        opacity: styling.opacity || 100,
        animation: styling.animation || 'fade',
        strokeWidth: styling.strokeWidth || 2,
        shadowOffset: styling.shadowOffset || 2,
        shadowColor: styling.shadowColor || 'rgba(0,0,0,0.8)'
      }
    }));
  }

  /**
   * Get captions
   */
  getCaptions() {
    return this.captions;
  }

  /**
   * Calculate optimal line length based on screen width
   */
  calculateOptimalLineLength(screenWidth, fontSize = 24) {
    // Approximate: each character is about 0.6 * fontSize pixels
    const charWidth = fontSize * 0.6;
    // Leave 10% margin on each side
    const availableWidth = screenWidth * 0.8;
    
    // Estimate average word width (5 characters + 1 space)
    const avgWordWidth = (5 + 1) * charWidth;
    
    const maxWords = Math.floor(availableWidth / avgWordWidth);
    
    // Return between 5 and 15 words
    return Math.max(5, Math.min(15, maxWords));
  }

  /**
   * Generate VTT format (for export)
   */
  generateVTT(captions) {
    let vtt = 'WEBVTT\n\n';

    captions.forEach(caption => {
      const startTime = this.secondsToVTTTime(caption.startTime);
      const endTime = this.secondsToVTTTime(caption.endTime);
      vtt += `${startTime} --> ${endTime}\n${caption.text}\n\n`;
    });

    return vtt;
  }

  /**
   * Generate SRT format (for export)
   */
  generateSRT(captions) {
    let srt = '';

    captions.forEach((caption, index) => {
      const startTime = this.secondsToSRTTime(caption.startTime);
      const endTime = this.secondsToSRTTime(caption.endTime);
      srt += `${index + 1}\n${startTime} --> ${endTime}\n${caption.text}\n\n`;
    });

    return srt;
  }

  /**
   * Convert seconds to VTT timecode
   */
  secondsToVTTTime(seconds) {
    const hours = Math.floor(seconds / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    const secs = Math.floor(seconds % 60);
    const ms = Math.floor((seconds % 1) * 1000);

    return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(secs).padStart(2, '0')}.${String(ms).padStart(3, '0')}`;
  }

  /**
   * Convert seconds to SRT timecode
   */
  secondsToSRTTime(seconds) {
    const hours = Math.floor(seconds / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    const secs = Math.floor(seconds % 60);
    const ms = Math.floor((seconds % 1) * 1000);

    return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(secs).padStart(2, '0')},${String(ms).padStart(3, '0')}`;
  }

  /**
   * Get caption statistics
   */
  getStatistics(captions = null) {
    const caps = captions || this.captions;
    
    if (caps.length === 0) {
      return {
        totalCaptions: 0,
        totalDuration: 0,
        avgWordsPerCaption: 0,
        avgDurationPerCaption: 0
      };
    }

    const totalWords = caps.reduce((sum, cap) => sum + cap.words.length, 0);
    const totalDuration = caps[caps.length - 1].endTime - caps[0].startTime;

    return {
      totalCaptions: caps.length,
      totalWords: totalWords,
      totalDuration: totalDuration,
      avgWordsPerCaption: (totalWords / caps.length).toFixed(1),
      avgDurationPerCaption: (totalDuration / caps.length).toFixed(2)
    };
  }

  /**
   * Clear all captions
   */
  clear() {
    this.captions = [];
  }
}

if (typeof module !== 'undefined' && module.exports) {
  module.exports = CaptionGenerator;
}
