/**
 * Animation Engine Module
 * Generates animation keyframes and effects for captions
 */

class AnimationEngine {
  constructor() {
    this.animations = {
      fade: this.createFadeAnimation,
      slide: this.createSlideAnimation,
      pop: this.createPopAnimation,
      bounce: this.createBounceAnimation,
      typewriter: this.createTypewriterAnimation
    };
  }

  /**
   * Generate animation keyframes for a caption
   */
  getAnimationKeyframes(animationType = 'fade', caption) {
    const animationFn = this.animations[animationType];

    if (animationFn) {
      return animationFn.call(this, caption);
    }

    return { in: [], out: [] };
  }

  /**
   * Fade animation
   */
  createFadeAnimation(caption) {
    const duration = 0.3; // seconds
    const startTime = caption.startTime - duration;
    const endTime = caption.endTime + duration;

    return {
      in: [
        { time: startTime, opacity: 0 },
        { time: caption.startTime, opacity: 100 }
      ],
      out: [
        { time: caption.endTime, opacity: 100 },
        { time: endTime, opacity: 0 }
      ],
      duration: duration,
      easing: 'easeInOutQuad'
    };
  }

  /**
   * Slide animation
   */
  createSlideAnimation(caption) {
    const duration = 0.4;
    const startTime = caption.startTime - duration;
    const endTime = caption.endTime + duration;

    return {
      in: [
        { time: startTime, x: 100, opacity: 0 },
        { time: caption.startTime, x: 0, opacity: 100 }
      ],
      out: [
        { time: caption.endTime, x: 0, opacity: 100 },
        { time: endTime, x: -100, opacity: 0 }
      ],
      duration: duration,
      easing: 'easeOutCubic',
      direction: 'horizontal'
    };
  }

  /**
   * Pop/Scale animation
   */
  createPopAnimation(caption) {
    const duration = 0.3;
    const startTime = caption.startTime - duration;
    const endTime = caption.endTime + duration;

    return {
      in: [
        { time: startTime, scale: 0, opacity: 0 },
        { time: startTime + duration * 0.7, scale: 1.1, opacity: 100 },
        { time: caption.startTime, scale: 1, opacity: 100 }
      ],
      out: [
        { time: caption.endTime, scale: 1, opacity: 100 },
        { time: endTime - duration * 0.3, scale: 0.9, opacity: 0 },
        { time: endTime, scale: 0, opacity: 0 }
      ],
      duration: duration,
      easing: 'easeOutBack'
    };
  }

  /**
   * Bounce animation
   */
  createBounceAnimation(caption) {
    const duration = 0.5;
    const startTime = caption.startTime - duration;
    const endTime = caption.endTime + duration;

    return {
      in: [
        { time: startTime, y: 50, opacity: 0 },
        { time: startTime + duration * 0.3, y: -10 },
        { time: startTime + duration * 0.6, y: 5 },
        { time: caption.startTime, y: 0, opacity: 100 }
      ],
      out: [
        { time: caption.endTime, y: 0, opacity: 100 },
        { time: endTime - duration * 0.3, y: -50, opacity: 0 },
        { time: endTime, y: -100, opacity: 0 }
      ],
      duration: duration,
      easing: 'easeOutBounce'
    };
  }

  /**
   * Typewriter animation
   */
  createTypewriterAnimation(caption) {
    const wordCount = caption.words ? caption.words.length : 1;
    const duration = Math.min(0.8, wordCount * 0.2); // 0.2s per word
    const startTime = caption.startTime;
    const endTime = caption.endTime;

    // Simulate character reveal
    const charCount = caption.text.length;

    return {
      in: [
        { time: startTime, charRevealed: 0, opacity: 100 }
      ],
      out: [
        { time: endTime, charRevealed: charCount, opacity: 100 }
      ],
      duration: duration,
      easing: 'linear',
      type: 'typewriter',
      charCount: charCount
    };
  }

  /**
   * Generate CSS animation code
   */
  generateCSS(captions, animationType = 'fade') {
    let css = '';

    captions.forEach((caption, index) => {
      const keyframes = this.getAnimationKeyframes(animationType, caption);
      const animationName = `caption-${index}`;

      css += `@keyframes ${animationName} {\n`;
      css += `  0% { opacity: ${keyframes.in[0].opacity || 0}%; }\n`;
      css += `  100% { opacity: ${keyframes.out[keyframes.out.length - 1].opacity || 0}%; }\n`;
      css += `}\n\n`;

      css += `.caption-${index} {\n`;
      css += `  animation: ${animationName} ${caption.endTime - caption.startTime}s linear;\n`;
      css += `  animation-delay: ${caption.startTime}s;\n`;
      css += `}\n\n`;
    });

    return css;
  }

  /**
   * Generate Premiere Pro expression (for future implementation)
   */
  generatePremierExpression(keyframes) {
    // This would generate Premiere Pro's expression language
    let expression = 'linear = (time, v1Start, v1End, v2Start, v2End) => {\\n';
    expression += '  return v2Start + (time - v1Start) / (v1End - v1Start) * (v2End - v2Start);\\n';
    expression += '};\\n\\n';

    // Add keyframes
    let firstTime = keyframes.in[0].time || 0;
    keyframes.in.forEach((kf, i) => {
      if (i === 0) {
        expression += `ease(time, ${firstTime}, ${keyframes.in[keyframes.in.length - 1].time}, 0, 100)\\n`;
      }
    });

    return expression;
  }

  /**
   * Get available animations
   */
  getAvailableAnimations() {
    return Object.keys(this.animations);
  }

  /**
   * Apply word-by-word highlighting animation
   */
  generateWordHighlightAnimation(caption, highlightColor = '#FFFF00') {
    const words = caption.words || [];
    
    return words.map((word, index) => ({
      wordIndex: index,
      text: word.text,
      startTime: word.start,
      endTime: word.end,
      highlight: {
        color: highlightColor,
        inEffect: 'in' // which animation to highlight
      }
    }));
  }

  /**
   * Create entrance animation sequence
   */
  generateEntranceSequence(captions, spacing = 0.1) {
    return captions.map((caption, index) => ({
      ...caption,
      enteranceDelay: index * spacing,
      enteranceAnimation: 'fade'
    }));
  }

  /**
   * Create exit animation sequence
   */
  generateExitSequence(captions, direction = 'out') {
    return captions.map((caption, index) => ({
      ...caption,
      exitAnimation: direction === 'out' ? 'fade' : 'slide',
      exitDelay: 0
    }));
  }

  /**
   * Validate animation compatibility with Premiere
   */
  validateForPremiere(animations) {
    // Check if animations are compatible with Premiere Pro
    const supportedProps = ['opacity', 'position', 'scale', 'rotation'];
    
    let isValid = true;
    let issues = [];

    Object.keys(animations).forEach(key => {
      if (!supportedProps.includes(key)) {
        isValid = false;
        issues.push(`Property '${key}' may not be supported in Premiere Pro`);
      }
    });

    return {
      isValid: isValid,
      issues: issues,
      recommendation: isValid ? 'Ready for Premiere' : 'Some properties may need adjustment'
    };
  }

  /**
   * Get animation preview data
   */
  getAnimationPreview(animationType, duration = 2) {
    const testCaption = {
      text: 'Sample Text',
      words: [{ text: 'Sample', start: 0.5, end: 1 }, { text: 'Text', start: 1, end: 1.5 }],
      startTime: 0.5,
      endTime: 1.5
    };

    return this.getAnimationKeyframes(animationType, testCaption);
  }
}

if (typeof module !== 'undefined' && module.exports) {
  module.exports = AnimationEngine;
}
