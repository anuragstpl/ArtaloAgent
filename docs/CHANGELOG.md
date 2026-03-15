# Changelog

All notable changes to ArtaloBot will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2024-XX-XX

### Added
- **Multi-LLM Support**
  - Ollama integration for local models
  - OpenAI API support (GPT-4o, GPT-4o-mini)
  - Google Gemini API support

- **Knowledge Base Agents**
  - Create custom AI agents with specific knowledge
  - Document upload and processing (PDF, TXT, DOCX, CSV, JSON, XML, MD)
  - Semantic chunking with LLM
  - Vector similarity search for knowledge retrieval

- **Multi-Channel Communication**
  - WhatsApp integration via Baileys
  - Telegram Bot API
  - Discord Gateway WebSocket
  - Slack Socket Mode
  - Viber Bot API
  - LINE Messaging API
  - Facebook Messenger Send API

- **Per-Channel LLM Configuration**
  - Different LLM providers per channel
  - Customizable model, temperature, and max tokens

- **MCP Skills System**
  - Connect to MCP-compatible tool servers
  - Auto-discovery of available tools
  - Built-in skills: Calculator, DateTime, Weather, Web Search

- **Memory & Context**
  - Long-term vector memory
  - Session-based conversation history
  - Intelligent context injection

- **Professional UI**
  - Clean Windows 11-inspired design
  - Material Design components
  - Markdown rendering with syntax highlighting
  - Real-time streaming responses
  - Debug console for troubleshooting

### Technical Details
- Built with .NET 8 and WPF
- MVVM architecture with CommunityToolkit.Mvvm
- SQLite database with Entity Framework Core
- MaterialDesignInXAML for UI components

---

## Future Releases

### [1.1.0] - Planned
- Voice input/output support
- Image generation integration
- Microsoft Teams channel

### [1.2.0] - Planned
- Plugin system for user extensions
- Cloud sync for settings
- Multi-language UI support
