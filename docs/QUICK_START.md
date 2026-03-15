# ArtaloBot Quick Start Guide

## Installation

### Option 1: Installer (Recommended)
1. Download `ArtaloBot-Setup-1.0.0.exe` from the releases page
2. Run the installer and follow the wizard
3. Launch ArtaloBot from Start Menu or Desktop

### Option 2: Portable
1. Download `ArtaloBot-Portable.zip`
2. Extract to any folder
3. Run `ArtaloBot.App.exe`

---

## First Time Setup

### Step 1: Install Ollama (For Local AI)

1. Download Ollama from [ollama.ai](https://ollama.ai)
2. Install and run Ollama
3. Open Command Prompt and run:
   ```
   ollama pull qwen2.5:3b
   ```
4. ArtaloBot will automatically detect Ollama

### Step 2: Launch ArtaloBot

1. Start the application
2. You should see the Chat interface
3. Select "Ollama" as the provider
4. Select your downloaded model from the dropdown

### Step 3: Start Chatting!

Type a message and press Enter. The AI will respond.

---

## Setting Up Cloud AI Providers

### OpenAI (GPT-4)

1. Go to **Settings** tab
2. Enter your OpenAI API key
3. Click **Save**
4. In Chat, select "OpenAI" as provider

### Google Gemini

1. Go to **Settings** tab
2. Enter your Gemini API key
3. Click **Save**
4. In Chat, select "Gemini" as provider

---

## Creating a Knowledge Agent

1. Go to **Agents** tab
2. Click **Create Agent**
3. Enter:
   - **Name**: e.g., "Company FAQ"
   - **Description**: What knowledge it contains
4. Click **Add Documents**
5. Select PDF, TXT, DOCX, or other supported files
6. Wait for processing (green checkmark = ready)
7. In Chat, select your agent from "Knowledge Source"

---

## Connecting WhatsApp

### Prerequisites
- Node.js 18+ installed

### Steps
1. Go to **Channels** tab
2. Click on **WhatsApp**
3. Click **Connect**
4. Scan the QR code with WhatsApp mobile app:
   - Open WhatsApp on phone
   - Go to Settings > Linked Devices
   - Tap "Link a Device"
   - Scan the QR code

### Assigning Agents to WhatsApp
1. In **Channels** tab, select WhatsApp
2. In the right panel, click **Assign** next to an agent
3. Now WhatsApp messages will use that agent's knowledge

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Enter | Send message (with debounce) |
| Shift+Enter | Send immediately |
| Ctrl+N | New chat |

---

## Troubleshooting

### "Cannot connect to Ollama"
- Make sure Ollama is running: `ollama serve`
- Check it's accessible: http://localhost:11434

### "No models available"
- Pull a model: `ollama pull qwen2.5:3b`

### WhatsApp QR code not showing
- Install Node.js 18+
- Restart the application

### Agent not answering from knowledge
- Make sure documents are processed (green checkmark)
- Select the agent in Chat > Knowledge Source
- Try asking questions related to your documents

---

## Getting Help

- Check the [README](../README.md) for detailed documentation
- Open an issue on GitHub for bugs
- Join our community for discussions

---

Happy chatting! 🤖
