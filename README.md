# 🎬 Magic Sub - AI Subtitle Generator for Premiere Pro

A powerful, modular Adobe Premiere Pro UXP plugin that automatically generates synchronized subtitles from video audio using OpenAI's Whisper API.

## ✨ Features

### Core Functionality
- **Timeline Integration**: Set In/Out points directly from Premiere Pro timeline
- **Audio Extraction**: Export audio segments as WAV format
- **AI Transcription**: Whisper API integration for accurate speech-to-text
- **Word-Level Timestamps**: Get precise timing for each word
- **Intelligent Caption Generation**: Automatically break text into readable subtitle lines
- **Smart Formatting**: Lines adapt to screen width and chosen preset

### Caption Styling & Effects
- **Multiple Presets**: TikTok, Reels, Shorts, Custom
- **Full Customization**: 
  - Font size, color, and opacity
  - Background styling
  - Stroke and shadow effects
  - Safe area margins
- **Animation Engines**:
  - Fade In/Out
  - Slide animations
  - Pop/Scale effects
  - Bounce animations
  - Typewriter effect
- **Active Word Highlighting**: Highlight currently spoken words in real-time

### Modular Architecture
```
modules/
├── premiereAPI.js          # Premiere Pro timeline & caption operations
├── audioExtractor.js       # Audio export and WAV generation
├── whisperAPI.js          # OpenAI Whisper API integration
├── captionGenerator.js    # Caption synchronization & formatting
├── animationEngine.js     # Animation keyframe generation
└── settingsManager.js     # User preferences persistence
```

## 🚀 Getting Started

### Installation

1. **Download the Plugin**
   - Clone or download this repository
   - Navigate to the plugin directory

2. **Load into Premiere Pro**
   - Open UXP Developer Tools (UDT)
   - Click "Add Plugin" and select `manifest.json`
   - Click "Load" to load the plugin
   - In Premiere Pro, go to Window > UXP Plugins > Magic Sub

3. **Configure Whisper API**
   - Get your OpenAI API key from [platform.openai.com](https://platform.openai.com)
   - Enter API key in the plugin settings
   - Click "Test API" to verify connection

### Basic Workflow

1. **Select Timeline Region**
   - Click "Set In Point" at your desired start
   - Click "Set Out Point" at your desired end
   - Duration is automatically calculated

2. **Export Audio**
   - Click "Export As WAV"
   - Audio is extracted from the In/Out region

3. **Transcribe**
   - Choose language (Turkish, English, Spanish, French, German)
   - Click "Transcribe & Generate"
   - Plugin sends audio to Whisper API

4. **Customize Captions**
   - Select a preset or customize manually
   - Adjust font, colors, animations
   - Configure safe area margins
   - Click "Save Settings"

5. **Apply to Timeline**
   - Click "Apply Captions to Track"
   - Captions are added to Premiere Pro caption track
   - Sync, animate, and refine in Premiere Pro

## 🔧 Configuration

### Settings
All settings are saved locally in browser storage and persist between sessions.

```javascript
{
  fontSize: 24,              // 12-72
  fontColor: '#FFFFFF',      // Hex color
  bgColor: '#000000',        // Hex or rgba
  opacity: 100,              // 0-100
  animation: 'fade',         // fade, slide, pop, bounce, typewriter
  preset: 'custom',          // tiktok, reels, shorts, custom
  language: 'en',            // tr, en, es, fr, de
  highlightColor: '#FFFF00', // Word highlight color
  strokeWidth: 2,            // Text outline
  shadowOffset: 2,           // Drop shadow
  safeAreaPercent: 10        // Margin from edges
}
```

### Presets

#### TikTok Preset
- Large font (32px)
- Bold white text
- Black background
- Pop animation
- Yellow word highlight

#### Reels Preset
- Medium font (28px)
- Semi-transparent background
- Slide animation
- Cyan word highlight

#### Shorts Preset
- Standard font (24px)
- Solid black background
- Fade animation
- Gold word highlight

## 📡 API Integration

### OpenAI Whisper API

The plugin uses OpenAI's Whisper API for transcription:

- **Model**: whisper-1
- **Supported Formats**: WAV, MP3, OGG, FLAC, M4A
- **Max File Size**: 25MB
- **Languages**: 99+ languages supported

**Cost**: ~$0.006 per minute of audio

To get started:
1. Create an account at [OpenAI](https://openai.com)
2. Generate API key at [platform.openai.com/api-keys](https://platform.openai.com/api-keys)
3. Add credits to your account
4. Enter API key in plugin settings

## 📚 Module Documentation

### settingsManager.js
Manages user preferences and caption settings.

```javascript
const settings = new SettingsManager();
settings.saveSettings({ fontSize: 28 });
settings.applyPreset('tiktok');
```

### premiereAPI.js
Handles Premiere Pro timeline operations.

```javascript
const premiere = new PremierePro();
premiere.initialize();
const times = premiere.getInOutTimes();
premiere.applyCaptions();
```

### whisperAPI.js
Sends audio to OpenAI Whisper for transcription.

```javascript
const whisper = new WhisperAPI(apiKey);
const result = await whisper.transcribeWithTimestamps(audioFile, 'en');
// Returns: { success: true, text: '', words: [{text, start, end}] }
```

### captionGenerator.js
Generates synchronized captions from word-level transcripts.

```javascript
const generator = new CaptionGenerator();
const result = generator.generateCaptions(words, { maxLineLength: 10 });
// Returns: [{ text, startTime, endTime, words: [] }]
```

### animationEngine.js
Generates animation keyframes for captions.

```javascript
const animation = new AnimationEngine();
const keyframes = animation.getAnimationKeyframes('fade', caption);
```

### audioExtractor.js
Extracts and exports audio segments.

```javascript
const extractor = new AudioExtractor();
const result = await extractor.extractFromTimeline(0, 30);
await extractor.exportToWAV(audioData, 'export.wav');
```

## 🎯 Workflow Examples

### Example 1: Turkish Interview Subtitles
```
1. Set In at 00:00:05:00, Out at 00:05:30:00
2. Export audio
3. Select Language: Türkçe
4. Apply TikTok preset
5. Generate captions
6. Apply to timeline
```

### Example 2: YouTube Shorts
```
1. Short segment (0-60 seconds)
2. Use Shorts preset
3. Customize to your brand colors
4. Generate with Pop animation
5. Apply and fine-tune in Premiere
```

## 🐛 Troubleshooting

### "Premiere Pro not initialized"
- Ensure Premiere Pro is open before loading plugin
- Check that you have an active project/sequence

### "Invalid API key"
- Verify API key is entered correctly
- Check key starts with "sk-"
- Test API connection first

### "Audio extraction failed"
- Ensure In/Out points are set correctly
- Check timeline has audio content
- Try exporting smaller segments

### "Transcription timeout"
- Large files may take longer
- Try splitting into shorter segments
- Check internet connection

### Captions not appearing in Premiere
- Verify caption track exists
- Check text is not white on white background
- Try applying from scratch

## 📋 File Structure

```
plugin/
├── index.html              # UI markup
├── main.js                 # Main application logic
├── style.css               # Styling (dark theme)
├── manifest.json           # Plugin configuration
├── package.json            # Metadata
├── README.md              # This file
├── icons/                 # Plugin icons
│   ├── dark.png
│   ├── light.png
│   └── plugin-icon.png
└── modules/               # Modular components
    ├── settingsManager.js
    ├── premiereAPI.js
    ├── whisperAPI.js
    ├── captionGenerator.js
    ├── animationEngine.js
    └── audioExtractor.js
```

## 💡 Tips & Best Practices

1. **Segment Long Videos**: Process videos in 5-10 minute chunks for better accuracy
2. **Use Consistent Settings**: Save presets for your brand
3. **Edit Manually After**: Use Premiere for fine-tuning and styling
4. **Test with Sample**: Use test audio before processing entire projects
5. **Monitor API Usage**: Keep track of Whisper API minutes for cost management
6. **Backup Settings**: Export settings regularly for backup

## 🔄 Keyboard Shortcuts

- **Alt+I**: Set In Point (coming soon)
- **Alt+O**: Set Out Point (coming soon)
- **Alt+T**: Transcribe (coming soon)

## 🌍 Supported Languages

- Türkçe (Turkish)
- English
- Español (Spanish)
- Français (French)
- Deutsch (German)
- + 95 more languages via Whisper API

## 📝 Export Formats

The plugin supports exporting captions in:
- VTT (WebVTT)
- SRT (SubRip)
- Premiere Pro native format
- Custom JSON

## 🤝 Contributing

To contribute improvements:

1. Test your changes thoroughly
2. Follow the modular architecture
3. Update documentation
4. Submit pull requests

## 📄 License

Copyright © 2025. All Rights Reserved.

This plugin is provided as-is for use with Adobe Premiere Pro.

## 📞 Support

For issues or questions:
- Check troubleshooting section above
- Review module documentation
- Verify API key is valid
- Check Premiere Pro version compatibility

## 🚀 Roadmap

- [ ] Real-time preview of captions
- [ ] Batch processing multiple files
- [ ] Custom font library support
- [ ] Multi-language mixing
- [ ] Direct Premiere caption track creation
- [ ] Automatic color grading for readability
- [ ] SRT/VTT import for comparison
- [ ] Caption style templates marketplace

---

**Magic Sub v1.0.0** | Built for Adobe Premiere Pro 26.2.2+
